using System.Windows;
using SCNexus.Models;
using SCNexus.Services;

namespace SCNexus.Tests;

public class FleetTerminalScanTests
{
    private static readonly VehicleCatalogItem[] Ships =
    [
        new() { Id = 1, Name = "Guardian", IsSpaceship = 1, IsMilitary = 1 },
        new() { Id = 2, Name = "Guardian MX", IsSpaceship = 1, IsMilitary = 1 },
        new() { Id = 3, Name = "C1 Spirit", NameFull = "Crusader C1 Spirit", IsSpaceship = 1, IsCargo = 1 },
        new() { Id = 4, Name = "Vulture", NameFull = "Drake Vulture", IsSpaceship = 1, IsSalvage = 1 }
    ];

    [Theory]
    [InlineData("Locked")]
    [InlineData("ЗАБЛОКИРОВАНО")]
    public void LockedRowIsExcludedWithoutExcludingItsNeighbors(string status)
    {
        var text = $"МЕНЕДЖЕР ПАРКА ТЕХНИКИ\nGuardian MX\n{status}\nCrusader C1 Spirit\nВосстановление\nDrake Vulture\nДоставка";
        var result = OcrProvider.ParseText(text, Ships, DateTimeOffset.UtcNow);
        Assert.Equal(new[] { "C1 Spirit", "Vulture" }, result.Records.Select(x => ((DetectedShip)x.Value).Name.Value));
        Assert.Equal("Утилизация", ((DetectedShip)result.Records[1].Value).Role!.Value);
    }

    [Fact]
    public void UnlockedCopyCountsOnceEvenWhenAnotherCopyIsLocked()
    {
        var result = OcrProvider.ParseText("ASOP\nCrusader C1 Spirit - 2\nLocked\nCrusader C1 Spirit\nStored\nC1 Spirit\nClaim", Ships, DateTimeOffset.UtcNow);
        Assert.Equal("C1 Spirit", ((DetectedShip)Assert.Single(result.Records).Value).Name.Value);
        Assert.DoesNotContain(result.Values, x => x.Key == "player.ship");
    }

    [Fact]
    public void RussianClaimIsIncludedButLockedOverridesAvailableAndRetrieve()
    {
        var result = OcrProvider.ParseText("МЕНЕДЖЕР ПАРКА ТЕХНИКИ\nGuardian MX\nВозместить\nC1 Spirit — 2\nЗАБЛОКИРОВАНО Доступно Извлечь\nVulture\nНа хранении", Ships, DateTimeOffset.UtcNow);
        Assert.Equal(new[] { "Guardian MX", "Vulture" }, result.Records.Select(x => ((DetectedShip)x.Value).Name.Value));
    }

    [Fact]
    public void UnreadableStatusIsNotTreatedAsUnlocked()
    {
        var result = OcrProvider.ParseText("ASOP\nGuardian MX\n???\nC1 Spirit\nStored", Ships, DateTimeOffset.UtcNow);
        Assert.Equal("C1 Spirit", ((DetectedShip)Assert.Single(result.Records).Value).Name.Value);
    }

    [Fact]
    public void FuzzyShipNameSurvivesSmallOcrErrors()
    {
        var result = OcrProvider.ParseText("Fleet Manag3r\nGuardlan MX\nStored\nCrusader C1 Sp1rit\nClaim", Ships, DateTimeOffset.UtcNow);
        Assert.Equal(new[] { "Guardian MX", "C1 Spirit" },
            result.Records.Select(x => ((DetectedShip)x.Value).Name.Value));
    }

    [Fact]
    public void ShopHeadingIsNotEvidenceOfPlayerFleet()
    {
        Assert.Empty(OcrProvider.ParseText("Корабли\nКупить\nGuardian MX", Ships, DateTimeOffset.UtcNow).Records);
    }

    [Fact]
    public void BilingualOcrUsesLockedStatusAtTheSameRowPosition()
    {
        var lines = new OcrProvider.ScreenLine[]
        {
            new("Fleet Manager", new Rect(20, 10, 100, 20)),
            new("Guardian MX", new Rect(20, 100, 150, 20)),
            new("Guardian", new Rect(20, 102, 120, 20)),
            new("ЗАБЛОКИРОВАНО", new Rect(20, 126, 150, 20)),
            new("Vulture", new Rect(20, 250, 100, 20)),
            new("Stored", new Rect(700, 252, 100, 20))
        };
        var screen = OcrProvider.ReadFleetScreen(lines, Ships);
        Assert.True(screen.IsTerminal);
        Assert.Equal(2, screen.Rows.Count);
        Assert.Equal("Guardian MX", screen.Rows[0].Vehicle.Name);
        Assert.True(screen.Rows[0].Locked);
        Assert.False(screen.Rows[1].Locked);
    }

    [Fact]
    public async Task ScanReachesTopThenBottomAndDeduplicatesModels()
    {
        var top = Page(Ships[0]);
        var middle = Page(Ships[1]);
        var bottom = Page(Ships[3]);
        var pages = new Queue<FleetScreen>([top, top, top, middle, bottom, bottom, bottom]);
        var directions = new List<int>();
        var result = await FleetTerminalScan.RunAsync(middle, _ => Task.FromResult(pages.Dequeue()),
            (_, direction, _) => { directions.Add(direction); return Task.FromResult(true); }, null, default);
        Assert.Equal(new[] { 1, 1, 1, -1, -1, -1, -1 }, directions);
        Assert.Equal(3, result.Vehicles.Count);
        Assert.Equal("end-of-list", result.Summary.StopReason);
    }

    [Fact]
    public async Task ChangedScreenStopsBeforeSendingMoreInput()
    {
        var scrolls = 0;
        var result = await FleetTerminalScan.RunAsync(Page(Ships[0]), _ => Task.FromResult(new FleetScreen("Flight HUD", [], false)),
            (_, _, _) => { scrolls++; return Task.FromResult(true); }, null, default);
        Assert.Equal(1, scrolls);
        Assert.Equal("screen-changed", result.Summary.StopReason);
        Assert.Single(result.Vehicles);
    }

    [Fact]
    public async Task CancellationRetainsAlreadyReadModelsAndStopsInput()
    {
        using var cancellation = new CancellationTokenSource();
        var scrolls = 0;
        var result = await FleetTerminalScan.RunAsync(Page(Ships[0]), token => { token.ThrowIfCancellationRequested(); return Task.FromResult(Page(Ships[1])); },
            (_, _, _) => { scrolls++; cancellation.Cancel(); return Task.FromResult(true); }, null, cancellation.Token);
        Assert.Single(result.Vehicles);
        Assert.Equal(1, scrolls);
        Assert.Equal("cancelled", result.Summary.StopReason);
    }

    [Fact]
    public async Task EmptyOrUnknownScreenNeverScrolls()
    {
        var result = await FleetTerminalScan.RunAsync(new FleetScreen("ASOP", [], true), _ => throw new InvalidOperationException(),
            (_, _, _) => throw new InvalidOperationException(), null, default);
        Assert.Empty(result.Vehicles);
        Assert.Equal("unrecognized", result.Summary.StopReason);
    }

    private static FleetScreen Page(VehicleCatalogItem ship) => new("Fleet Manager", [new(ship, false, new Rect(20, 200, 100, 20))], true);
}
