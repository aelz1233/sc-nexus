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
    public void PurchasePlanGroupsRepeatedComponentsByShop()
    {
        var slotOne = new ShipComponentSlot("/shield#1", "Shield", 2, 2, "stock-1", "Stock 1");
        var slotTwo = new ShipComponentSlot("/shield#2", "Shield", 2, 2, "stock-2", "Stock 2");
        var component = Part("upgrade", "FR-76", 500, 20_000, slotOne.Key) with
        {
            Shop = "Omega Pro", CompatibleSlotKeys = [slotOne.Key, slotTwo.Key]
        };
        var build = new ShipBuildResult("Test",
        [
            new ShipBuildLine(slotOne, component, false, component.PriceAuec),
            new ShipBuildLine(slotTwo, component, false, component.PriceAuec)
        ], 40_000, 1, 0, 0, "OK");

        Assert.Contains("Omega Pro: FR-76 ×2", build.PurchasePlanDisplay);
    }

    private static ShipComponent Part(string uuid, string name, double health, decimal? price, string slotKey) =>
        new(uuid, name, "Shield", 2, price, "Магазин", DateTimeOffset.UtcNow, "4.10", health, 0,
            "Прочность щита / восстановление")
        { CompatibleSlotKeys = [slotKey] };
}
