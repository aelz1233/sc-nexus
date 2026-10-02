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

    public async Task SaveAsync(IEnumerable<ValueObservation> values, IEnumerable<TypedObservation> records,
        CancellationToken token = default)
    {
        var rows = values.Select(x => Row("value", x.Key, new PersistedValue(x.Value, x.Unit), x.Source, x.Timestamp, x.Confidence))
            .Concat(records.Select(x => Row(x.Kind, x.Key, x.Value, x.Source, x.Timestamp, x.Confidence)))
            .ToArray();
        if (rows.Length == 0) return;
        await _gate.WaitAsync(token);
        try
        {
            await using var db = settingsService.CreateDbContext();
            var keys = rows.Select(x => x.RecordKey).Distinct(StringComparer.Ordinal).ToArray();
            var savedRows = await db.DataObservations.AsNoTracking().Where(x => keys.Contains(x.RecordKey)).ToArrayAsync(token);
            var existing = savedRows
                .GroupBy(x => (x.Kind, x.RecordKey))
                .ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.TimestampUtc).First());
            var fingerprints = savedRows.Select(x => x.Fingerprint).ToHashSet(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                if (existing.TryGetValue((row.Kind, row.RecordKey), out var latest) &&
                    latest.Source == row.Source && latest.PayloadJson == row.PayloadJson) continue;
                if (fingerprints.Add(row.Fingerprint))
                    db.DataObservations.Add(row);
                existing[(row.Kind, row.RecordKey)] = row;
            }
            await db.SaveChangesAsync(token);
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<DataObservation>> LoadLatestAsync(int limit = 500,
        CancellationToken token = default)
    {
        await using var db = settingsService.CreateDbContext();
        var rows = await db.DataObservations.AsNoTracking().ToArrayAsync(token);
        return rows.OrderByDescending(x => x.TimestampUtc).Take(Math.Clamp(limit, 1, 5000)).ToArray();
    }

    public static bool TryReadValue(DataObservation row, out string value, out string? unit)
    {
        value = ""; unit = null;
        if (row.Kind != "value") return false;
        try
        {
            var saved = JsonSerializer.Deserialize<PersistedValue>(row.PayloadJson, Json);
            if (saved is null) return false;
            value = saved.Value; unit = saved.Unit; return true;
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
            TimestampUtc = timestamp.ToUniversalTime(), Confidence = Math.Clamp(confidence, 0, 1),
            Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))
        };
    }

    private sealed record PersistedValue(string Value, string? Unit);
}
