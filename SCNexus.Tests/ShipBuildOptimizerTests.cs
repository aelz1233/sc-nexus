using SCNexus.Models;
using SCNexus.Services;

namespace SCNexus.Tests;

public class ShipBuildOptimizerTests
{
    [Fact]
    public void BudgetUsesPricedCompatiblePartsAndCanKeepInstalledPart()
    {
        var slot = new ShipComponentSlot("/shield#0", "Shield", 2, 2, "stock", "Штатный щит");
        var parts = new[]
        {
            Part("stock", "Штатный щит", 100, null, slot.Key),
            Part("affordable", "Щит за 40", 160, 40, slot.Key),
            Part("premium", "Щит за 100", 250, 100, slot.Key),
            Part("unpriced", "Трофейный щит", 400, null, slot.Key),
            Part("wrong-port", "Несовместимый щит", 500, 1, "/another-port")
        };
        var catalog = new ShipComponentCatalog("C2", "4.10", DateTimeOffset.UtcNow, [slot], parts);

        var (budget, best) = ShipBuildOptimizer.Build(catalog, 40, ShipBuildProfile.Combat);

        Assert.Equal("Щит за 40", Assert.Single(budget.Lines).Component.Name);
        Assert.Equal(40, budget.KnownCost);
        Assert.Equal("Трофейный щит", Assert.Single(best.Lines).Component.Name);
        Assert.Equal(1, best.UnpricedCount);

        var (zeroBudget, _) = ShipBuildOptimizer.Build(catalog, 0, ShipBuildProfile.Combat);
        Assert.Equal("Штатный щит", Assert.Single(zeroBudget.Lines).Component.Name);
        Assert.Equal(0, zeroBudget.KnownCost);
    }

    [Fact]
    public void BudgetOptimizesTheWholeBuildInsteadOfOneSlotAtATime()
    {
        var first = new ShipComponentSlot("/shield#1", "Shield", 2, 2, "stock-1", "Штатный");
        var second = new ShipComponentSlot("/shield#2", "Shield", 2, 2, "stock-2", "Штатный");
        var parts = new[]
        {
            Part("stock-1", "Штатный 1", 100, null, first.Key),
            Part("stock-2", "Штатный 2", 100, null, second.Key),
            Part("cheap", "Хороший", 140, 5, first.Key) with
                { CompatibleSlotKeys = [first.Key, second.Key] },
            Part("costly", "Лучший", 150, 6, first.Key) with
                { CompatibleSlotKeys = [first.Key, second.Key] }
        };
        var catalog = new ShipComponentCatalog("C2", "4.10", DateTimeOffset.UtcNow,
            [first, second], parts);

        var (budget, _) = ShipBuildOptimizer.Build(catalog, 10, ShipBuildProfile.Combat);

        Assert.Equal(2, budget.Lines.Count);
        Assert.All(budget.Lines, x => Assert.Equal("Хороший", x.Component.Name));
        Assert.Equal(10, budget.KnownCost);
    }

    [Fact]
    public void BuildCalculatesEngineeringQuantumRangeAndOrderedShoppingStops()
    {
        var plantSlot = new ShipComponentSlot("/plant", "PowerPlant", 2, 2, "old-plant", "Old plant");
        var driveSlot = new ShipComponentSlot("/drive", "QuantumDrive", 2, 2, "old-drive", "Old drive");
        var plant = new ShipComponent("plant", "Atlas Power", "PowerPlant", 2, 20_000, "Pyro Shop",
            DateTimeOffset.UtcNow, "4.10", 30, 100, "Power")
        {
            CompatibleSlotKeys = [plantSlot.Key], PowerGeneration = 30,
            Offers = [new(20_000, "Pyro Shop", "Ruin Station", "", "Pyro", DateTimeOffset.UtcNow)]
        };
        var drive = new ShipComponent("drive", "Voyager", "QuantumDrive", 2, 10_000, "Stanton Shop",
            DateTimeOffset.UtcNow, "4.10", 500_000_000, 20, "Quantum")
        {
            CompatibleSlotKeys = [driveSlot.Key], PowerDraw = 5, CoolantDraw = 4,
            QuantumFuelConsumptionScuPerGm = .05,
            Offers = [new(10_000, "Stanton Shop", "Area18", "ArcCorp", "Stanton", DateTimeOffset.UtcNow)]
        };
        var catalog = new ShipComponentCatalog("Test", "4.10", DateTimeOffset.UtcNow,
            [plantSlot, driveSlot], [plant, drive]) { QuantumFuelCapacityScu = 10 };

        var (_, best) = ShipBuildOptimizer.Build(catalog, 100_000, ShipBuildProfile.Travel, "Stanton");

        Assert.Equal(30, best.PowerSupply);
        Assert.Equal(5, best.PowerDemand);
        Assert.Equal(200, best.QuantumRangeGm);
        Assert.Equal("Stanton", best.ShoppingStops[0].System);
        Assert.Equal("Pyro", best.ShoppingStops[1].System);
        Assert.Contains("Area18", best.ShoppingStops[0].Heading);

        var (_, startingInPyro) = ShipBuildOptimizer.Build(catalog, 100_000, ShipBuildProfile.Travel, "Pyro");
        Assert.Equal("Stanton", startingInPyro.ShoppingStops[0].System);
        Assert.Equal("Pyro", startingInPyro.ShoppingStops[^1].System);
    }

