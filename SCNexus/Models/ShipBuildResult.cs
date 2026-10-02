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

public sealed record ShipBuildResult(
    string Title,
    IReadOnlyList<ShipBuildLine> Lines,
    decimal KnownCost,
    double Score,
    int UnpricedCount,
    int UnsupportedSlots,
    string Status)
{
    public string CostDisplay => UnpricedCount == 0
        ? $"Стоимость замены: {KnownCost:N0} aUEC"
        : $"Известная стоимость: {KnownCost:N0} aUEC · без цены: {UnpricedCount}";
    public string CoverageDisplay => UnsupportedSlots == 0
        ? "Подтверждённые слоты компонентов рассчитаны"
        : $"Не удалось подобрать для {UnsupportedSlots} слотов";
}
