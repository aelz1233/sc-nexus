using SCNexus.Models;
using SCNexus.Services;

namespace SCNexus.Tests;

public class DataCollectionTests
{
    [Fact]
    public async Task GameLogProviderReadsOnlyAppendedLinesAfterInitialTail()
    {
        var directory = TempDirectory();
        try
        {
            var log = Path.Combine(directory, "Game.log");
            await File.WriteAllTextAsync(log,
                "<2026-10-02T10:00:00Z> Sending SShopCommodityBuyRequest shopName[Area18] price[1000] quantity[10]\n");
            var service = new GameLogService(directory);
            var provider = new GameLogProvider(service);
            var context = new DataProviderContext(directory, new PlayerState(), DateTimeOffset.UtcNow, false);

            var first = await provider.CollectAsync(context, default);
            var second = await provider.CollectAsync(context, default);
            await File.AppendAllTextAsync(log,
                "<2026-10-02T10:01:00Z> Sending SShopCommoditySellRequest shopName[Orison] amount[1400] quantity[10]\n");
            var third = await provider.CollectAsync(context, default);

            Assert.Single(first.Records, x => x.Kind == "trade");
            Assert.Empty(second.Records);
            Assert.Single(third.Records, x => x.Kind == "trade");
            Assert.Equal("sale", Assert.IsType<TradeEvent>(third.Records.Single().Value).Action.Value);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task LocalProviderDetectsEnvironmentAndBuildReadOnly()
    {
        var root = TempDirectory();
        var directory = Path.Combine(root, "PTU");
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "build_manifest.id"), "4.5.0-PTU.1234567");
            await File.WriteAllTextAsync(Path.Combine(directory, "Game.log"), "Connected to pub_euw1a_123");
            var provider = new LocalGameDataProvider(new GameLogService(directory));
            var result = await provider.CollectAsync(new DataProviderContext(directory, new PlayerState(), DateTimeOffset.UtcNow, false), default);

            Assert.Contains(result.Values, x => x.Key == "game.environment" && x.Value == "PTU" && x.Confidence == .99);
            Assert.Contains(result.Values, x => x.Key == "game.build" && x.Value == "4.5.0-PTU.1234567");
            Assert.Single(result.Records, x => x.Kind == "session");
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task GameLogProviderParsesRealBalanceLocationAndMissionFormats()
    {
        var directory = TempDirectory();
        try
        {
            await File.WriteAllLinesAsync(Path.Combine(directory, "Game.log"),
            [
                "<2026-10-01T20:56:41.291Z> 23,800,000 aUEC",
                "<2026-10-01T22:29:38.410Z> [Notice] <RequestLocationInventory> Player[Test] requested inventory for Location[Stanton2_Orison] [Inventory]",
                "<2026-10-01T22:43:59.110Z> [Notice] <CLocalMissionPhaseMarker::CreateMarker> Creating objective marker: missionId [f3fd326d-1ac1-4cda-837c-a7c1541b0aba], generator name [BountyHuntersGuild_KIllShip], contract [BountyHuntersGuild_Bounty_Stanton_Easy_0], objectiveId [bab951fe-3b6c-c746-b83d-7c214c834726]",
                "<2026-10-01T22:46:00.770Z> [Notice] <MissionEnded> Received MissionEnded push message for: mission_id f3fd326d-1ac1-4cda-837c-a7c1541b0aba - mission_state MISSION_STATE_COMPLETED [Missions]"
            ]);
            var provider = new GameLogProvider(new GameLogService(directory));
            var result = await provider.CollectAsync(new DataProviderContext(directory, new PlayerState(), DateTimeOffset.UtcNow, false), default);

            Assert.Contains(result.Values, x => x.Key == "player.balance" && x.Value == "23800000");
            Assert.Contains(result.Values, x => x.Key == "player.location" && x.Value == "Orison");
            var mission = Assert.IsType<MissionState>(result.Records.Last(x => x.Kind == "mission").Value);
            Assert.Equal("completed", mission.Status.Value);
            Assert.Equal("BountyHuntersGuild_Bounty_Stanton_Easy_0", mission.Name.Value);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task HistoryPersistsValueSourceTimestampAndConfidence()
    {
        var directory = TempDirectory();
        try
        {
            var settings = new SettingsService(Path.Combine(directory, "nexus.db"));
            await settings.LoadAsync();
            var history = new DataHistoryService(settings);
            var time = DateTimeOffset.Parse("2026-10-02T10:00:00Z");
            await history.SaveAsync([new ValueObservation("player.ship", "Guardian MX", DataSourceKind.GameLog, time, .93)], []);
            var rows = await history.LoadLatestAsync();

            var row = Assert.Single(rows);
            Assert.Equal(DataSourceKind.GameLog, row.Source);
            Assert.Equal(.93, row.Confidence, 3);
            Assert.Equal(time, row.TimestampUtc);
            Assert.True(DataHistoryService.TryReadValue(row, out var value, out _));
            Assert.Equal("Guardian MX", value);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task CollectionPrefersGameLogOverHistoryFallback()
    {
        var directory = TempDirectory();
        try
        {
            var settings = new SettingsService(Path.Combine(directory, "nexus.db"));
            await settings.LoadAsync();
            var history = new DataHistoryService(settings);
            var providers = new IDataProvider[]
            {
                new FixedProvider("history", DataSourceKind.NexusHistory, 6, "Old ship", .6),
                new FixedProvider("log", DataSourceKind.GameLog, 1, "Guardian MX", .95)
            };
            var collection = new DataCollectionService(providers, history, new GameLogService(directory));
            await collection.RefreshAsync(false);

            Assert.Equal("Guardian MX", collection.Current.Player.CurrentShip?.Value);
            Assert.Equal(DataSourceKind.GameLog, collection.Current.Player.CurrentShip?.Source);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(directory, true); }
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "SCNexusTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FixedProvider(string name, DataSourceKind source, int priority, string value, double confidence) : IDataProvider
    {
        public string Name => name;
        public DataSourceKind Source => source;
        public int Priority => priority;
        public TimeSpan RefreshInterval => TimeSpan.Zero;
        public Task<DataProviderResult> CollectAsync(DataProviderContext context, CancellationToken token) =>
            Task.FromResult(new DataProviderResult
            {
                Values = [new ValueObservation("player.ship", value, source, DateTimeOffset.UtcNow, confidence)]
            });
    }
}
