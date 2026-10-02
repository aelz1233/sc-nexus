using SCNexus.Models;

namespace SCNexus.Services;

public interface IDataProvider
{
    string Name { get; }
    DataSourceKind Source { get; }
    int Priority { get; }
    TimeSpan RefreshInterval { get; }
    Task<DataProviderResult> CollectAsync(DataProviderContext context, CancellationToken token);
}

public sealed record DataProviderContext(string? GameDirectory, PlayerState CurrentState,
    DateTimeOffset Now, bool OcrEnabled, bool ForceRefresh = false);

public sealed class DataProviderResult
{
    public IReadOnlyList<ValueObservation> Values { get; init; } = [];
    public IReadOnlyList<TypedObservation> Records { get; init; } = [];
    public string Status { get; init; } = "OK";
}

public sealed record ValueObservation(string Key, string Value, DataSourceKind Source,
    DateTimeOffset Timestamp, double Confidence, string? Unit = null, string? DataVersion = null);

public sealed record TypedObservation(string Kind, string Key, object Value, DataSourceKind Source,
    DateTimeOffset Timestamp, double Confidence);
