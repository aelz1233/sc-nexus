namespace SCNexus.Models;

public sealed record ShipComponentSlot(
    string Key,
    string Type,
    int MinSize,
    int MaxSize,
    string InstalledUuid,
    string InstalledName)
{
    public IReadOnlyList<string> RequiredTags { get; init; } = [];
    public IReadOnlyList<string> PortTags { get; init; } = [];
}

public sealed record ShipComponent(
    string Uuid,
    string Name,
    string Type,
    int Size,
    decimal? PriceAuec,
    string? Shop,
    DateTimeOffset? PriceUpdated,
    string GameVersion,
    double PrimaryMetric,
    double SecondaryMetric,
    string? MetricDescription)
{
    public IReadOnlyList<string> CompatibleSlotKeys { get; init; } = [];
    public IReadOnlyList<ComponentShopOffer> Offers { get; init; } = [];
    public double PowerDraw { get; init; }
    public double CoolantDraw { get; init; }
    public double PowerGeneration { get; init; }
    public double CoolantGeneration { get; init; }
    public double QuantumFuelConsumptionScuPerGm { get; init; }
}

public sealed record ComponentShopOffer(
    decimal PriceAuec,
    string Shop,
    string Location,
    string ParentLocation,
    string System,
    DateTimeOffset UpdatedAt)
{
    public bool IsPyro => System.Equals("Pyro", StringComparison.OrdinalIgnoreCase);
    public string PlaceDisplay => string.Join(" · ", new[] { System, Location, Shop }
        .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase));
}

public sealed record ShipComponentCatalog(
    string ShipName,
    string GameVersion,
    DateTimeOffset FetchedAt,
    IReadOnlyList<ShipComponentSlot> Slots,
    IReadOnlyList<ShipComponent> Components)
{
    public int SchemaVersion { get; init; }
    public double QuantumFuelCapacityScu { get; init; }
    public bool UsedOldCache { get; init; }
}
