using SCNexus.Models;

namespace SCNexus.Services;

public static class VehicleCatalog
{
    public static string InferRole(VehicleCatalogItem vehicle)
    {
        var name = vehicle.Name.ToUpperInvariant();
        if (name.StartsWith("A2 HERCULES") || name.Contains("GLADIATOR") ||
            name.Contains("ECLIPSE") || name.Contains("RETALIATOR BOMBER")) return "Бомбардировщик";
        if (name.Contains("APOLLO") || name.Contains("PISCES RESCUE") ||
            name.Contains("CUTLASS RED") || name.Contains("TERRAPIN MEDIC")) return "Медицинский";
        if (name.Contains("PROSPECTOR") || name.Contains("MOLE") || name.Contains("ARRASTRA") ||
            name.Contains("ORION") || name.Contains("GOLEM")) return "Добыча ресурсов";
        if (name.Contains("VULTURE") || name.Contains("RECLAIMER") || name.Contains("FORTUNE"))
            return "Утилизация";
        if (name.Contains("STARFARER")) return "Заправка";
        if (name.Contains("SRV") || name.Contains("VULCAN") || name.Contains("CRUCIBLE"))
            return "Поддержка и ремонт";
        if (name.Contains("HULL ") || name.StartsWith("C1 SPIRIT") || name.StartsWith("C2 HERCULES") ||
            name.Contains("CATERPILLAR") || name.Contains("RAFT") || name.Contains("IRONCLAD") ||
            name.Contains("RAILEN") || name.Contains("TAURUS") || name.Contains("FREELANCER MAX") ||
            name.Contains("STARLANCER MAX") || name.Contains("ZEUS MK II CL")) return "Грузоперевозки";
        if (name.StartsWith("M2 HERCULES") || name.Contains("VALKYRIE") || name.Contains("PROWLER") ||
            name.Contains("HOPLITE") || name.Contains("CUTLASS STEEL")) return "Военный транспорт";
        if (name.Contains("600I EXPLORER") || name.Contains("CARRACK") || name.Contains("AQUILA") ||
            name.Contains("ODYSSEY") || name.Contains("TERRAPIN") || name.Contains("ZEUS MK II ES") ||
            name.Contains("315P")) return "Исследование";
        if (name.Contains("890 JUMP") || name.Contains("PHOENIX") || name.Contains("TOURING") ||
            name.Contains("E1 SPIRIT") || name.Contains("STARLINER")) return "Пассажирский";
        if (name.Contains("M50") || name.StartsWith("RAZOR") ||
            name.Contains("350R") || name.Contains("MUSTANG GAMMA") || name.Contains("MUSTANG OMEGA"))
            return "Гоночный";
        if (name.Contains("MERCURY STAR RUNNER") || name.Contains("HERALD") ||
            name.Contains("RELIANT MAKO")) return "Передача данных";
        if (name.Contains("ARROW") || name.Contains("GLADIUS") || name.Contains("HORNET") ||
            name.Contains("SABRE") || name.Contains("SCORPIUS") || name.Contains("VANGUARD") ||
            name.Contains("ARES ") || name.Contains("F8C") || name.Contains("BUCCANEER") ||
            name.Contains("HURRICANE") || name.Contains("REDEEMER")) return "Боевое";
        return "Универсальный";
    }

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
