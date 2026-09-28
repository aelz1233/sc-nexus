using System.Collections.ObjectModel;
using System.IO;
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
    private CancellationTokenSource? _voyageCancellation;
    public string[] VoyageModes { get; } = ["Прямой рейс", "Цепочка", "Сбор груза"];
    public int[] VoyageStopLimits { get; } = [2, 3, 4, 5];
    public ObservableCollection<TerminalOption> VoyageDestinations { get; } = [];
    public ObservableCollection<VoyagePlan> VoyagePlans { get; } = [];
    public ObservableCollection<RouteSystemChoice> RouteSystems { get; } = [];
    public bool IsDirectVoyage => VoyageMode == "Прямой рейс";
    public bool IsMultiVoyage => !IsDirectVoyage;
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
                    OnPropertyChanged(nameof(RouteSystemsSummary));
                    RefreshVoyageDestinations();
                    RecalculateHauling();
                };
                RouteSystems.Add(choice);
            }
        OnPropertyChanged(nameof(RouteSystemsSummary));
    }
    [RelayCommand] private void SelectAllRouteSystems() { foreach (var system in RouteSystems) system.IsSelected = true; }
    [RelayCommand] private void ClearRouteSystems() { foreach (var system in RouteSystems) system.IsSelected = false; }

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
        OpenHistory();
        FlightStatus = "План остановок перенесён в описание рейса. При завершении укажи общие фактические закупки и продажи за все участки.";
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
