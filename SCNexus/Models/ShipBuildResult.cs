using SCNexus.Services;

namespace SCNexus.Models;

public enum ShipBuildProfile
{
    Combat,
    Travel
}

public sealed record ShipBuildLine(
    ShipComponentSlot Slot,
    ShipComponent Component,
    bool IsInstalled,
    decimal? PurchasePrice)
{
    public string SlotDisplay => Slot.Type switch
    {
        "Shield" => "Щит",
        "QuantumDrive" => "Квантовый привод",
        "PowerPlant" => "Генератор",
        "Cooler" => "Охлаждение",
        "WeaponGun" => "Орудие",
        _ => Slot.Type
    };

    public string NameDisplay => $"{Component.Name} · S{Component.Size}";
    public string PriceDisplay => IsInstalled ? "Уже установлен" : PurchasePrice is { } price
        ? $"{price:N0} aUEC" : "Цена неизвестна";
    public string ShopDisplay => IsInstalled ? "" : Component.Shop ?? "Магазин не указан";
    public string UpdatedDisplay => Component.PriceUpdated is { } date
        ? $"Цена от {date.ToLocalTime():dd.MM.yyyy}" : "";
    public string MetricDisplay => Component.PrimaryMetric <= 0 && Component.SecondaryMetric <= 0
        ? "Характеристики не загружены"
        : Slot.Type switch
    {
        "Shield" => $"Прочность {Component.PrimaryMetric:N0} HP · восстановление {Component.SecondaryMetric:N0}/с",
        "QuantumDrive" => Component.SecondaryMetric > 0
            ? $"Скорость {Component.PrimaryMetric / 1_000_000:N0} Мм/с · расход {1 / Component.SecondaryMetric:N3} SCU/Гм"
            : $"Скорость {Component.PrimaryMetric / 1_000_000:N0} Мм/с · расход неизвестен",
        "PowerPlant" => $"Энергия {Component.PrimaryMetric:N0} сегм. · прочность {Component.SecondaryMetric:N0}",
        "Cooler" => $"Охлаждение {Component.PrimaryMetric:N0} сегм. · прочность {Component.SecondaryMetric:N0}",
        "WeaponGun" => $"Устойчивый DPS (60 с): {Component.PrimaryMetric:N0} · пиковый DPS: {Component.SecondaryMetric:N0}",
        _ => Component.MetricDescription ?? "Характеристики не указаны"
    };
}

public sealed record ComponentShoppingItem(string Name, int Quantity, decimal UnitPrice)
{
    public string Display => LocalizationService.T(Quantity > 1
        ? $"{Name} ×{Quantity} · {UnitPrice:N0} aUEC за шт."
        : $"{Name} · {UnitPrice:N0} aUEC");
}

public sealed record ComponentShoppingStop(
    int Number,
    string System,
    string Location,
    string Shop,
    IReadOnlyList<ComponentShoppingItem> Items,
    decimal Cost)
{
    public bool IsPyro => System.Equals("Pyro", StringComparison.OrdinalIgnoreCase);
    public string Heading => LocalizationService.T($"{Number}. {string.Join(" · ", new[] { System, Location, Shop }
        .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))}");
    public string CostDisplay => LocalizationService.T($"На этой остановке: {Cost:N0} aUEC");
}

public sealed record ShipBuildResult(
    string Title,
    IReadOnlyList<ShipBuildLine> Lines,
    decimal KnownCost,
    double Score,
    int UnpricedCount,
    int UnsupportedSlots,
    string Status)
{
    public IReadOnlyList<ComponentShoppingStop> ShoppingStops { get; init; } = [];
    public double PowerSupply { get; init; }
    public double PowerDemand { get; init; }
    public double CoolantSupply { get; init; }
    public double CoolantDemand { get; init; }
    public double QuantumSpeed { get; init; }
    public double QuantumRangeGm { get; init; }
    public double QuantumFuelConsumption { get; init; }
    public decimal ShoppingMinimumCost { get; init; }
    public decimal ShoppingRouteCost { get; init; }
    public string CostDisplay => UnpricedCount == 0
        ? $"Стоимость замены: {KnownCost:N0} aUEC"
        : $"Известная стоимость: {KnownCost:N0} aUEC · без цены: {UnpricedCount}";
    public string CoverageDisplay => UnsupportedSlots == 0
        ? "Подтверждённые слоты компонентов рассчитаны"
        : $"Не удалось подобрать для {UnsupportedSlots} слотов";
    public string ChangesDisplay => $"Заменить компонентов: {Lines.Count(x => !x.IsInstalled)} · оставить штатными: {Lines.Count(x => x.IsInstalled)}";
    public string EngineeringDisplay
    {
        get
        {
            var parts = new List<string>();
            if (PowerSupply > 0 || PowerDemand > 0)
                parts.Add($"Энергия: {PowerDemand:N1} из {PowerSupply:N1} сегм. · резерв {PowerSupply - PowerDemand:+0.0;-0.0;0.0}");
            if (CoolantSupply > 0 || CoolantDemand > 0)
                parts.Add($"Охлаждение: {CoolantDemand:N1} из {CoolantSupply:N1} сегм. · резерв {CoolantSupply - CoolantDemand:+0.0;-0.0;0.0}");
            return parts.Count == 0 ? "Энергия и охлаждение: данных недостаточно" : string.Join(Environment.NewLine, parts);
        }
    }

    public string EngineeringStatus => PowerSupply > 0 && PowerDemand > PowerSupply
        ? "⚠ Расчётная нагрузка превышает выработку энергии."
        : CoolantSupply > 0 && CoolantDemand > CoolantSupply
            ? "⚠ Расчётная нагрузка превышает возможности охлаждения."
            : "Нагрузка рассчитана для сравниваемых сменных слотов; неподдерживаемые системы корабля не включены.";

    public string QuantumDisplay => QuantumSpeed <= 0 && QuantumRangeGm <= 0
        ? "Квантовый привод: данных недостаточно"
        : $"Квант: {(QuantumSpeed > 0 ? $"{QuantumSpeed / 1_000_000:N0} Мм/с" : "скорость неизвестна")}" +
          (QuantumRangeGm > 0 ? $" · расчётная дальность {QuantumRangeGm:N0} Гм" : "") +
          (QuantumFuelConsumption > 0 ? $" · расход {QuantumFuelConsumption:N3} SCU/Гм" : "");

    public decimal ShoppingPremiumPercent => ShoppingMinimumCost <= 0 ? 0
        : 100 * (ShoppingRouteCost - ShoppingMinimumCost) / ShoppingMinimumCost;
    public string ShoppingRouteDisplay => LocalizationService.T(ShoppingStops.Count == 0
        ? "Маршрут покупок не нужен или магазины не указаны."
        : $"Рекомендуемый маршрут: {ShoppingStops.Count} {StopsWord(ShoppingStops.Count)} · покупки {ShoppingRouteCost:N0} aUEC" +
          (ShoppingPremiumPercent > 0.05m ? $" · +{ShoppingPremiumPercent:N1}% к минимальной сумме" : " · без переплаты"));
    public string ShoppingRouteLogic => LocalizationService.T("Сначала сокращаем число остановок. Учитываются магазины, где каждая деталь дороже своей минимальной цены не более чем на 5%. Безопасные точки, текущие локация и система в приоритете, Pyro — в конце.");

    private static string StopsWord(int count) => count % 10 == 1 && count % 100 != 11 ? "остановка"
        : count % 10 is >= 2 and <= 4 && count % 100 is not (>= 12 and <= 14) ? "остановки"
        : "остановок";
}
