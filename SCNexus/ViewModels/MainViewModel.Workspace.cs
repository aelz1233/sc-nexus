using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SCNexus.Models;
using SCNexus.Services;

namespace SCNexus.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private string activePage = "Обзор";
    [ObservableProperty] private bool monitorEnabled = true;
    [ObservableProperty] private int monitorIntervalSeconds = 15;
    [ObservableProperty] private bool showRouteDetails = true;
    [ObservableProperty] private string resolvedGameDirectory = "Ищу установку игры…";
    [ObservableProperty] private string workspaceStatus = "";
    [ObservableProperty] private bool isMarketLoading;
    [ObservableProperty] private string catalogQuery = "";
    public ObservableCollection<VehicleCatalogItem> CatalogShips { get; } = [];
    public int[] MonitorIntervals { get; } = [15, 30, 60];
    public string DataDirectory => Path.GetDirectoryName(settingsService.DatabasePath)!;
    public bool HasNoShips => Ships.Count == 0;
    public bool HasNoFlights => Flights.Count == 0;
    public bool CanStartFlight => !HasActiveFlight && SelectedShip is not null;
    public bool HasNoHaulingRoutes => HaulingRoutes.Count == 0 && !IsMarketLoading;
    public string FleetSummary => $"Кораблей во флоте: {Ships.Count} · {Ships.Sum(x => x.Ship.CargoScu):N0} SCU всего";
    public string TradeBudgetHint => Reserve > Balance ? "Резерв больше баланса — денег на закупку нет." : $"На закупку: {Math.Max(0, Balance - Reserve):N0} aUEC";
    public string PageTitle => LocalizationService.T(ActivePage == "Рейсы" ? "Журнал" : ActivePage);
    public string PageDescription => LocalizationService.T(ActivePage switch
    {
        "Маршруты" => "Корабль, бюджет и подходящие торговые рейсы",
        "Флот" => "Твои корабли и каталог моделей",
        "Рейсы" => "События игры, текущий рейс и история",
        "Инструменты" => "Состояние игры, проверка файлов и журнал сессий",
        "Настройки" => "Подключение к игре, отображение и сохранение данных",
        _ => "Всё для следующего вылета"
    });

    partial void OnActivePageChanged(string value) { OnPropertyChanged(nameof(PageTitle)); OnPropertyChanged(nameof(PageDescription)); }
    partial void OnMonitorEnabledChanged(bool value) => QueueSave();
    partial void OnMonitorIntervalSecondsChanged(int value) => QueueSave();
    partial void OnShowRouteDetailsChanged(bool value) => QueueSave();
    partial void OnIsMarketLoadingChanged(bool value) => OnPropertyChanged(nameof(HasNoHaulingRoutes));
    partial void OnCatalogQueryChanged(string value) => ScheduleCatalogRefresh();

    private async void ScheduleCatalogRefresh()
    {
        _catalogSearchCancellation?.Cancel();
        var cancellation = _catalogSearchCancellation = new CancellationTokenSource();
        try
        {
            await Task.Delay(160, cancellation.Token);
            if (!cancellation.IsCancellationRequested) RefreshCatalog();
        }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(_catalogSearchCancellation, cancellation)) _catalogSearchCancellation = null; cancellation.Dispose(); }
    }

    private void Navigate(string page)
    {
        IsSettingsOpen = page == "Настройки";
        IsFleetOpen = page == "Флот";
        IsHistoryOpen = page == "Рейсы";
        IsToolsOpen = page == "Инструменты";
        IsHaulingOpen = page == "Маршруты";
        ActivePage = page;
    }

    private void RefreshCatalog()
    {
        var selected = SelectedCatalogVehicle;
        CatalogShips.Clear();
        foreach (var vehicle in VehicleCatalog.Search(_allVehicles, CatalogQuery, ShipSortMode)) CatalogShips.Add(vehicle);
        if (selected is not null && CatalogShips.Contains(selected)) SelectedCatalogVehicle = selected;
        else
        {
            SelectedCatalogVehicle = null;
            VehicleStatus = $"В списке: {CatalogShips.Count}. Выбери модель для заполнения вместимости и роли.";
        }
    }

    private void NotifyWorkspace()
    {
        foreach (var name in new[] { nameof(HasNoShips), nameof(HasNoFlights), nameof(FleetSummary), nameof(CanStartFlight), nameof(HasNoHaulingRoutes), nameof(TradeBudgetHint) })
            OnPropertyChanged(name);
    }

    [RelayCommand] private void ResetRouteFilters()
    {
        AvoidPyro = false; AllowRisky = false;
        MinimumFillPercent = 0; MinimumProfit = 0;
        HaulingSameSystemOnly = false; HaulingCategory = "Все маршруты";
        SelectAllRouteSystems();
        WorkspaceStatus = "Ограничения сброшены. Стартовая точка и сортировка сохранены.";
    }

    [RelayCommand] private Task RefreshMarketAsync() => LoadMarketAsync(true);

    private async Task LoadMarketAsync(bool forceRefresh)
    {
        if (IsMarketLoading) return;
        IsMarketLoading = true;
        HaulingStatus = "Загружаю котировки и объёмы…";
        try
        {
            _haulingData = await gameDataService.GetSnapshotAsync(forceRefresh: forceRefresh);
            RefreshRouteSystems();
            RefreshVoyageDestinations();
            DataStatus = $"UEX · {_haulingData.PricesFetchedAt.LocalDateTime:dd.MM HH:mm}" + (_haulingData.UsedOldCache ? " · сохранённые данные" : "");
            RecalculateHauling();
        }
        catch (Exception ex) { HaulingStatus = ex.Message; }
        finally { IsMarketLoading = false; }
    }

    [RelayCommand] private async Task ChooseGameFolderAsync()
    {
        var dialog = new OpenFolderDialog { Title = "Выбери папку LIVE, PTU или EPTU с установленной игрой" };
        if (dialog.ShowDialog() != true) return;
        var path = dialog.FolderName;
        if (!File.Exists(Path.Combine(path, "Game.log")) && !File.Exists(Path.Combine(path, "Bin64", "StarCitizen.exe")))
        {
            WorkspaceStatus = "В этой папке не найден Game.log или Bin64\\StarCitizen.exe. Выбери папку LIVE, PTU или EPTU.";
            return;
        }
        GameDirectoryPath = path;
        WorkspaceStatus = "Папка игры выбрана.";
        await RefreshToolsAsync();
    }

    [RelayCommand] private async Task AutoDetectGameAsync()
    {
        GameDirectoryPath = "";
        try
        {
            await RefreshGameInfoAsync();
            WorkspaceStatus = Directory.Exists(ResolvedGameDirectory)
                ? $"Игра найдена: {ResolvedGameDirectory}"
                : "Игра не найдена. Выбери папку вручную.";
        }
        catch (Exception ex) { WorkspaceStatus = $"Не удалось проверить игру: {ex.Message}"; }
    }

    [RelayCommand] private void OpenDataFolder() => OpenFolder(DataDirectory);
    [RelayCommand] private void OpenGameFolder()
    {
        var path = gameLogService.ResolveGameDirectory();
        if (path is null) { WorkspaceStatus = "Сначала выбери папку игры."; return; }
        OpenFolder(path);
    }
    private void OpenFolder(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { WorkspaceStatus = $"Не удалось открыть папку: {ex.Message}"; }
    }

    [RelayCommand] private async Task BackupDataAsync()
    {
        var dialog = new SaveFileDialog { Title = "Сохранить резервную копию", Filter = "База SC NEXUS (*.db)|*.db", FileName = $"SCNexus-{DateTime.Now:yyyy-MM-dd-HHmm}.db" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            await SaveNowAsync();
            await settingsService.BackupAsync(dialog.FileName);
            WorkspaceStatus = $"Резервная копия сохранена: {dialog.FileName}";
        }
        catch (Exception ex) { WorkspaceStatus = $"Не удалось сохранить копию: {ex.Message}"; }
    }

    [RelayCommand] private async Task ExportFlightsAsync()
    {
        var dialog = new SaveFileDialog { Title = "Экспорт истории рейсов", Filter = "Таблица CSV (*.csv)|*.csv", FileName = $"Рейсы-{DateTime.Now:yyyy-MM-dd}.csv" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            await File.WriteAllTextAsync(dialog.FileName, FlightExport.ToCsv(Flights), new UTF8Encoding(true));
            WorkspaceStatus = $"История сохранена: {dialog.FileName}";
        }
        catch (Exception ex) { WorkspaceStatus = $"Ошибка экспорта: {ex.Message}"; }
    }

    [RelayCommand] private async Task ExportDiagnosticsAsync()
    {
        var dialog = new SaveFileDialog { Title = "Сохранить отчёт проверки", Filter = "Текстовый отчёт (*.txt)|*.txt", FileName = $"SCNexus-проверка-{DateTime.Now:yyyy-MM-dd}.txt" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var report = $"SC NEXUS · {DateTime.Now:dd.MM.yyyy HH:mm}\n{GameProcessStatus}\n{GameRegion}\nСервер: {GameShard}\n\n" +
                string.Join("\n\n", GameHealthFindings.Select(x => $"{x.Title}\n{x.Detail}"));
            await File.WriteAllTextAsync(dialog.FileName, report, Encoding.UTF8);
            WorkspaceStatus = $"Отчёт сохранён: {dialog.FileName}";
        }
        catch (Exception ex) { WorkspaceStatus = $"Ошибка экспорта: {ex.Message}"; }
    }
}
