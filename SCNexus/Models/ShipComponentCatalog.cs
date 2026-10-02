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
}

public sealed record ShipComponentCatalog(
    string ShipName,
    string GameVersion,
    DateTimeOffset FetchedAt,
    IReadOnlyList<ShipComponentSlot> Slots,
    IReadOnlyList<ShipComponent> Components)
{
    public bool UsedOldCache { get; init; }
}
