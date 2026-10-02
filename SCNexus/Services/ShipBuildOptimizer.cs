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
        ShipComponentCatalog catalog, decimal budgetAuec, ShipBuildProfile profile,
        string? startSystem = null, string? startLocation = null,
        bool avoidPyro = false, bool allowRisky = true)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var slots = catalog.Slots.Where(x => x.MinSize > 0 && x.MaxSize >= x.MinSize).ToArray();
        var prepared = slots.Select(slot => Prepare(slot, catalog.Components, profile)).ToArray();
        var best = BuildUnrestricted(prepared, catalog, startSystem, startLocation, avoidPyro, allowRisky);
        var budget = BuildBudget(prepared, Math.Max(0, budgetAuec), catalog, startSystem, startLocation,
            avoidPyro, allowRisky);
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

    private static ShipBuildResult BuildUnrestricted(IReadOnlyList<PreparedSlot> slots,
        ShipComponentCatalog catalog, string? startSystem, string? startLocation,
        bool avoidPyro, bool allowRisky)
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
            "Максимум оценки для выбранного профиля среди подтверждённых совместимых деталей.",
            catalog, startSystem, startLocation, avoidPyro, allowRisky);
    }

    private static ShipBuildResult BuildBudget(IReadOnlyList<PreparedSlot> slots, decimal budget,
        ShipComponentCatalog catalog, string? startSystem, string? startLocation,
        bool avoidPyro, bool allowRisky)
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
        return Result("Лучшее за бюджет", slots, lines, winner.Score, status, catalog, startSystem,
            startLocation, avoidPyro, allowRisky);
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
        IReadOnlyList<ShipBuildLine> lines, double score, string status,
        ShipComponentCatalog catalog, string? startSystem, string? startLocation,
        bool avoidPyro, bool allowRisky)
    {
        var unpriced = lines.Count(x => !x.IsInstalled && x.PurchasePrice is null);
        var knownCost = lines.Sum(x => x.IsInstalled ? 0 : x.PurchasePrice ?? 0);
        var engineering = CalculateEngineering(lines, catalog);
        var currentSelections = slots.Select(x => x.Candidates.FirstOrDefault(y => y.Line.IsInstalled))
            .Where(x => x is not null).Select(x => x!).ToArray();
        var currentLines = currentSelections.Select(x => x.Line).ToArray();
        var currentEngineering = CalculateEngineering(currentLines, catalog);
        var shoppingPlans = BuildShoppingPlans(lines, startSystem, startLocation, avoidPyro, allowRisky);
        var shopping = shoppingPlans.FirstOrDefault(x => x.Kind == "Balanced") ?? shoppingPlans.FirstOrDefault();
        var result = new ShipBuildResult(title, lines, knownCost, score, unpriced,
            Math.Max(0, slots.Count - lines.Count), unpriced == 0 ? status :
                status + " Часть деталей не имеет подтверждённой цены; эту сборку нельзя считать гарантированно покупаемой.")
        {
            CurrentScore = currentSelections.Sum(x => x.Score),
            CurrentPowerSupply = currentEngineering.PowerSupply,
            CurrentPowerDemand = currentEngineering.PowerDemand,
            CurrentCoolantSupply = currentEngineering.CoolantSupply,
            CurrentCoolantDemand = currentEngineering.CoolantDemand,
            CurrentQuantumSpeed = currentEngineering.QuantumSpeed,
            PowerSupply = engineering.PowerSupply,
            PowerDemand = engineering.PowerDemand,
            CoolantSupply = engineering.CoolantSupply,
            CoolantDemand = engineering.CoolantDemand,
            QuantumSpeed = engineering.QuantumSpeed,
            QuantumFuelConsumption = engineering.FuelConsumption,
            QuantumRangeGm = engineering.QuantumRange,
            ShoppingPlans = shoppingPlans,
            ShoppingStops = shopping?.Stops ?? [],
            ShoppingMinimumCost = shopping?.MinimumCost ?? 0,
            ShoppingRouteCost = shopping?.Cost ?? 0
        };
        return result;
    }

    private static EngineeringSnapshot CalculateEngineering(IReadOnlyList<ShipBuildLine> lines,
        ShipComponentCatalog catalog)
    {
        var powerSupply = lines.Sum(x => x.Component.PowerGeneration > 0
            ? x.Component.PowerGeneration
            : x.Slot.Type == "PowerPlant" ? x.Component.PrimaryMetric : 0);
        var coolantSupply = lines.Sum(x => x.Component.CoolantGeneration > 0
            ? x.Component.CoolantGeneration
            : x.Slot.Type == "Cooler" ? x.Component.PrimaryMetric : 0);
        var powerDemand = lines.Where(x => x.Slot.Type != "PowerPlant").Sum(x => x.Component.PowerDraw);
        var coolantDemand = lines.Where(x => x.Slot.Type != "Cooler").Sum(x => x.Component.CoolantDraw);
        var quantum = lines.FirstOrDefault(x => x.Slot.Type == "QuantumDrive")?.Component;
        var fuelConsumption = quantum?.QuantumFuelConsumptionScuPerGm > 0
            ? quantum.QuantumFuelConsumptionScuPerGm
            : quantum?.SecondaryMetric > 0 ? 1 / quantum.SecondaryMetric : 0;
        return new EngineeringSnapshot(powerSupply, powerDemand, coolantSupply, coolantDemand,
            quantum?.PrimaryMetric ?? 0, fuelConsumption,
            fuelConsumption > 0 ? catalog.QuantumFuelCapacityScu / fuelConsumption : 0);
    }

    private static IReadOnlyList<ComponentShoppingPlan> BuildShoppingPlans(
        IReadOnlyList<ShipBuildLine> lines, string? startSystem, string? startLocation,
        bool avoidPyro, bool allowRisky)
    {
        var balanced = BuildShoppingRoute(lines, startSystem, startLocation, 1.05m, avoidPyro, allowRisky);
        var fastest = BuildShoppingRoute(lines, startSystem, startLocation, 1.15m, avoidPyro, allowRisky);
        var cheapest = BuildCheapestRoute(lines, startSystem, startLocation, avoidPyro, allowRisky);
        if (balanced.Stops.Count == 0 && fastest.Stops.Count == 0 && cheapest.Stops.Count == 0) return [];
        return
        [
            new ComponentShoppingPlan("Balanced", balanced.Stops, balanced.RouteCost,
                balanced.MinimumCost, balanced.TravelScore),
            new ComponentShoppingPlan("Fastest", fastest.Stops, fastest.RouteCost,
                fastest.MinimumCost, fastest.TravelScore),
            new ComponentShoppingPlan("Cheapest", cheapest.Stops, cheapest.RouteCost,
                cheapest.MinimumCost, cheapest.TravelScore)
        ];
    }

    private static ShoppingRoute BuildShoppingRoute(
        IReadOnlyList<ShipBuildLine> lines, string? startSystem, string? startLocation,
        decimal maximumPriceMultiplier, bool avoidPyro, bool allowRisky)
    {
        var demands = lines.Where(x => !x.IsInstalled && x.PurchasePrice is > 0)
            .GroupBy(x => x.Component.Uuid, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ComponentDemand(group.Key, group.First().Component, group.Count()))
            .ToArray();
        if (demands.Length == 0) return new([], 0, 0, 0);

        var minimumCost = demands.Sum(demand =>
            (demand.Component.Offers.Count > 0 ? demand.Component.Offers.Min(x => x.PriceAuec)
                : demand.Component.PriceAuec ?? 0) * demand.Quantity);
        var candidates = new Dictionary<(string System, string Location, string Shop),
            Dictionary<string, ComponentShopOffer>>(StringTupleComparer.Instance);
        for (var index = 0; index < demands.Length; index++)
        {
            var demand = demands[index];
            var offers = demand.Component.Offers.Count > 0 ? demand.Component.Offers :
                demand.Component.PriceAuec is { } price
                    ? [new ComponentShopOffer(price, demand.Component.Shop ?? "Магазин не указан", "", "",
                        "Неизвестная система", demand.Component.PriceUpdated ?? DateTimeOffset.UtcNow)]
                    : [];
            offers = offers.Where(x => OfferAllowed(x, avoidPyro, allowRisky)).ToArray();
            if (offers.Count == 0) continue;
            var minimum = offers.Min(x => x.PriceAuec);
            foreach (var offer in offers.Where(x => x.PriceAuec <= minimum * maximumPriceMultiplier))
            {
                var key = (string.IsNullOrWhiteSpace(offer.System) ? "Неизвестная система" : offer.System,
                    string.IsNullOrWhiteSpace(offer.Location) ? offer.ParentLocation : offer.Location,
                    string.IsNullOrWhiteSpace(offer.Shop) ? "Магазин не указан" : offer.Shop);
                if (!candidates.TryGetValue(key, out var byComponent)) candidates[key] = byComponent =
                    new Dictionary<string, ComponentShopOffer>(StringComparer.OrdinalIgnoreCase);
                if (!byComponent.TryGetValue(demand.Uuid, out var current) || offer.PriceAuec < current.PriceAuec)
                    byComponent[demand.Uuid] = offer;
            }
        }

        var shops = candidates.Select(pair => new ShopCandidate(pair.Key, pair.Value,
            Coverage(pair.Value.Keys, demands))).Where(x => x.Offers.Count > 0).ToArray();
        if (shops.Length == 0) return new([], minimumCost, 0, 0);
        var selected = demands.Length <= 20 ? SelectStops(shops, demands, startSystem, startLocation) :
            SelectStopsGreedy(shops, demands, startSystem, startLocation);
        if (selected.Length == 0) return new([], minimumCost, 0, 0);

        var assigned = selected.Select(index => new List<(ComponentDemand Demand, ComponentShopOffer Offer)>()).ToArray();
        foreach (var demand in demands)
        {
            var choice = selected.Select((shopIndex, routeIndex) => new
            {
                RouteIndex = routeIndex,
                Offer = shops[shopIndex].Offers.GetValueOrDefault(demand.Uuid)
            }).Where(x => x.Offer is not null).OrderBy(x => x.Offer!.PriceAuec).FirstOrDefault();
            if (choice?.Offer is not null) assigned[choice.RouteIndex].Add((demand, choice.Offer));
        }

        var groups = selected.Select((shopIndex, routeIndex) =>
            new RouteGroup(shops[shopIndex], assigned[routeIndex])).Where(x => x.Assigned.Count > 0).ToArray();
        groups = OrderGroupsByTravel(groups, startSystem, startLocation);
        var stops = groups.Select((x, index) => new ComponentShoppingStop(index + 1, x.Shop.Key.System,
            x.Shop.Key.Location, x.Shop.Key.Shop,
            x.Assigned.Select(item => new ComponentShoppingItem(item.Demand.Component.Name,
                item.Demand.Quantity, item.Offer.PriceAuec)).ToArray(),
            x.Assigned.Sum(item => item.Offer.PriceAuec * item.Demand.Quantity))).ToArray();
        return new(stops, minimumCost, stops.Sum(x => x.Cost),
            RouteTravelScore(stops, startSystem, startLocation));
    }

    private static ShoppingRoute BuildCheapestRoute(IReadOnlyList<ShipBuildLine> lines,
        string? startSystem, string? startLocation, bool avoidPyro, bool allowRisky)
    {
        var demands = lines.Where(x => !x.IsInstalled && x.PurchasePrice is > 0)
            .GroupBy(x => x.Component.Uuid, StringComparer.OrdinalIgnoreCase)
            .Select(x => new ComponentDemand(x.Key, x.First().Component, x.Count())).ToArray();
        if (demands.Length == 0) return new([], 0, 0, 0);
        var selected = new List<(ComponentDemand Demand, ComponentShopOffer Offer)>();
        foreach (var demand in demands)
        {
            var offers = demand.Component.Offers.Where(x => OfferAllowed(x, avoidPyro, allowRisky)).ToArray();
            if (offers.Length == 0 && demand.Component.PriceAuec is { } fallback)
                offers = [new ComponentShopOffer(fallback, demand.Component.Shop ?? "Магазин не указан", "", "",
                    "Неизвестная система", demand.Component.PriceUpdated ?? DateTimeOffset.UtcNow)];
            var offer = offers.OrderBy(x => x.PriceAuec)
                .ThenBy(x => TravelLegScore(startSystem, startLocation, x.System, x.Location, x.Shop)).FirstOrDefault();
            if (offer is null) return new([], demands.Sum(x => (x.Component.PriceAuec ?? 0) * x.Quantity), 0, 0);
            selected.Add((demand, offer));
        }
        var grouped = selected.GroupBy(x => (x.Offer.System, x.Offer.Location, x.Offer.Shop), StringTupleComparer.Instance)
            .Select(x => new CheapestGroup(x.Key, x.ToArray())).ToList();
        var ordered = new List<CheapestGroup>();
        var currentSystem = startSystem;
        var currentLocation = startLocation;
        while (grouped.Count > 0)
        {
            var next = grouped.OrderBy(x => TravelLegScore(currentSystem, currentLocation,
                    x.Key.System, x.Key.Location, x.Key.Shop)).ThenBy(x => x.Key.System).ThenBy(x => x.Key.Location).First();
            ordered.Add(next);
            grouped.Remove(next);
            currentSystem = next.Key.System;
            currentLocation = next.Key.Location;
        }
        var stops = ordered.Select((x, index) => new ComponentShoppingStop(index + 1,
            x.Key.System, x.Key.Location, x.Key.Shop,
            x.Items.Select(item => new ComponentShoppingItem(item.Demand.Component.Name,
                item.Demand.Quantity, item.Offer.PriceAuec)).ToArray(),
            x.Items.Sum(item => item.Offer.PriceAuec * item.Demand.Quantity))).ToArray();
        var cost = stops.Sum(x => x.Cost);
        return new(stops, cost, cost, RouteTravelScore(stops, startSystem, startLocation));
    }

    private static RouteGroup[] OrderGroupsByTravel(RouteGroup[] groups,
        string? startSystem, string? startLocation)
    {
        var remaining = groups.ToList();
        var result = new List<RouteGroup>();
        var system = startSystem;
        var location = startLocation;
        while (remaining.Count > 0)
        {
            var next = remaining.OrderBy(x => TravelLegScore(system, location,
                    x.Shop.Key.System, x.Shop.Key.Location, x.Shop.Key.Shop))
                .ThenBy(x => x.Shop.Key.System).ThenBy(x => x.Shop.Key.Location).First();
            result.Add(next);
            remaining.Remove(next);
            system = next.Shop.Key.System;
            location = next.Shop.Key.Location;
        }
        return result.ToArray();
    }

    private static double RouteTravelScore(IReadOnlyList<ComponentShoppingStop> stops,
        string? startSystem, string? startLocation)
    {
        double score = 0;
        var system = startSystem;
        var location = startLocation;
        foreach (var stop in stops)
        {
            score += TravelLegScore(system, location, stop.System, stop.Location, stop.Shop);
            system = stop.System;
            location = stop.Location;
        }
        return score;
    }

    private static double TravelLegScore(string? fromSystem, string? fromLocation,
        string system, string location, string shop)
    {
        var score = LocationMatches(location, fromLocation) ? 0 :
            !string.IsNullOrWhiteSpace(fromSystem) && system.Equals(fromSystem, StringComparison.OrdinalIgnoreCase) ? 2 : 8;
        if (system.Equals("Pyro", StringComparison.OrdinalIgnoreCase)) score += 40;
        if (IsRisky(system, location, shop)) score += 12;
        if (system.Contains("Неизвест", StringComparison.OrdinalIgnoreCase)) score += 6;
        return score;
    }

    private static bool OfferAllowed(ComponentShopOffer offer, bool avoidPyro, bool allowRisky) =>
        (!avoidPyro || !offer.IsPyro) && (allowRisky || !IsRisky(offer.System, offer.Location, offer.Shop));

    private static bool IsRisky(params string[] values) => values.Any(x =>
        x.Contains("NQA", StringComparison.OrdinalIgnoreCase) ||
        x.Contains("No Questions", StringComparison.OrdinalIgnoreCase));

    private static int[] SelectStops(ShopCandidate[] shops, ComponentDemand[] demands,
        string? startSystem, string? startLocation)
    {
        var full = (1UL << demands.Length) - 1;
        var states = new Dictionary<ulong, StopState> { [0] = new([], 0, 0, 0, 0) };
        var queue = new Queue<ulong>();
        queue.Enqueue(0);
        while (queue.Count > 0)
        {
            var mask = queue.Dequeue();
            var state = states[mask];
            for (var shopIndex = 0; shopIndex < shops.Length; shopIndex++)
            {
                var nextMask = mask | shops[shopIndex].Coverage;
                if (nextMask == mask || state.Stops.Contains(shopIndex)) continue;
                var stops = state.Stops.Append(shopIndex).ToArray();
                var candidate = new StopState(stops,
                    stops.Count(x => shops[x].Key.System.Equals("Pyro", StringComparison.OrdinalIgnoreCase)),
                    stops.Count(x => !string.IsNullOrWhiteSpace(startSystem) &&
                        !shops[x].Key.System.Equals(startSystem, StringComparison.OrdinalIgnoreCase)),
                    stops.Count(x => !string.IsNullOrWhiteSpace(startLocation) &&
                        !LocationMatches(shops[x].Key.Location, startLocation)),
                    CoveredCost(nextMask, stops, shops, demands));
                if (states.TryGetValue(nextMask, out var existing) && !candidate.IsBetterThan(existing)) continue;
                states[nextMask] = candidate;
                queue.Enqueue(nextMask);
            }
        }
        return states.TryGetValue(full, out var result) ? result.Stops : [];
    }

    private static int[] SelectStopsGreedy(ShopCandidate[] shops, ComponentDemand[] demands,
        string? startSystem, string? startLocation)
    {
        var uncovered = demands.Select(x => x.Uuid).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = new List<int>();
        while (uncovered.Count > 0)
        {
            var next = Enumerable.Range(0, shops.Length).Where(x => !selected.Contains(x))
                .OrderByDescending(x => shops[x].Offers.Keys.Count(uncovered.Contains))
                .ThenBy(x => shops[x].Key.System.Equals("Pyro", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ThenBy(x => string.IsNullOrWhiteSpace(startSystem) ||
                    !shops[x].Key.System.Equals(startSystem, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ThenBy(x => LocationMatches(shops[x].Key.Location, startLocation) ? 0 : 1)
                .FirstOrDefault(-1);
            if (next < 0 || !shops[next].Offers.Keys.Any(uncovered.Contains)) break;
            selected.Add(next);
            uncovered.ExceptWith(shops[next].Offers.Keys);
        }
        return uncovered.Count == 0 ? selected.ToArray() : [];
    }

    private static ulong Coverage(IEnumerable<string> uuids, ComponentDemand[] demands)
    {
        ulong result = 0;
        for (var i = 0; i < demands.Length && i < 64; i++)
            if (uuids.Contains(demands[i].Uuid, StringComparer.OrdinalIgnoreCase)) result |= 1UL << i;
        return result;
    }

    private static decimal CoveredCost(ulong mask, IReadOnlyList<int> selected, ShopCandidate[] shops,
        ComponentDemand[] demands)
    {
        decimal total = 0;
        for (var i = 0; i < demands.Length; i++)
        {
            if ((mask & (1UL << i)) == 0) continue;
            var price = selected.Select(x => shops[x].Offers.GetValueOrDefault(demands[i].Uuid)?.PriceAuec)
                .Where(x => x is not null).Select(x => x!.Value).DefaultIfEmpty().Min();
            total += price * demands[i].Quantity;
        }
        return total;
    }

    private static bool LocationMatches(string candidate, string? current) =>
        !string.IsNullOrWhiteSpace(candidate) && !string.IsNullOrWhiteSpace(current) &&
        current is not "Не указана" and not "Not specified" &&
        (candidate.Equals(current, StringComparison.OrdinalIgnoreCase) ||
         candidate.Contains(current, StringComparison.OrdinalIgnoreCase) ||
         current.Contains(candidate, StringComparison.OrdinalIgnoreCase));

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
    private sealed record ComponentDemand(string Uuid, ShipComponent Component, int Quantity);
    private sealed record ShopCandidate((string System, string Location, string Shop) Key,
        IReadOnlyDictionary<string, ComponentShopOffer> Offers, ulong Coverage);
    private sealed record ShoppingRoute(IReadOnlyList<ComponentShoppingStop> Stops,
        decimal MinimumCost, decimal RouteCost, double TravelScore);
    private sealed record EngineeringSnapshot(double PowerSupply, double PowerDemand,
        double CoolantSupply, double CoolantDemand, double QuantumSpeed,
        double FuelConsumption, double QuantumRange);
    private sealed record RouteGroup(ShopCandidate Shop,
        List<(ComponentDemand Demand, ComponentShopOffer Offer)> Assigned);
    private sealed record CheapestGroup((string System, string Location, string Shop) Key,
        IReadOnlyList<(ComponentDemand Demand, ComponentShopOffer Offer)> Items);
    private sealed record StopState(int[] Stops, int PyroStops, int NonStartStops,
        int NonStartLocationStops, decimal Cost)
    {
        public bool IsBetterThan(StopState other) => Stops.Length < other.Stops.Length ||
            Stops.Length == other.Stops.Length && (PyroStops < other.PyroStops ||
            PyroStops == other.PyroStops && (NonStartStops < other.NonStartStops ||
            NonStartStops == other.NonStartStops && (NonStartLocationStops < other.NonStartLocationStops ||
            NonStartLocationStops == other.NonStartLocationStops && Cost < other.Cost)));
    }

    private sealed class StringTupleComparer : IEqualityComparer<(string System, string Location, string Shop)>
    {
        public static StringTupleComparer Instance { get; } = new();
        public bool Equals((string System, string Location, string Shop) x,
            (string System, string Location, string Shop) y) =>
            StringComparer.OrdinalIgnoreCase.Equals(x.System, y.System) &&
            StringComparer.OrdinalIgnoreCase.Equals(x.Location, y.Location) &&
            StringComparer.OrdinalIgnoreCase.Equals(x.Shop, y.Shop);
        public int GetHashCode((string System, string Location, string Shop) value) => HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(value.System),
            StringComparer.OrdinalIgnoreCase.GetHashCode(value.Location),
            StringComparer.OrdinalIgnoreCase.GetHashCode(value.Shop));
    }
}
