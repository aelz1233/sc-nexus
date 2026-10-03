using System.Net.Http;
using SCNexus.Models;
using SCNexus.Services;
using SCNexus.ViewModels;

namespace SCNexus.Tests;

public class VoyageGuidanceTests
{
    [Fact]
    public void AdvancesByDetectedLocationAndRequiresEveryActionAtIntermediateStop()
    {
        var route = new HaulingRoute("Laranite", "Area 045", "Everus Harbor", "Stanton", "Stanton",
            10, 20, 10, 20, 100, 100, 100, 200, false, "Trade", DateTimeOffset.UtcNow);
        var plan = new VoyagePlan("Цепочка", [route],
        [
            new(1, "ArcCorp Mining Area 045", "Stanton", "Купить Laranite: 10 SCU за 100 aUEC.", 10, 20, 0),
            new(2, "Admin - Everus Harbor", "Stanton", "Продать Laranite: 10 SCU за 200 aUEC.\nКупить Iodine: 5 SCU за 50 aUEC.", 5, 20, 150),
            new(3, "Admin - Seraphim Station", "Stanton", "Продать Iodine: 5 SCU за 100 aUEC.", 0, 20, 250)
        ], 100, 20);
        var vm = CreateViewModel();
        vm.ActiveVoyagePlan = plan;

        vm.UpdateVoyageProgress(new DataCollectionSnapshot
        {
            Player = new PlayerState { CurrentLocation = Observed("Everus Harbor") }
        });
        Assert.Equal(1, vm.ActiveVoyageStopIndex);

        vm.UpdateVoyageProgress(new DataCollectionSnapshot
        {
            Player = new PlayerState { CurrentLocation = Observed("Everus Harbor") },
            Trades = [Trade("sale-1", "sale", "Laranite", "Everus Harbor")]
        });
        Assert.Equal(1, vm.ActiveVoyageStopIndex);
        Assert.Equal(2, vm.OverlayChecklist.Count);
        Assert.True(vm.OverlayChecklist[0].IsDone);
        Assert.False(vm.OverlayChecklist[1].IsDone);
        Assert.False(vm.CompleteChecklistStopCommand.CanExecute(null));
        var serialized = typeof(MainViewModel).GetMethod("SerializeActiveVoyage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(vm, null);
        var restored = CreateViewModel();
        typeof(MainViewModel).GetMethod("RestoreActiveVoyage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(restored, [serialized]);
        Assert.True(restored.OverlayChecklist[0].IsDone);
        restored.OverlayChecklist[1].IsDone = true;
        Assert.True(restored.CompleteChecklistStopCommand.CanExecute(null));
        restored.CompleteChecklistStopCommand.Execute(null);
        Assert.Equal(2, restored.ActiveVoyageStopIndex);

        vm.UpdateVoyageProgress(new DataCollectionSnapshot
        {
            Player = new PlayerState { CurrentLocation = Observed("Everus Harbor") },
            Trades =
            [
                Trade("sale-1", "sale", "Laranite", "Everus Harbor"),
                Trade("purchase-1", "purchase", "Iodine", "Admin - Everus Harbor")
            ]
        });
        Assert.Equal(2, vm.ActiveVoyageStopIndex);

        vm.UpdateVoyageProgress(new DataCollectionSnapshot
        {
            Player = new PlayerState { CurrentLocation = Observed("Seraphim Station") },
            Trades = [Trade("sale-2", "sale", "Iodine", "Admin - Seraphim Station")]
        });
        Assert.True(vm.ActiveVoyageCompleted);
    }

    [Theory]
    [InlineData("Everus Harbor", "Admin - Everus Harbor")]
    [InlineData("Seraphim Station", "Admin - Seraphim")]
    public void LocationMatchingAcceptsSharedMeaningfulToken(string detected, string terminal) =>
        Assert.True(MainViewModel.LocationMatches(detected, terminal));

    [Theory]
    [InlineData("ArcCorp Mining Area 045", "ArcCorp Mining Area 056")]
    [InlineData("Hickes Research Outpost", "Rayari Deltana Research Outpost")]
    public void LocationMatchingRejectsGenericSharedWords(string detected, string terminal) =>
        Assert.False(MainViewModel.LocationMatches(detected, terminal));

    private static ObservedValue<string> Observed(string value) =>
        new(value, DataSourceKind.GameLog, DateTimeOffset.UtcNow, .95);

    private static TradeEvent Trade(string id, string action, string commodity, string terminal) => new()
    {
        Id = id,
        Action = Observed(action),
        Commodity = Observed(commodity),
        Amount = new ObservedValue<decimal>(100, DataSourceKind.GameLog, DateTimeOffset.UtcNow, .95),
        Terminal = Observed(terminal)
    };

    private static MainViewModel CreateViewModel()
    {
        var root = Path.Combine(Path.GetTempPath(), "SCNexusTests", Guid.NewGuid().ToString("N"));
        var settings = new SettingsService(Path.Combine(root, "test.db"));
        var client = new HttpClient();
        var data = new GameDataService(client, Path.Combine(root, "cache"));
        return new MainViewModel(settings, new TradingService(data, new RouteService()),
            new FlightLogService(settings), data, new GameLogService(), new HaulingService(), new UpdateService());
    }
}
