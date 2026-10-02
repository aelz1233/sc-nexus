using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SCNexus.Models;

namespace SCNexus.Services;

public sealed class DataHistoryService(SettingsService settingsService)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateOnly? _lastPruneDay;

    public async Task SaveAsync(IEnumerable<ValueObservation> values, IEnumerable<TypedObservation> records,
        CancellationToken token = default)
    {
        var rows = values.Select(x => Row("value", x.Key, new PersistedValue(x.Value, x.Unit, x.DataVersion), x.Source, x.Timestamp, x.Confidence))
            .Concat(records.Select(x => Row(x.Kind, x.Key, x.Value, x.Source, x.Timestamp, x.Confidence)))
            .ToArray();
        if (rows.Length == 0) return;
        await _gate.WaitAsync(token);
        try
        {
            await using var db = settingsService.CreateDbContext();
            var keys = rows.Select(x => x.RecordKey).Distinct(StringComparer.Ordinal).ToArray();
            var savedRows = await db.DataObservations.AsNoTracking()
                .Where(x => keys.Contains(x.RecordKey))
                .GroupBy(x => new { x.Kind, x.RecordKey })
                .Select(x => x.OrderByDescending(y => y.TimestampUnixMs).First())
                .ToArrayAsync(token);
            var existing = savedRows
                .GroupBy(x => (x.Kind, x.RecordKey))
                .ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.TimestampUtc).First());
            var incomingFingerprints = rows.Select(x => x.Fingerprint).Distinct(StringComparer.Ordinal).ToArray();
            var fingerprints = (await db.DataObservations.AsNoTracking()
                    .Where(x => incomingFingerprints.Contains(x.Fingerprint))
                    .Select(x => x.Fingerprint).ToArrayAsync(token))
                .ToHashSet(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                if (existing.TryGetValue((row.Kind, row.RecordKey), out var latest) &&
                    latest.Source == row.Source && latest.PayloadJson == row.PayloadJson) continue;
                if (fingerprints.Add(row.Fingerprint))
                    db.DataObservations.Add(row);
                existing[(row.Kind, row.RecordKey)] = row;
            }
            await db.SaveChangesAsync(token);
            if (_lastPruneDay != DateOnly.FromDateTime(DateTime.UtcNow))
            {
                await PruneAsync(db, token);
                _lastPruneDay = DateOnly.FromDateTime(DateTime.UtcNow);
            }
        }
        finally { _gate.Release(); }
    }

    private static async Task PruneAsync(Data.NexusDbContext db, CancellationToken token)
    {
        var recordCutoff = DateTimeOffset.UtcNow.AddDays(-365).ToUnixTimeMilliseconds();
        await db.DataObservations.Where(x => x.Kind != "value" && x.TimestampUnixMs < recordCutoff)
            .ExecuteDeleteAsync(token);

        var valueRows = await db.DataObservations.AsNoTracking().Where(x => x.Kind == "value")
            .OrderByDescending(x => x.TimestampUnixMs)
            .Select(x => new { x.Id, x.RecordKey }).ToArrayAsync(token);
        var obsolete = valueRows.GroupBy(x => x.RecordKey, StringComparer.OrdinalIgnoreCase)
            .SelectMany(x => x.Skip(30)).Select(x => x.Id).ToArray();
        foreach (var batch in obsolete.Chunk(500))
            await db.DataObservations.Where(x => batch.Contains(x.Id)).ExecuteDeleteAsync(token);
    }

    public async Task<IReadOnlyList<DataObservation>> LoadLatestAsync(int limit = 500,
        CancellationToken token = default)
    {
        await using var db = settingsService.CreateDbContext();
        return await db.DataObservations.AsNoTracking()
            .OrderByDescending(x => x.TimestampUnixMs)
            .Take(Math.Clamp(limit, 1, 5000))
            .ToArrayAsync(token);
    }

    public async Task<IReadOnlyList<DataObservation>> LoadRecoverySnapshotAsync(int recordLimit = 800,
        CancellationToken token = default)
    {
        await using var db = settingsService.CreateDbContext();
        var values = await db.DataObservations.AsNoTracking()
            .Where(x => x.Kind == "value")
            .GroupBy(x => x.RecordKey)
            .Select(x => x.OrderByDescending(y => y.TimestampUnixMs).First())
            .ToArrayAsync(token);
        var records = await db.DataObservations.AsNoTracking()
            .Where(x => x.Kind != "value")
            .OrderByDescending(x => x.TimestampUnixMs)
            .Take(Math.Clamp(recordLimit, 1, 5000))
            .ToArrayAsync(token);
        return values.Concat(records).ToArray();
    }

    public static bool TryReadValue(DataObservation row, out string value, out string? unit)
    {
        return TryReadValue(row, out value, out unit, out _);
    }

    public static bool TryReadValue(DataObservation row, out string value, out string? unit,
        out string? dataVersion)
    {
        value = ""; unit = null; dataVersion = null;
        if (row.Kind != "value") return false;
        try
        {
            var saved = JsonSerializer.Deserialize<PersistedValue>(row.PayloadJson, Json);
            if (saved is null) return false;
            value = saved.Value; unit = saved.Unit; dataVersion = saved.DataVersion; return true;
        }
        catch (JsonException) { return false; }
    }

    public static T? ReadRecord<T>(DataObservation row)
    {
        try { return JsonSerializer.Deserialize<T>(row.PayloadJson, Json); }
        catch (JsonException) { return default; }
    }

    private static DataObservation Row(string kind, string key, object payload, DataSourceKind source,
        DateTimeOffset timestamp, double confidence)
    {
        var json = JsonSerializer.Serialize(payload, payload.GetType(), Json);
        var raw = $"{kind}\n{key}\n{source}\n{timestamp.UtcTicks}\n{json}";
        return new DataObservation
        {
            Kind = kind, RecordKey = key, PayloadJson = json, Source = source,
            TimestampUtc = timestamp.ToUniversalTime(), TimestampUnixMs = timestamp.ToUnixTimeMilliseconds(),
            Confidence = Math.Clamp(confidence, 0, 1),
            Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))
        };
    }

    private sealed record PersistedValue(string Value, string? Unit, string? DataVersion = null);
}