    [Fact]
    public void ShoppingRouteCombinesStopsWhenSharedShopCostsAtMostFivePercentMore()
    {
        var first = new ShipComponentSlot("/shield#1", "Shield", 2, 2, "old-1", "Old 1");
        var second = new ShipComponentSlot("/shield#2", "Shield", 2, 2, "old-2", "Old 2");
        var now = DateTimeOffset.UtcNow;
        var a = Part("a", "Shield A", 200, 100, first.Key) with
        {
            Offers =
            [
                new(100, "Shop A", "Area18", "", "Stanton", now),
                new(104, "Central", "Orison", "", "Stanton", now)
            ]
        };
        var b = Part("b", "Shield B", 200, 100, second.Key) with
        {
            Offers =
            [
                new(100, "Shop B", "Lorville", "", "Stanton", now),
                new(104, "Central", "Orison", "", "Stanton", now)
            ]
        };
        var catalog = new ShipComponentCatalog("Test", "4.10", now, [first, second], [a, b]);

        var (_, best) = ShipBuildOptimizer.Build(catalog, 1_000, ShipBuildProfile.Combat, "Stanton");

        var stop = Assert.Single(best.ShoppingStops);
        Assert.Equal("Central", stop.Shop);
        Assert.Equal(200, best.ShoppingMinimumCost);
        Assert.Equal(208, best.ShoppingRouteCost);
        Assert.Equal(4, best.ShoppingPremiumPercent);
    }

    [Fact]
    public void ShoppingRoutePrefersCurrentLocationWhenPriceAndStopCountMatch()
    {
        var slot = new ShipComponentSlot("/shield", "Shield", 2, 2, "old", "Old");
        var now = DateTimeOffset.UtcNow;
        var part = Part("upgrade", "Shield", 200, 100, slot.Key) with
        {
            Offers =
            [
                new(100, "CenterMass", "Area18", "", "Stanton", now),
                new(100, "Cousin Crow's", "Orison", "", "Stanton", now)
            ]
        };
        var catalog = new ShipComponentCatalog("Test", "4.10", now, [slot], [part]);

        var (_, best) = ShipBuildOptimizer.Build(catalog, 1_000, ShipBuildProfile.Combat,
            "Stanton", "Orison");

        Assert.Equal("Orison", Assert.Single(best.ShoppingStops).Location);
    }

    [Fact]
    public void ShoppingPlansSeparateFastestBalancedAndCheapestRoutes()
    {
        var first = new ShipComponentSlot("/shield#1", "Shield", 2, 2, "old-1", "Old 1");
        var second = new ShipComponentSlot("/shield#2", "Shield", 2, 2, "old-2", "Old 2");
        var now = DateTimeOffset.UtcNow;
        var a = Part("a", "Shield A", 200, 100, first.Key) with
        {
            Offers =
            [
                new(100, "Shop A", "Area18", "", "Stanton", now),
                new(112, "Central", "Orison", "", "Stanton", now)
            ]
        };
        var b = Part("b", "Shield B", 200, 100, second.Key) with
        {
            Offers =
            [
                new(100, "Shop B", "Lorville", "", "Stanton", now),
                new(112, "Central", "Orison", "", "Stanton", now)
            ]
        };
        var catalog = new ShipComponentCatalog("Test", "4.10", now, [first, second], [a, b]);

        var (_, best) = ShipBuildOptimizer.Build(catalog, 1_000, ShipBuildProfile.Combat, "Stanton");

        Assert.Equal(3, best.ShoppingPlans.Count);
        Assert.Equal(2, best.ShoppingPlans.Single(x => x.Kind == "Balanced").Stops.Count);
        Assert.Single(best.ShoppingPlans.Single(x => x.Kind == "Fastest").Stops);
        Assert.Equal(2, best.ShoppingPlans.Single(x => x.Kind == "Cheapest").Stops.Count);
    }

    [Fact]
    public void ShoppingPlansRespectPyroAndRiskFiltersAndCompareWithInstalledBuild()
    {
        var slot = new ShipComponentSlot("/shield", "Shield", 2, 2, "stock", "Stock");
        var now = DateTimeOffset.UtcNow;
        var stock = Part("stock", "Stock", 100, null, slot.Key) with
        {
            PowerDraw = 5
        };
        var upgrade = Part("upgrade", "Upgrade", 200, 100, slot.Key) with
        {
            PowerDraw = 8,
            Offers =
            [
                new(80, "Pyro Store", "Ruin Station", "", "Pyro", now),
                new(90, "NQA Terminal", "Brio's Breaker Yard", "", "Stanton", now),
                new(100, "Safe Store", "Area18", "", "Stanton", now)
            ]
        };
        var catalog = new ShipComponentCatalog("Test", "4.10", now, [slot], [stock, upgrade]);

        var (_, best) = ShipBuildOptimizer.Build(catalog, 1_000, ShipBuildProfile.Combat,
            "Stanton", null, avoidPyro: true, allowRisky: false);

        Assert.All(best.ShoppingPlans.SelectMany(x => x.Stops), stop =>
        {
            Assert.NotEqual("Pyro", stop.System);
            Assert.DoesNotContain("NQA", stop.Shop, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Equal("Safe Store", Assert.Single(best.ShoppingStops).Shop);
        Assert.True(best.Score > best.CurrentScore);
        Assert.Contains("Stock", Assert.Single(best.Lines).ChangeDisplay);
    }

    private static ShipComponent Part(string uuid, string name, double health, decimal? price, string slotKey) =>
        new(uuid, name, "Shield", 2, price, "Магазин", DateTimeOffset.UtcNow, "4.10", health, 0,
            "Прочность щита / восстановление")
        { CompatibleSlotKeys = [slotKey] };
}
