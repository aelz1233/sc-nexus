using SCNexus.Services;

namespace SCNexus.Tests;

public class GameLogServiceTests
{
    [Fact]
    public async Task ReadsLocalTradeRequestsWithoutTreatingThemAsCompletedSales()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SCNexusTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var lines = new[]
            {
                "<2026-09-24T19:46:43.557Z> Sending SShopCommodityBuyRequest - playerId[123] shopName[ShopA] price[3500050.000000] quantity[68800.000000 cSCU]",
                "<2026-09-24T20:25:15.838Z> Sending SShopCommoditySellRequest - playerId[123] shopName[ShopB] amount[5052196.000000] quantity[640]",
                "<2026-09-24T20:25:20.000Z> Something unrelated"
            };
            await File.WriteAllLinesAsync(Path.Combine(directory, "Game.log"), lines);
            var snapshot = await new GameLogService(directory).ReadRecentAsync();
            Assert.Equal(directory, snapshot.GameDirectory);
            Assert.Equal(2, snapshot.Candidates.Count);
            Assert.False(snapshot.Candidates[0].IsPurchase);
            Assert.Equal(5_052_196, snapshot.Candidates[0].Amount);
            Assert.True(snapshot.Candidates[1].IsPurchase);
            Assert.Equal(3_500_050, snapshot.Candidates[1].Amount);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task CanUseManuallySelectedGameDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SCNexusTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "Game.log"),
                "<2026-09-24T19:46:43.557Z> Sending SShopCommodityBuyRequest shopName[Shop] price[100] quantity[100]");
            var service = new GameLogService { GameDirectoryOverride = directory };
            var snapshot = await service.ReadRecentAsync();
            Assert.Equal(directory, snapshot.GameDirectory);
            Assert.Single(snapshot.Candidates);
        }
        finally { Directory.Delete(directory, true); }
    }
}
