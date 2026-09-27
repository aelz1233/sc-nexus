using SCNexus.Models;

namespace SCNexus.Services;

public static class LocationCatalog
{
    public static IReadOnlyList<LocationOption> Build(IEnumerable<TradeTerminal> terminals) => terminals
        .Where(x => x.IsAvailableLive == 1 && !string.IsNullOrWhiteSpace(x.Location))
        .Select(x => new LocationOption(
            string.IsNullOrWhiteSpace(x.StarSystemName) ? "Неизвестная система" : x.StarSystemName.Trim(),
            x.Location.Trim()))
        .DistinctBy(x => (x.System.ToUpperInvariant(), x.Name.ToUpperInvariant()))
        .OrderBy(x => x.System, StringComparer.CurrentCultureIgnoreCase)
        .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
        .ToArray();

    public static IReadOnlyList<LocationOption> Search(IEnumerable<LocationOption> options,
        string system, string query, int limit = 12)
    {
        var value = query.Trim();
        if (value.Length < 2) return [];
        return options
            .Where(x => system == "Все системы" || x.System.Equals(system, StringComparison.OrdinalIgnoreCase))
            .Where(x => x.Name.Contains(value, StringComparison.OrdinalIgnoreCase) ||
                x.System.Contains(value, StringComparison.OrdinalIgnoreCase))
            .Take(limit).ToArray();
    }
}
