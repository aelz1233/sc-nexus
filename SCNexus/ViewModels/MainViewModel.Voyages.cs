using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SCNexus.Models;
using SCNexus.Services;

namespace SCNexus.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private string voyageMode = "Прямой рейс";
    [ObservableProperty] private int voyageMaxPurchases = 3;
    [ObservableProperty] private TerminalOption? voyageDestination;
    [ObservableProperty] private string voyageDestinationQuery = "";
    [ObservableProperty] private bool isVoyageLoading;
    [ObservableProperty] private string voyageStatus = "Нажми «Подобрать рейсы», чтобы построить план остановок.";
    [ObservableProperty] private VoyagePlan? activeVoyagePlan;
    [ObservableProperty] private int activeVoyageStopIndex;
    [ObservableProperty] private bool activeVoyageCompleted;
    private CancellationTokenSource? _voyageCancellation;
    private DateTimeOffset _activeVoyageStartedUtc;
    private readonly HashSet<string> _knownVoyageTradeIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _completedVoyageActions = new(StringComparer.OrdinalIgnoreCase);
    public string[] VoyageModes { get; } = ["Прямой рейс", "Цепочка", "Сбор груза"];
    public int[] VoyageStopLimits { get; } = [2, 3, 4, 5];
    public ObservableCollection<TerminalOption> VoyageDestinations { get; } = [];
    public ObservableCollection<VoyagePlan> VoyagePlans { get; } = [];
    public ObservableCollection<RouteSystemChoice> RouteSystems { get; } = [];
    private bool _updatingRouteSystems;
    public bool IsDirectVoyage => VoyageMode == "Прямой рейс";
    public bool IsMultiVoyage => !IsDirectVoyage;
    public bool HasActiveVoyage => ActiveVoyagePlan is not null;
    public VoyageStop? ActiveVoyageStop => ActiveVoyagePlan is { Stops.Count: > 0 } plan
        ? plan.Stops[Math.Clamp(ActiveVoyageStopIndex, 0, plan.Stops.Count - 1)] : null;
    public string ActiveVoyageStopDisplay => ActiveVoyagePlan is not { Stops.Count: > 0 } plan
        ? (IsEnglish ? "No active route" : "Нет активного маршрута")
        : ActiveVoyageCompleted
            ? IsEnglish ? $"Route complete · {plan.Stops.Count} stops" : $"Маршрут завершён · {plan.Stops.Count} остановок"
            : IsEnglish ? $"Stop {ActiveVoyageStopIndex + 1} of {plan.Stops.Count} · {ActiveVoyageStop?.Terminal} · {ActiveVoyageStop?.System}"
                : $"Остановка {ActiveVoyageStopIndex + 1} из {plan.Stops.Count} · {ActiveVoyageStop?.Terminal} · {ActiveVoyageStop?.System}";
    public string ActiveVoyageActionDisplay => ActiveVoyageCompleted
        ? IsEnglish ? "All stops are complete. Finish the trip in the journal after checking the actual amounts."
            : "Все остановки пройдены. Заверши рейс в журнале, когда фактические суммы проверены."
        : LocalizationService.T(ActiveVoyageStop?.Action ?? "");
    public string ActiveVoyageCargoDisplay => LocalizationService.T(ActiveVoyageStop?.LoadDisplay ?? "");
    public string ActiveVoyageProfitDisplay => ActiveVoyagePlan?.ProfitDisplay ?? "";
    public string ActiveVoyageNextDisplay => ActiveVoyagePlan is not { Stops.Count: > 0 } plan || ActiveVoyageCompleted
        ? ""
        : ActiveVoyageStopIndex + 1 < plan.Stops.Count
            ? IsEnglish ? $"Next: {plan.Stops[ActiveVoyageStopIndex + 1].Terminal} · {plan.Stops[ActiveVoyageStopIndex + 1].System}"
                : $"Далее: {plan.Stops[ActiveVoyageStopIndex + 1].Terminal} · {plan.Stops[ActiveVoyageStopIndex + 1].System}"
            : IsEnglish ? "Final stop" : "Финальная остановка";
    public double ActiveVoyageProgress => ActiveVoyagePlan is not { Stops.Count: > 0 } plan ? 0
        : ActiveVoyageCompleted ? 100 : 100d * (ActiveVoyageStopIndex + 1) / plan.Stops.Count;
    public string VoyageHelp => VoyageMode switch
    {
        "Цепочка" => "Цепочка: в A покупаешь груз, в B продаёшь его и покупаешь следующий, в C снова продаёшь. Пример: A → B → C → D. Трюм освобождается при каждой продаже, выручку можно вложить дальше. «До точек закупки» — максимум покупок, финальная продажа считается отдельной остановкой.",
        "Сбор груза" => "Сбор груза: покупаешь товары в A и B, везёшь их вместе и продаёшь всё в C. Пример: купить в A → докупить в B → разгрузить в C. До конечной точки продаж нет: все товары должны поместиться в один трюм, а закупки — в исходный бюджет.",
        _ => "Прямой рейс: купить один товар в A и продать в B. Две остановки, одна покупка и одна продажа. Подходит для быстрого выбора следующего рейса."
    };
    private HashSet<string>? AllowedRouteSystems => RouteSystems.Count == 0 ? null : RouteSystems.Where(x => x.IsSelected).Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
    public string RouteSystemsSummary => RouteSystems.Count == 0 ? "Системы появятся после загрузки котировок." :
        RouteSystems.All(x => !x.IsSelected) ? "Ни одна система не выбрана — выбери хотя бы одну." :
        "Все остановки только в выбранных системах: " + string.Join(", ", RouteSystems.Where(x => x.IsSelected).Select(x => x.Name));

    private void RefreshRouteSystems()
    {
        var previous = RouteSystems.ToDictionary(x => x.Name, x => x.IsSelected, StringComparer.OrdinalIgnoreCase);
        RouteSystems.Clear();
        if (_haulingData is not null)
            foreach (var name in _haulingData.Terminals.Where(x => x.Type == "commodity" && x.IsAvailableLive == 1)
                .Select(x => x.StarSystemName ?? "Неизвестно").Distinct(StringComparer.OrdinalIgnoreCase).Order())
            {
                var choice = new RouteSystemChoice(name, previous.GetValueOrDefault(name, true));
                choice.PropertyChanged += (_, _) =>
                {
                    if (!_updatingRouteSystems)
                        ApplyRouteSystemSelection();
                };
                RouteSystems.Add(choice);
            }
        OnPropertyChanged(nameof(RouteSystemsSummary));
    }
    private void ApplyRouteSystemSelection()
    {
        OnPropertyChanged(nameof(RouteSystemsSummary));
        RefreshVoyageDestinations();
        RecalculateHauling();
    }

    [RelayCommand]
    private void SelectAllRouteSystems()
    {
        _updatingRouteSystems = true;
        try { foreach (var system in RouteSystems) system.IsSelected = true; }
        finally { _updatingRouteSystems = false; }
        ApplyRouteSystemSelection();
    }

    [RelayCommand]
    private void ClearRouteSystems()
    {
        _updatingRouteSystems = true;
        try { foreach (var system in RouteSystems) system.IsSelected = false; }
        finally { _updatingRouteSystems = false; }
        ApplyRouteSystemSelection();
    }

    partial void OnVoyageModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsDirectVoyage)); OnPropertyChanged(nameof(IsMultiVoyage)); OnPropertyChanged(nameof(VoyageHelp));
        InvalidateVoyages(); RefreshVoyageDestinations();
    }
    partial void OnVoyageMaxPurchasesChanged(int value) => InvalidateVoyages();
    partial void OnVoyageDestinationChanged(TerminalOption? value) => InvalidateVoyages();
    partial void OnVoyageDestinationQueryChanged(string value) => RefreshVoyageDestinations();

    private void InvalidateVoyages()
    {
        _voyageCancellation?.Cancel();
        VoyagePlans.Clear();
        VoyageStatus = "Нажми «Подобрать рейсы» для текущего корабля и фильтров.";
    }

    private void RefreshVoyageDestinations()
    {
        var selected = VoyageDestination;
        var allowedSystems = AllowedRouteSystems;
        VoyageDestinations.Clear();
        VoyageDestinations.Add(new(0, "", ""));
        if (_haulingData is not null)
            foreach (var t in _haulingData.Terminals.Where(t => t.Type == "commodity" && t.IsAvailableLive == 1)
                .Where(t => allowedSystems is null || allowedSystems.Contains(t.StarSystemName ?? "Неизвестно"))
                .Where(t => string.IsNullOrWhiteSpace(VoyageDestinationQuery) || $"{t.StarSystemName} {t.Name}".Contains(VoyageDestinationQuery.Trim(), StringComparison.OrdinalIgnoreCase))
                .OrderBy(t => t.StarSystemName).ThenBy(t => t.Name))
                VoyageDestinations.Add(new(t.Id, t.Name, t.StarSystemName ?? "Неизвестно"));
        VoyageDestination = VoyageDestinations.FirstOrDefault(x => x.Id == selected?.Id) ?? VoyageDestinations[0];
    }

    [RelayCommand]
    private async Task BuildVoyagesAsync()
    {
        if (SelectedShip is null) { VoyageStatus = "Сначала выбери корабль из своего флота."; return; }
        if (_haulingData is null) await LoadMarketAsync(false);
        if (_haulingData is null) { VoyageStatus = "Нет котировок. Обнови цены и попробуй снова."; return; }
        if (SelectedShip is null) { VoyageStatus = "Выбери корабль для расчёта."; return; }
        _voyageCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource();
        _voyageCancellation = cancellation;
        IsVoyageLoading = true;
        VoyagePlans.Clear();
        VoyageStatus = "Подбираю остановки, товары и объёмы…";
        var request = new VoyageRequest(VoyageMode, SelectedShip.Ship.CargoScu, Math.Max(0, Balance - Reserve),
            VoyageMaxPurchases, VoyageDestination?.Id ?? 0, CurrentLocation, CurrentSystem,
            AllowRisky, AvoidPyro, HaulingSameSystemOnly, MinimumFillPercent, MinimumProfit, HaulingCategory, AllowedRouteSystems);
        var data = _haulingData;
        try
        {
            var plans = await Task.Run(() => new VoyagePlanner().Calculate(data, request, cancellation.Token));
            cancellation.Token.ThrowIfCancellationRequested();
            foreach (var plan in plans) VoyagePlans.Add(plan);
            VoyageStatus = plans.Count == 0 ? "Нет подходящих планов. Попробуй другую конечную точку, больше остановок или мягче фильтры."
                : $"Подобрано планов: {plans.Count}. Сортировка по суммарной прибыли; время и топливо не учтены.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { VoyageStatus = $"Не удалось построить план: {ex.Message}"; }
        finally { if (_voyageCancellation == cancellation) _voyageCancellation = null; IsVoyageLoading = false; }
    }

    [RelayCommand]
    private void PrepareVoyage(VoyagePlan? plan)
    {
        if (plan is null) return;
        if (HasActiveFlight) { OpenHistory(); FlightStatus = "Сначала заверши текущий рейс."; return; }
        FlightOrigin = plan.Stops[0].Terminal;
        FlightDestination = plan.Stops[^1].Terminal;
        FlightCommodity = plan.Manifest;
        FlightInvestment = plan.Investment;
        FlightRevenue = 0; FlightExpenses = 0; FlightLosses = 0;
        ActivateVoyageGuidance(plan);
        OpenHistory();
        FlightStatus = "План перенесён в рейс. Nexus будет переключать остановки по локации и подтверждённым торговым событиям.";
    }

    private void ActivateVoyageGuidance(VoyagePlan plan)
    {
        ActiveVoyagePlan = plan;
        ActiveVoyageStopIndex = 0;
        ActiveVoyageCompleted = false;
        _activeVoyageStartedUtc = DateTimeOffset.UtcNow;
        _knownVoyageTradeIds.Clear();
        foreach (var trade in _lastDataSnapshot.Trades) _knownVoyageTradeIds.Add(trade.Id);
        _completedVoyageActions.Clear();
        RefreshActiveVoyageProperties();
        QueueSave();
    }

    [RelayCommand]
    private void PreviousVoyageStop()
    {
        if (ActiveVoyagePlan is null || ActiveVoyageStopIndex <= 0) return;
        ActiveVoyageStopIndex--;
        ActiveVoyageCompleted = false;
        _completedVoyageActions.Clear();
        RefreshActiveVoyageProperties();
        QueueSave();
    }

    [RelayCommand]
    private void NextVoyageStop()
    {
        if (ActiveVoyagePlan is not { Stops.Count: > 0 } plan) return;
        if (ActiveVoyageStopIndex + 1 < plan.Stops.Count) ActiveVoyageStopIndex++;
        else ActiveVoyageCompleted = true;
        _completedVoyageActions.Clear();
        RefreshActiveVoyageProperties();
        QueueSave();
    }

    [RelayCommand]
    private void CancelVoyageGuidance()
    {
        ActiveVoyagePlan = null;
        ActiveVoyageStopIndex = 0;
        ActiveVoyageCompleted = false;
        _completedVoyageActions.Clear();
        _knownVoyageTradeIds.Clear();
        RefreshActiveVoyageProperties();
        QueueSave();
    }

    internal void UpdateVoyageProgress(DataCollectionSnapshot snapshot)
    {
        if (ActiveVoyagePlan is not { Stops.Count: > 0 } plan || ActiveVoyageCompleted) return;
        var changed = false;
        var previousStopIndex = ActiveVoyageStopIndex;
        var detectedLocation = snapshot.Player.CurrentLocation?.Value;
        if (!string.IsNullOrWhiteSpace(detectedLocation))
        {
            for (var i = plan.Stops.Count - 1; i > ActiveVoyageStopIndex; i--)
            {
                if (!LocationMatches(detectedLocation, plan.Stops[i].Terminal)) continue;
                ActiveVoyageStopIndex = i;
                _completedVoyageActions.Clear();
                changed = true;
                break;
            }
        }

        foreach (var trade in snapshot.Trades.OrderBy(x => x.Amount.Timestamp))
        {
            if (!_knownVoyageTradeIds.Add(trade.Id) || trade.Amount.Timestamp < _activeVoyageStartedUtc) continue;
            var stop = ActiveVoyageStop;
            if (stop is null || !TradeMatchesStop(trade, stop, detectedLocation)) continue;
            var expected = ExpectedActions(stop).ToArray();
            var action = trade.Action.Value.Equals("purchase", StringComparison.OrdinalIgnoreCase) ? "Купить" :
                trade.Action.Value.Equals("sale", StringComparison.OrdinalIgnoreCase) ? "Продать" : "";
            if (action.Length == 0) continue;
            var commodity = trade.Commodity?.Value;
            var matches = string.IsNullOrWhiteSpace(commodity) ? [] : expected.Where(x => x.Action == action &&
                (x.Commodity.Contains(commodity, StringComparison.OrdinalIgnoreCase) ||
                 commodity.Contains(x.Commodity, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (matches.Length == 0 && string.IsNullOrWhiteSpace(commodity))
                matches = expected.Where(x => x.Action == action).Take(1).ToArray();
            foreach (var match in matches) changed |= _completedVoyageActions.Add(match.Key);
            if (expected.Length == 0 || expected.Any(x => !_completedVoyageActions.Contains(x.Key))) continue;
            if (ActiveVoyageStopIndex + 1 < plan.Stops.Count) ActiveVoyageStopIndex++;
            else ActiveVoyageCompleted = true;
            _completedVoyageActions.Clear();
            changed = true;
        }
        if (!changed) return;
        RefreshActiveVoyageProperties();
        if (previousStopIndex != ActiveVoyageStopIndex || ActiveVoyageCompleted)
        RaiseNotification($"voyage:{ActiveVoyageStopIndex}:{ActiveVoyageCompleted}",
            ActiveVoyageCompleted ? (IsEnglish ? "Route completed" : "Маршрут завершён")
                : (IsEnglish ? "Next route stop" : "Следующая остановка маршрута"),
            ActiveVoyageCompleted ? ActiveVoyageStopDisplay : $"{ActiveVoyageStopDisplay}. {ActiveVoyageActionDisplay}",
            ActiveVoyageCompleted ? NexusNotificationKind.Success : NexusNotificationKind.Info);
        QueueSave();
    }

    private static IEnumerable<VoyageAction> ExpectedActions(VoyageStop stop)
    {
        foreach (var raw in stop.Action.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var action = raw.StartsWith("Купить ", StringComparison.OrdinalIgnoreCase) ? "Купить" :
                raw.StartsWith("Продать ", StringComparison.OrdinalIgnoreCase) ? "Продать" : "";
            if (action.Length == 0) continue;
            var rest = raw[action.Length..].Trim();
            var separator = rest.IndexOf(':');
            var commodity = (separator >= 0 ? rest[..separator] : rest).Trim();
            if (commodity.Length > 0) yield return new VoyageAction(action, commodity);
        }
    }

    private static bool TradeMatchesStop(TradeEvent trade, VoyageStop stop, string? detectedLocation)
    {
        if (trade.Terminal is { Value.Length: > 0 } terminal)
            return LocationMatches(terminal.Value, stop.Terminal);
        return !string.IsNullOrWhiteSpace(detectedLocation) && LocationMatches(detectedLocation, stop.Terminal);
    }

    internal static bool LocationMatches(string left, string right)
    {
        static string Normalize(string value) => new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        static string[] Tokens(string value) => value.ToLowerInvariant()
            .Split([' ', '-', '_', '.', ',', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
            .Where(x => (x.Length >= 3 || x.Any(char.IsDigit)) && x is not "admin" and not "station"
                and not "terminal" and not "mining" and not "area" and not "research" and not "outpost"
                and not "service" and not "center" and not "centre" and not "platform")
            .ToArray();
        var normalizedLeft = Normalize(left);
        var normalizedRight = Normalize(right);
        if (Math.Min(normalizedLeft.Length, normalizedRight.Length) >= 5 &&
            (normalizedLeft.Contains(normalizedRight, StringComparison.Ordinal) ||
             normalizedRight.Contains(normalizedLeft, StringComparison.Ordinal))) return true;
        var a = Tokens(left);
        var b = Tokens(right);
        if (a.Length == 0 || b.Length == 0) return false;
        var aNumeric = a.Where(x => x.Any(char.IsDigit)).ToArray();
        var bNumeric = b.Where(x => x.Any(char.IsDigit)).ToArray();
        if (aNumeric.Length > 0 || bNumeric.Length > 0)
            return aNumeric.Intersect(bNumeric, StringComparer.OrdinalIgnoreCase).Any() &&
                a.Where(x => !x.Any(char.IsDigit)).Intersect(b.Where(x => !x.Any(char.IsDigit)),
                    StringComparer.OrdinalIgnoreCase).Any();
        return a.Intersect(b, StringComparer.OrdinalIgnoreCase).Any();
    }

    private void RestoreActiveVoyage(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            var state = JsonSerializer.Deserialize<VoyageGuidanceState>(json);
            if (state?.Plan.Stops.Count is not > 0) return;
            ActiveVoyagePlan = state.Plan;
            ActiveVoyageStopIndex = Math.Clamp(state.StopIndex, 0, state.Plan.Stops.Count - 1);
            ActiveVoyageCompleted = state.Completed;
            _activeVoyageStartedUtc = state.StartedAtUtc;
            _completedVoyageActions.Clear();
            foreach (var key in state.CompletedActions ?? []) _completedVoyageActions.Add(key);
            RefreshActiveVoyageProperties();
        }
        catch (JsonException) { }
    }

    private string SerializeActiveVoyage()
    {
        if (ActiveVoyagePlan is null) return "";
        try { return JsonSerializer.Serialize(new VoyageGuidanceState(ActiveVoyagePlan, ActiveVoyageStopIndex,
            ActiveVoyageCompleted, _activeVoyageStartedUtc, _completedVoyageActions.ToArray())); }
        catch (NotSupportedException) { return ""; }
    }

    private void RefreshActiveVoyageProperties()
    {
        RefreshOverlayChecklist();
        OnPropertyChanged(nameof(HasActiveVoyage));
        OnPropertyChanged(nameof(ActiveVoyageStop));
        OnPropertyChanged(nameof(ActiveVoyageStopDisplay));
        OnPropertyChanged(nameof(ActiveVoyageActionDisplay));
        OnPropertyChanged(nameof(ActiveVoyageCargoDisplay));
        OnPropertyChanged(nameof(ActiveVoyageProfitDisplay));
        OnPropertyChanged(nameof(ActiveVoyageNextDisplay));
        OnPropertyChanged(nameof(ActiveVoyageProgress));
        NotifyOverlayChanged();
    }

    private sealed record VoyageAction(string Action, string Commodity)
    {
        public string Key => $"{Action}:{Commodity}";
    }

    [RelayCommand]
    private async Task ExportVoyageAsync(VoyagePlan? plan)
    {
        if (plan is null) return;
        var dialog = new SaveFileDialog { Title = "Сохранить план рейса", FileName = "SCNexus-рейс.txt", Filter = "Текстовый файл|*.txt" };
        if (dialog.ShowDialog() != true) return;
        try { await File.WriteAllTextAsync(dialog.FileName, plan.ExportText); VoyageStatus = "План сохранён."; }
        catch (Exception ex) { VoyageStatus = $"Не удалось сохранить план: {ex.Message}"; }
    }
}
