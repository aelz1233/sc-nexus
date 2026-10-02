using SCNexus.Models;

namespace SCNexus.Services;

/// <summary>
/// Compares only measured, size-compatible components. The budget build uses a Pareto
/// frontier, so the highest-scoring affordable combination is chosen from loaded prices.
/// </summary>
public static class ShipBuildOptimizer
{
    private const double Epsilon = 0.000001;

    public static (ShipBuildResult Budget, ShipBuildResult Best) Build(
        ShipComponentCatalog catalog, decimal budgetAuec, ShipBuildProfile profile)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var slots = catalog.Slots.Where(x => x.MinSize > 0 && x.MaxSize >= x.MinSize).ToArray();
        var prepared = slots.Select(slot => Prepare(slot, catalog.Components, profile)).ToArray();
        var best = BuildUnrestricted(prepared);
        var budget = BuildBudget(prepared, Math.Max(0, budgetAuec));
        return (budget, best);
    }

    private static PreparedSlot Prepare(ShipComponentSlot slot, IReadOnlyList<ShipComponent> catalog,
        ShipBuildProfile profile)
    {
        var matching = catalog.Where(x => x.Type.Equals(slot.Type, StringComparison.OrdinalIgnoreCase)
                                          && x.Size >= slot.MinSize && x.Size <= slot.MaxSize
                                          && x.CompatibleSlotKeys.Contains(slot.Key))
            .GroupBy(x => x.Uuid, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();

        // A stock component can always be kept. The API may omit its detailed entry.
        if (!string.IsNullOrWhiteSpace(slot.InstalledUuid) &&
            matching.All(x => !x.Uuid.Equals(slot.InstalledUuid, StringComparison.OrdinalIgnoreCase)))
        {
            matching.Add(new ShipComponent(slot.InstalledUuid,
                string.IsNullOrWhiteSpace(slot.InstalledName) ? "Штатный компонент" : slot.InstalledName,
                slot.Type, slot.MaxSize, null, null, null, "", 0, 0, null));
        }

        var maxPrimary = matching.Count == 0 ? 0 : matching.Max(x => Math.Max(0, x.PrimaryMetric));
        var maxSecondary = matching.Count == 0 ? 0 : matching.Max(x => Math.Max(0, x.SecondaryMetric));
        var weight = CategoryWeight(slot.Type, profile);
        var candidates = matching.Select(component =>
        {
            var isInstalled = component.Uuid.Equals(slot.InstalledUuid, StringComparison.OrdinalIgnoreCase);
            var primary = maxPrimary > 0 ? Math.Max(0, component.PrimaryMetric) / maxPrimary : 0;
            var secondary = maxSecondary > 0 ? Math.Max(0, component.SecondaryMetric) / maxSecondary : 0;
            var measured = (maxPrimary > 0 ? 0.7 : 0) + (maxSecondary > 0 ? 0.3 : 0);
            var score = measured == 0 ? 0 : weight * (0.7 * primary + 0.3 * secondary) / measured;
            return new Candidate(new ShipBuildLine(slot, component, isInstalled,
                isInstalled ? 0 : component.PriceAuec), score);
        }).ToArray();
        return new PreparedSlot(slot, candidates);
    }

    private static ShipBuildResult BuildUnrestricted(IReadOnlyList<PreparedSlot> slots)
    {
        var lines = new List<ShipBuildLine>();
        double score = 0;
        foreach (var slot in slots)
        {
            var choice = slot.Candidates
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Line.IsInstalled ? 0 : 1)
                .ThenBy(x => x.Line.PurchasePrice ?? decimal.MaxValue)
                .FirstOrDefault();
            if (choice is null) continue;
            lines.Add(choice.Line);
            score += choice.Score;
        }
        return Result("Лучшее из доступных данных", slots, lines, score,
            "Максимум оценки для выбранного профиля среди подтверждённых совместимых деталей.");
    }

    private static ShipBuildResult BuildBudget(IReadOnlyList<PreparedSlot> slots, decimal budget)
    {
        var frontier = new List<BuildState> { new(0, 0, null) };
        var eligibleSlots = 0;
        foreach (var slot in slots)
        {
            var choices = AffordableCandidates(slot.Candidates);
            if (choices.Length == 0) continue;
            eligibleSlots++;
            var expanded = new List<BuildState>();
            foreach (var state in frontier)
            foreach (var candidate in choices)
            {
                var cost = state.Cost + (candidate.Line.IsInstalled ? 0 : candidate.Line.PurchasePrice!.Value);
                if (cost <= budget)
                    expanded.Add(new BuildState(cost, state.Score + candidate.Score,
                        new SelectionNode(state.Selection, candidate.Line)));
            }
            if (expanded.Count == 0) continue;
            expanded.Sort((a, b) =>
            {
                var cost = a.Cost.CompareTo(b.Cost);
                return cost == 0 ? b.Score.CompareTo(a.Score) : cost;
            });
            frontier = [];
            var bestScore = double.NegativeInfinity;
            foreach (var candidate in expanded)
            {
                if (candidate.Score <= bestScore + Epsilon) continue;
                frontier.Add(candidate);
                bestScore = candidate.Score;
            }
        }
        var winner = frontier.OrderByDescending(x => x.Score).ThenBy(x => x.Cost).First();
        var lines = new List<ShipBuildLine>();
        for (var node = winner.Selection; node is not null; node = node.Previous)
            lines.Add(node.Line);
        lines.Reverse();
        var status = eligibleSlots == 0 ? "Нет слотов с доступной ценой или штатной деталью."
            : "Максимальная оценка среди деталей с указанной ценой в пределах бюджета. Штатные детали можно оставить бесплатно.";
        return Result("Лучшее за бюджет", slots, lines, winner.Score, status);
    }

    private static Candidate[] AffordableCandidates(IEnumerable<Candidate> candidates)
    {
        var ordered = candidates.Where(x => x.Line.IsInstalled || x.Line.PurchasePrice is > 0)
            .OrderBy(x => x.Line.IsInstalled ? 0 : x.Line.PurchasePrice!.Value)
            .ThenByDescending(x => x.Score)
            .ThenBy(x => x.Line.IsInstalled ? 0 : 1);
        var result = new List<Candidate>();
        var bestScore = double.NegativeInfinity;
        foreach (var candidate in ordered)
        {
            if (candidate.Score <= bestScore + Epsilon) continue;
            result.Add(candidate);
            bestScore = candidate.Score;
        }
        return result.ToArray();
    }

    private static ShipBuildResult Result(string title, IReadOnlyList<PreparedSlot> slots,
        IReadOnlyList<ShipBuildLine> lines, double score, string status)
    {
        var unpriced = lines.Count(x => !x.IsInstalled && x.PurchasePrice is null);
        var knownCost = lines.Sum(x => x.IsInstalled ? 0 : x.PurchasePrice ?? 0);
        return new ShipBuildResult(title, lines, knownCost, score, unpriced,
            Math.Max(0, slots.Count - lines.Count), unpriced == 0 ? status :
                status + " Часть деталей не имеет подтверждённой цены; эту сборку нельзя считать гарантированно покупаемой.");
    }

    private static double CategoryWeight(string type, ShipBuildProfile profile) => (type, profile) switch
    {
        ("Shield", ShipBuildProfile.Combat) => 4,
        ("WeaponGun", ShipBuildProfile.Combat) => 5,
        ("QuantumDrive", ShipBuildProfile.Combat) => 1,
        ("PowerPlant", ShipBuildProfile.Combat) => 1.5,
        ("Cooler", ShipBuildProfile.Combat) => 1,
        ("Shield", ShipBuildProfile.Travel) => 2,
        ("WeaponGun", ShipBuildProfile.Travel) => 0.5,
        ("QuantumDrive", ShipBuildProfile.Travel) => 5,
        ("PowerPlant", ShipBuildProfile.Travel) => 1.5,
        ("Cooler", ShipBuildProfile.Travel) => 1.5,
        _ => 1
    };

    private sealed record PreparedSlot(ShipComponentSlot Slot, Candidate[] Candidates);
    private sealed record Candidate(ShipBuildLine Line, double Score);
    private sealed record SelectionNode(SelectionNode? Previous, ShipBuildLine Line);
    private sealed record BuildState(decimal Cost, double Score, SelectionNode? Selection);
}
