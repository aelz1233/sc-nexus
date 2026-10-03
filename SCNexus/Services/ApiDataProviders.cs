using SCNexus.Models;

namespace SCNexus.Services;

public sealed class UexProvider(GameDataService gameDataService) : IDataProvider
{
    public string Name => "UEX market data";
    public DataSourceKind Source => DataSourceKind.Uex;
    public int Priority => 3;
    public TimeSpan RefreshInterval => TimeSpan.FromMinutes(5);

    public async Task<DataProviderResult> CollectAsync(DataProviderContext context, CancellationToken token)
    {
        var data = await gameDataService.GetSnapshotAsync(token: token, forceRefresh: context.ForceRefresh);
        var newest = data.Quotes.Count == 0 ? data.PricesFetchedAt : data.Quotes.Max(x => Epoch(x.DateModified));
        return new DataProviderResult
        {
            Values =
            [
                new("market.prices", $"{data.Quotes.Count} quotes", Source, newest, data.UsedOldCache ? .65 : .9),
                new("market.terminals", data.Terminals.Count.ToString(), Source, data.TerminalsFetchedAt, data.UsedOldCache ? .65 : .9)
            ],
            Status = data.UsedOldCache ? "Cached market data" : $"{data.Quotes.Count} prices"
        };
    }

    private static DateTimeOffset Epoch(long value) => value > 10_000_000_000
        ? DateTimeOffset.FromUnixTimeMilliseconds(Math.Max(0, value))
        : DateTimeOffset.FromUnixTimeSeconds(Math.Max(0, value));
}

public sealed class SCWikiProvider(ShipComponentCatalogService catalogService) : IDataProvider
{
    public string Name => "Star Citizen Wiki";
    public DataSourceKind Source => DataSourceKind.StarCitizenWiki;
    public int Priority => 4;
    public TimeSpan RefreshInterval => TimeSpan.FromMinutes(2);

    public async Task<DataProviderResult> CollectAsync(DataProviderContext context, CancellationToken token)
    {
        var ship = context.CurrentState.CurrentShip?.Value;
        if (string.IsNullOrWhiteSpace(ship)) return new DataProviderResult { Status = "Waiting for current ship" };
        var catalog = await catalogService.LoadAsync(ship, token);
        var record = new ComponentCatalogSnapshot
        {
            ShipName = catalog.ShipName, GameVersion = catalog.GameVersion,
            Slots = catalog.Slots.Count, Components = catalog.Components.Count
        };
        return new DataProviderResult
        {
            Values = [new("ship.components", $"{catalog.Components.Count} components", Source, catalog.FetchedAt,
                catalog.UsedOldCache ? .65 : .92, DataVersion: catalog.GameVersion)],
            Records = [new("component-catalog", ship, record, Source, catalog.FetchedAt, catalog.UsedOldCache ? .65 : .92)],
            Status = $"{catalog.Slots.Count} slots for {catalog.ShipName}"
        };
    }
}

public sealed class ManualDataProvider(SettingsService settingsService) : IDataProvider
{
    public string Name => "Saved manual values";
    public DataSourceKind Source => DataSourceKind.Manual;
    public int Priority => 7;
    public TimeSpan RefreshInterval => TimeSpan.FromMinutes(2);

    public async Task<DataProviderResult> CollectAsync(DataProviderContext context, CancellationToken token)
    {
        var settings = await settingsService.LoadAsync();
        var timestamp = DateTimeOffset.UtcNow;
        var values = new List<ValueObservation>();
        if (!string.IsNullOrWhiteSpace(settings.CurrentShip) && settings.CurrentShip != "Не выбран")
            values.Add(new("player.ship", settings.CurrentShip, Source, timestamp, 1));
        if (!string.IsNullOrWhiteSpace(settings.CurrentLocation) && settings.CurrentLocation != "Не указана")
            values.Add(new("player.location", settings.CurrentLocation, Source, timestamp, 1));
        if (!string.IsNullOrWhiteSpace(settings.CurrentSystem)) values.Add(new("player.system", settings.CurrentSystem, Source, timestamp, 1));
        values.Add(new("player.balance", settings.Balance.ToString(System.Globalization.CultureInfo.InvariantCulture), Source, timestamp, 1, "aUEC"));
        return new DataProviderResult { Values = values, Status = values.Count == 0 ? "No manual fallback values" : $"{values.Count} fallback values" };
    }
}

public sealed class NexusHistoryProvider(DataHistoryService history) : IDataProvider
{
    public string Name => "Nexus history";
    public DataSourceKind Source => DataSourceKind.NexusHistory;
    public int Priority => 6;
    public TimeSpan RefreshInterval => TimeSpan.FromMinutes(2);

    public async Task<DataProviderResult> CollectAsync(DataProviderContext context, CancellationToken token)
    {
        var rows = await history.LoadRecoverySnapshotAsync(800, token);
        // Legacy balances may have been inferred from transfer/price notifications. Never replay them as a wallet value.
        var values = rows.Where(x => x.Kind == "value" && x.RecordKey != "player.balance").GroupBy(x => x.RecordKey)
            .Select(x => x.OrderByDescending(y => y.TimestampUtc).First())
            .Select(x => DataHistoryService.TryReadValue(x, out var value, out var unit, out var version)
                ? new ValueObservation(x.RecordKey, value, Source, x.TimestampUtc, Math.Min(.6, x.Confidence * .7), unit, version) : null)
            .Where(x => x is not null).Select(x => x!).ToArray();
        var records = new List<TypedObservation>();
        foreach (var row in rows.Where(x => x.Kind != "value").GroupBy(x => (x.Kind, x.RecordKey)).Select(x => x.First()))
        {
            object? value = row.Kind switch
            {
                "session" => DataHistoryService.ReadRecord<GameSession>(row),
                "ship" => DataHistoryService.ReadRecord<DetectedShip>(row),
                "mission" => DataHistoryService.ReadRecord<MissionState>(row),
                "trade" => DataHistoryService.ReadRecord<TradeEvent>(row),
                "movement" => DataHistoryService.ReadRecord<MovementEvent>(row),
                "death" => DataHistoryService.ReadRecord<DeathEvent>(row),
                "component-catalog" => DataHistoryService.ReadRecord<ComponentCatalogSnapshot>(row),
                _ => null
            };
            if (value is not null) records.Add(new TypedObservation(row.Kind, row.RecordKey, value,
                Source, row.TimestampUtc, Math.Min(.6, row.Confidence * .7)));
        }
        return new DataProviderResult { Values = values, Records = records, Status = $"{rows.Count} saved observations" };
    }
}
