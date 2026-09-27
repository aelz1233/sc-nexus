using SCNexus.Models;

namespace SCNexus.Services;

public static class VehicleCatalog
{
    public static IReadOnlyList<VehicleCatalogItem> Search(IEnumerable<VehicleCatalogItem> vehicles,
        string query, string sortMode, int limit = 400)
    {
        var term = query.Trim();
        var matches = vehicles.Where(x => x.IsSpaceship == 1 &&
            (term.Length < 2 || x.Name.Contains(term, StringComparison.OrdinalIgnoreCase)));
        var sorted = sortMode == "По вместимости"
            ? matches.OrderByDescending(x => x.Scu).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            : matches.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase);
        return sorted.Take(limit).ToArray();
    }
}
