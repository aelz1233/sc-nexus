using System.Globalization;
using System.IO;
using System.Net.Http;
using SCNexus.Models;

namespace SCNexus.Services;

public sealed class DataCollectionService(IEnumerable<IDataProvider> providers, DataHistoryService history,
    GameLogService gameLogService)
{
    private readonly IDataProvider[] _providers = providers.OrderBy(x => x.Priority).ToArray();
    private readonly Dictionary<string, ValueObservation> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Kind, string Key), TypedObservation> _records = new();
    private readonly Dictionary<string, ProviderState> _providerStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SemaphoreSlim> _providerGates = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _stateGate = new();

    public DataCollectionSnapshot Current { get; private set; } = new();
    public event EventHandler<DataCollectionSnapshot>? SnapshotUpdated;

    public async Task WatchAsync(Func<bool> monitoringEnabled, Func<bool> ocrEnabled, CancellationToken token)
    {
        var tasks = _providers.Select(provider => WatchProviderAsync(provider, monitoringEnabled, ocrEnabled, token)).ToArray();
        try { await Task.WhenAll(tasks); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    public async Task RefreshAsync(bool ocrEnabled, CancellationToken token = default) =>
        await Task.WhenAll(_providers.Select(x => CollectProviderAsync(x, ocrEnabled, token, true)));

    public async Task<DataProviderResult?> RefreshOcrAsync(CancellationToken token = default,
        bool scanFleet = false, IProgress<FleetScanSummary>? progress = null)
    {
        var provider = _providers.FirstOrDefault(x => x.Source == DataSourceKind.Ocr);
        if (provider is null) return null;
        return scanFleet && provider is OcrProvider ocr
            ? await CollectProviderAsync(provider, true, token, true, (context, cancellation) => ocr.DetectShipsAsync(context, progress, cancellation), TimeSpan.FromSeconds(120))
            : await CollectProviderAsync(provider, ocrEnabled: true, token, forceRefresh: true);
    }

    private async Task WatchProviderAsync(IDataProvider provider, Func<bool> monitoringEnabled,
        Func<bool> ocrEnabled, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (!monitoringEnabled())
            {
                PublishPaused(provider);
                try { await Task.Delay(TimeSpan.FromSeconds(2), token); }
                catch (OperationCanceledException) { break; }
                continue;
            }
            await CollectProviderAsync(provider, ocrEnabled(), token);
            try { await Task.Delay(provider.RefreshInterval, token); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task<DataProviderResult?> CollectProviderAsync(IDataProvider provider, bool ocrEnabled, CancellationToken token,
        bool forceRefresh = false, Func<DataProviderContext, CancellationToken, Task<DataProviderResult>>? collect = null,
        TimeSpan? operationTimeout = null)
    {
        SemaphoreSlim gate;
        lock (_stateGate)
        {
            if (!_providerGates.TryGetValue(provider.Name, out gate!))
                _providerGates[provider.Name] = gate = new SemaphoreSlim(1, 1);
        }
        if (!await gate.WaitAsync(0, token)) return null;
        try
        {
            var now = DateTimeOffset.UtcNow;
            ProviderState? known;
            DataProviderContext context;
            lock (_stateGate)
            {
                _providerStates.TryGetValue(provider.Name, out known);
                _providerStates[provider.Name] = new ProviderState(now, known?.LastSuccess, false, "Updating");
                context = new DataProviderContext(gameLogService.ResolveGameDirectory(), BuildPlayerState(), now,
                    ocrEnabled, forceRefresh);
            }
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(operationTimeout ?? ProviderTimeout(provider));
                var result = await (collect is null ? provider.CollectAsync(context, timeout.Token) : collect(context, timeout.Token));
                await history.SaveAsync(result.Values.Where(x => x.Source != DataSourceKind.NexusHistory),
                    result.Records.Where(x => x.Source != DataSourceKind.NexusHistory), timeout.Token);
                Publish(provider, now, true, result.Status, result.Values, result.Records, known?.LastSuccess, known);
                return result;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (OperationCanceledException)
            {
                Publish(provider, now, false, "Timed out", [], [], known?.LastSuccess, known);
            }
            catch (Exception ex) { Publish(provider, now, false, FriendlyError(ex), [], [], known?.LastSuccess); }
        }
        finally { gate.Release(); }
        return null;
    }

    private void PublishPaused(IDataProvider provider)
    {
        DataCollectionSnapshot? snapshot = null;
        lock (_stateGate)
        {
            _providerStates.TryGetValue(provider.Name, out var previous);
            if (previous?.Status == "Paused") return;
            _providerStates[provider.Name] = new ProviderState(DateTimeOffset.UtcNow,
                previous?.LastSuccess, false, "Paused");
            Current = snapshot = BuildSnapshot();
        }
        SnapshotUpdated?.Invoke(this, snapshot);
    }

    private static TimeSpan ProviderTimeout(IDataProvider provider) => TimeSpan.FromSeconds(
        Math.Clamp(provider.RefreshInterval.TotalSeconds * 2, 15, 120));

    private void Publish(IDataProvider provider, DateTimeOffset attempt, bool available, string status,
        IReadOnlyList<ValueObservation> values, IReadOnlyList<TypedObservation> records,
        DateTimeOffset? previousSuccess = null, ProviderState? previousState = null)
    {
        DataCollectionSnapshot snapshot;
        lock (_stateGate)
        {
            var changed = false;
            foreach (var value in values) changed |= MergeValue(value);
            foreach (var record in records) changed |= MergeRecord(record);
            _providerStates[provider.Name] = new ProviderState(attempt,
                available ? DateTimeOffset.UtcNow : previousSuccess, available, status);
            if (!changed && previousState is not null && previousState.Available == available && previousState.Status == status)
                return;
            PruneRecords();
            Current = snapshot = BuildSnapshot();
        }
        SnapshotUpdated?.Invoke(this, snapshot);
    }

    private bool MergeValue(ValueObservation incoming)
    {
        if (_values.TryGetValue(incoming.Key, out var existing) && !Prefer(incoming.Key, incoming.Source, incoming.Timestamp, incoming.Confidence,
                existing.Source, existing.Timestamp, existing.Confidence)) return false;
        _values[incoming.Key] = incoming;
        return true;
    }

    private bool MergeRecord(TypedObservation incoming)
    {
        var key = (incoming.Kind, incoming.Key);
        if (_records.TryGetValue(key, out var existing) && !Prefer(incoming.Kind, incoming.Source, incoming.Timestamp, incoming.Confidence,
                existing.Source, existing.Timestamp, existing.Confidence)) return false;
        _records[key] = incoming;
        return true;
    }

    private static bool Prefer(string key, DataSourceKind incomingSource, DateTimeOffset incomingTime, double incomingConfidence,
        DataSourceKind existingSource, DateTimeOffset existingTime, double existingConfidence)
    {
        if (incomingSource == existingSource) return incomingTime >= existingTime && incomingConfidence >= existingConfidence * .75;
        // A historical high-priority log entry must never replace a newer correction or observation.
        if (incomingTime < existingTime - TimeSpan.FromMinutes(2)) return false;
        var existingStale = DateTimeOffset.UtcNow - existingTime > FreshnessFor(key);
        if (existingStale && incomingTime > existingTime && incomingConfidence >= existingConfidence) return true;
        return (int)incomingSource < (int)existingSource && incomingConfidence >= .6;
    }

    private static TimeSpan FreshnessFor(string key) => key switch
    {
        "player.location" or "player.system" or "player.ship" or "player.loadout" => TimeSpan.FromMinutes(30),
        "player.balance" => TimeSpan.FromHours(1),
        "market.prices" or "market.terminals" => TimeSpan.FromHours(2),
        "game.build" or "game.environment" => TimeSpan.FromDays(30),
        "session" or "movement" => TimeSpan.FromHours(12),
        _ => TimeSpan.FromHours(6)
    };

    private void PruneRecords()
    {
        foreach (var (kind, limit) in new[]
                 {
                     ("session", 200), ("ship", 200), ("mission", 500), ("trade", 1000),
                     ("movement", 1000), ("death", 500), ("component-catalog", 50)
                 })
        {
            var excess = _records.Where(x => x.Key.Kind == kind)
                .OrderByDescending(x => x.Value.Timestamp).Skip(limit).Select(x => x.Key).ToArray();
            foreach (var key in excess) _records.Remove(key);
        }
    }

    private DataCollectionSnapshot BuildSnapshot() => new()
    {
        Player = BuildPlayerState(), Sessions = Records<GameSession>("session", 100),
        Ships = Records<DetectedShip>("ship", 100), Missions = Records<MissionState>("mission", 200),
        Trades = Records<TradeEvent>("trade", 500), Movements = Records<MovementEvent>("movement", 500),
        Deaths = Records<DeathEvent>("death", 200),
        Values = new Dictionary<string, ValueObservation>(_values, StringComparer.OrdinalIgnoreCase),
        Sources = _providers.Select(provider =>
        {
            _providerStates.TryGetValue(provider.Name, out var state);
            return new DataSourceInfo
            {
                Name = provider.Name, Source = provider.Source, Priority = provider.Priority,
                LastSuccess = state?.LastSuccess, Status = state?.Status ?? "Waiting",
                IsAvailable = state?.Available == true
            };
        }).ToArray(),
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private PlayerState BuildPlayerState() => new()
    {
        GameBuild = Text("game.build"), Environment = Text("game.environment"),
        CurrentSystem = Text("player.system"), CurrentLocation = Text("player.location"),
        CurrentShip = Text("player.ship"), Balance = Decimal("player.balance"),
        CurrentLoadout = Text("player.loadout")
    };

    private ObservedValue<string>? Text(string key) => _values.TryGetValue(key, out var item)
        ? new ObservedValue<string>(item.Value, item.Source, item.Timestamp, item.Confidence) : null;

    private ObservedValue<decimal>? Decimal(string key)
    {
        if (!_values.TryGetValue(key, out var item)) return null;
        return decimal.TryParse(item.Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            ? new ObservedValue<decimal>(value, item.Source, item.Timestamp, item.Confidence) : null;
    }

    private IReadOnlyList<T> Records<T>(string kind, int limit) => _records.Values
        .Where(x => x.Kind == kind && x.Value is T).OrderByDescending(x => x.Timestamp)
        .Take(limit).Select(x => (T)x.Value).ToArray();

    private static string FriendlyError(Exception ex) => ex switch
    {
        HttpRequestException => "Network data unavailable", IOException => "File is temporarily unavailable",
        UnauthorizedAccessException => "Read access denied", _ => ex.Message
    };

    private sealed record ProviderState(DateTimeOffset LastAttempt, DateTimeOffset? LastSuccess,
        bool Available, string Status);
}
