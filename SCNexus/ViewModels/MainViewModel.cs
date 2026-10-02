using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SCNexus.Models;
using SCNexus.Services;

namespace SCNexus.ViewModels;

public partial class MainViewModel(SettingsService settingsService, TradingService tradingService,
    FlightLogService flightLogService, GameDataService gameDataService, GameLogService gameLogService,
    HaulingService haulingService, UpdateService updateService, DataCollectionService? dataCollectionService = null) : ObservableObject
{
    private bool _loaded;
    private bool _selectingLocation;
    private IReadOnlyList<LocationOption> _allLocations = [];
    private IReadOnlyList<VehicleCatalogItem> _allVehicles = [];
    private DataSnapshot? _haulingData;
    private IReadOnlyList<HaulingRoute> _allHaulingRoutes = [];
    private int _visibleHaulingCount;
    private IReadOnlyList<TradeRoute> _recommendedRoutes = [];
    private int _visibleRouteCount;
    private CancellationTokenSource? _pendingSave;

    [ObservableProperty] private decimal balance;
    [ObservableProperty] private string currentShip = "Не выбран";
    [ObservableProperty] private string currentLocation = "Не указана";
    [ObservableProperty] private string currentSystem = "";
    [ObservableProperty] private int cargoScu;
    [ObservableProperty] private decimal reserve;
    [ObservableProperty] private bool allowRisky;
    [ObservableProperty] private bool avoidPyro;
    [ObservableProperty] private int minimumFillPercent;
    [ObservableProperty] private decimal minimumProfit;
    [ObservableProperty] private string gameDirectoryPath = "";
    [ObservableProperty] private bool isSettingsOpen;
    [ObservableProperty] private bool isFleetOpen;
    [ObservableProperty] private bool isConfiguratorOpen;
    [ObservableProperty] private bool isHistoryOpen;
    [ObservableProperty] private bool isToolsOpen;
    [ObservableProperty] private bool isHaulingOpen;
    [ObservableProperty] private bool showRecommendation;
    [ObservableProperty] private string recommendationMessage = "";
    [ObservableProperty] private string dataStatus = "UEX • ещё не загружено";
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string saveStatus = "Локальные настройки";
    [ObservableProperty] private string flightStatus = "";
    [ObservableProperty] private string newShipName = "";
    [ObservableProperty] private int newShipCargoScu;
    [ObservableProperty] private VehicleCatalogItem? selectedCatalogVehicle;
    [ObservableProperty] private string vehicleStatus = "Каталог кораблей загружается…";
    [ObservableProperty] private string gameLogStatus = "Ищу журнал Star Citizen на компьютере…";
    [ObservableProperty] private string gameProcessStatus = "Проверяю игру…";
    [ObservableProperty] private string gameRegion = "Регион не определён";
    [ObservableProperty] private string gameShard = "Не определён";
    [ObservableProperty] private string gameLogUpdated = "Нет данных";
    [ObservableProperty] private string sessionStatus = "История сессий загружается…";
    [ObservableProperty] private string haulingStatus = "Открой планировщик, чтобы загрузить котировки UEX.";
    [ObservableProperty] private string haulingSortMode = "За рейс";
    [ObservableProperty] private string haulingCategory = "Все маршруты";
    [ObservableProperty] private bool haulingSameSystemOnly;
    [ObservableProperty] private HaulingRoute? haulingBestRoute;
    [ObservableProperty] private string newShipRole = "";
    [ObservableProperty] private string newShipBuild = "";
    [ObservableProperty] private ShipSummary? selectedShip;
    [ObservableProperty] private string shipSortMode = "По названию";
    [ObservableProperty] private string selectedSystem = "Все системы";
    [ObservableProperty] private string locationQuery = "";
    [ObservableProperty] private string locationStatus = "Локации загружаются…";
    [ObservableProperty] private bool showLocationSuggestions;
    [ObservableProperty] private string flightOrigin = "";
    [ObservableProperty] private string flightDestination = "";
    [ObservableProperty] private string flightCommodity = "";
    [ObservableProperty] private decimal flightInvestment;
    [ObservableProperty] private decimal flightRevenue;
    [ObservableProperty] private decimal flightExpenses;
    [ObservableProperty] private decimal flightLosses;
    [ObservableProperty] private FlightRecord? activeFlight;

    public string BalanceDisplay => $"{Balance:N0} aUEC";
    public string HaulingBudgetDisplay => $"{Math.Max(0, Balance - Reserve):N0} aUEC";
    public string HaulingStartDisplay => CurrentLocation == "Не указана" ? "Все локации" : LocationDisplay;
    public string LocationDisplay => string.IsNullOrWhiteSpace(CurrentSystem) ? CurrentLocation : $"{CurrentSystem} · {CurrentLocation}";
    public ObservableCollection<TradeRoute> Routes { get; } = [];
    public ObservableCollection<ShipSummary> Ships { get; } = [];
    public ObservableCollection<ShipSummary> SortedShips { get; } = [];
    public ObservableCollection<GameTradeCandidate> GameTrades { get; } = [];
    public ObservableCollection<GameHealthFinding> GameHealthFindings { get; } = [];
    public ObservableCollection<GameSessionSummary> GameSessions { get; } = [];
    public ObservableCollection<HaulingRoute> HaulingRoutes { get; } = [];
    public string[] HaulingSortOptions { get; } = HaulingService.SortOptions;
    public string[] HaulingCategories { get; } = HaulingService.Categories;
    public int[] MinimumFillOptions { get; } = [0, 25, 50, 75, 100];
    public string[] ShipSortOptions { get; } = ["По названию", "По вместимости"];
    public ObservableCollection<string> Systems { get; } = ["Все системы"];
    public ObservableCollection<LocationOption> FilteredLocations { get; } = [];
    public ObservableCollection<FlightRecord> Flights { get; } = [];
    public bool IsDashboardOpen => !IsSettingsOpen && !IsFleetOpen && !IsConfiguratorOpen && !IsHistoryOpen && !IsToolsOpen && !IsHaulingOpen;
    public bool HasActiveFlight => ActiveFlight is not null;
    public bool HasHaulingBestRoute => HaulingBestRoute is not null;
    public bool HasMoreHaulingRoutes => _visibleHaulingCount < _allHaulingRoutes.Count;
    public string HaulingCountDisplay => _allHaulingRoutes.Count == 0 ? "" :
        $"Показано {_visibleHaulingCount} из {_allHaulingRoutes.Count}";
    public bool HasMoreRoutes => _visibleRouteCount < _recommendedRoutes.Count;
    public string RecommendedCountDisplay => _recommendedRoutes.Count == 0 ? "" :
        $"Показано {_visibleRouteCount} из {_recommendedRoutes.Count} маршрутов";
    public string ActiveFlightDisplay => ActiveFlight is null ? "Нет активного рейса" :
        $"{ActiveFlight.ShipName} · {ActiveFlight.Commodity} · {ActiveFlight.Origin} → {ActiveFlight.Destination}";
    public string PersonalProfitHourDisplay
    {
        get
        {
            var completed = Flights.Where(x => x.EndedAtUtc != null).ToList();
            var hours = completed.Sum(x => x.DurationHours);
            return hours <= 0 ? "Нет данных" : $"{completed.Sum(x => x.Profit) / (decimal)hours:N0} aUEC / час";
        }
    }
    public string TotalFlightProfitDisplay => $"{Flights.Where(x => x.EndedAtUtc != null).Sum(x => x.Profit):+#,##0;-#,##0;0} aUEC";
    public string TodayProfitDisplay => $"{Flights.Where(x => x.EndedAtUtc?.ToLocalTime().Date == DateTime.Today).Sum(x => x.Profit):+#,##0;-#,##0;0} aUEC";

    public async Task InitializeAsync()
    {
        var settings = await settingsService.LoadAsync();
        Balance = settings.Balance;
        CurrentShip = settings.CurrentShip;
        CurrentLocation = settings.CurrentLocation;
        CurrentSystem = settings.CurrentSystem;
        LocationQuery = settings.CurrentLocation == "Не указана" ? "" : settings.CurrentLocation;
        CargoScu = settings.CargoScu;
        Reserve = settings.Reserve;
        AllowRisky = settings.AllowRisky;
        AvoidPyro = settings.AvoidPyro;
        MinimumFillPercent = settings.MinimumFillPercent;
        MinimumProfit = settings.MinimumProfit;
        GameDirectoryPath = settings.GameDirectoryPath;
        MonitorEnabled = settings.MonitorEnabled;
        MonitorIntervalSeconds = MonitorIntervals.Contains(settings.MonitorIntervalSeconds) ? settings.MonitorIntervalSeconds : 15;
        ShowRouteDetails = settings.ShowRouteDetails;
        OcrEnabled = settings.OcrEnabled;
        Language = settings.Language is "en" ? "en" : "ru";
        LocalizationService.SetLanguage(Language);
        gameLogService.GameDirectoryOverride = GameDirectoryPath;
        await ReloadFlightLogAsync();
        if (CurrentShip != "Не выбран" && Ships.All(x => !x.Name.Equals(CurrentShip, StringComparison.OrdinalIgnoreCase)))
        {
            await flightLogService.AddShipAsync(CurrentShip, CargoScu, "Торговля", "Импортирован из настроек");
            await ReloadFlightLogAsync();
        }
        if (SelectedShip is { } ship)
        {
            CurrentShip = ship.Name;
            CargoScu = ship.Ship.CargoScu;
        }
        _loaded = true;
    }

    public async Task LoadLocationsAsync()
    {
        try
        {
            var terminals = await gameDataService.GetTerminalsAsync();
            _allLocations = LocationCatalog.Build(terminals);
            Systems.Clear();
            Systems.Add("Все системы");
            foreach (var system in _allLocations.Select(x => x.System).Distinct(StringComparer.OrdinalIgnoreCase))
                Systems.Add(system);
            SelectedSystem = Systems.FirstOrDefault(x => x.Equals(CurrentSystem, StringComparison.OrdinalIgnoreCase)) ?? "Все системы";
            LocationStatus = $"Найдено {_allLocations.Count} локаций. Введи минимум две буквы.";
            RefreshLocationSuggestions();
        }
        catch (Exception ex)
        {
            LocationStatus = $"Не удалось загрузить локации: {ex.Message}";
        }
    }

    public async Task LoadVehiclesAsync()
    {
        try
        {
            _allVehicles = await gameDataService.GetVehiclesAsync();
            VehicleStatus = $"Каталог UEX: {_allVehicles.Count(x => x.IsSpaceship == 1)} кораблей. Выбери модель из списка.";
            RefreshCatalog();
            if (dataCollectionService is not null) _ = SyncDetectedFleetAsync(dataCollectionService.Current.Ships);
        }
        catch (Exception ex)
        {
            VehicleStatus = $"Каталог кораблей недоступен: {ex.Message}";
        }
    }

    public async Task WatchGameLogAsync(CancellationToken token)
    {
        if (dataCollectionService is not null)
        {
            dataCollectionService.SnapshotUpdated += OnDataSnapshotUpdated;
            try { await dataCollectionService.WatchAsync(() => OcrEnabled, token); }
            finally { dataCollectionService.SnapshotUpdated -= OnDataSnapshotUpdated; }
            return;
        }
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (MonitorEnabled) await RefreshGameInfoAsync(token);
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(MonitorIntervalSeconds, 15, 60)), token);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                GameLogStatus = $"Не удалось прочитать журнал: {ex.Message}";
                try { await Task.Delay(TimeSpan.FromSeconds(15), token); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task RefreshGameInfoAsync(CancellationToken token = default)
    {
        var snapshot = await gameLogService.ReadRecentAsync(token);
        ResolvedGameDirectory = snapshot.GameDirectory ?? "Игра не найдена — выбери папку вручную";
        var monitor = await Task.Run(() => GameMonitorService.Inspect(snapshot.GameDirectory), token);
        var health = await Task.Run(() => GameHealthService.Scan(snapshot.GameDirectory), token);
        GameLogStatus = snapshot.GameDirectory is null
            ? "Игра не найдена. Выбери Game.log в инструментах или укажи папку игры в настройках."
            : File.Exists(Path.Combine(snapshot.GameDirectory, "Game.log"))
                ? $"Game.log: {snapshot.GameDirectory} · торговых запросов: {snapshot.Candidates.Count}"
                : $"Игра найдена: {snapshot.GameDirectory} · Game.log появится после запуска игры.";
        if (!GameTrades.SequenceEqual(snapshot.Candidates))
        {
            GameTrades.Clear();
            foreach (var candidate in snapshot.Candidates) GameTrades.Add(candidate);
            ApplyGameTradesToFlight(onlyNew: true);
        }
        GameProcessStatus = monitor.IsRunning ? "Игра запущена" : "Игра не запущена";
        GameRegion = monitor.Region;
        GameShard = monitor.Shard;
        GameLogUpdated = monitor.LogUpdatedAt?.ToString("dd.MM.yyyy HH:mm:ss") ?? "Нет данных";
        if (!GameHealthFindings.SequenceEqual(health))
        {
            GameHealthFindings.Clear();
            foreach (var finding in health) GameHealthFindings.Add(finding);
        }
        var sessions = await GameSessionService.LoadAsync(snapshot.GameDirectory, token);
        if (!GameSessions.SequenceEqual(sessions))
        {
            GameSessions.Clear();
            foreach (var session in sessions) GameSessions.Add(session);
        }
        SessionStatus = sessions.Count == 0 ? "Игровые сессии пока не найдены" : $"Найдено {sessions.Count} журналов";
    }

    [RelayCommand]
    private void UseGameTrade(GameTradeCandidate? candidate)
    {
        if (candidate is null) return;
        if (candidate.IsPurchase) FlightInvestment = candidate.Amount;
        else FlightRevenue = candidate.Amount;
        OpenHistory();
        AutoFillStatus = $"Подставлена сумма {candidate.AmountDisplay} из журнала. Поле можно исправить вручную.";
    }

    partial void OnBalanceChanged(decimal value) { OnPropertyChanged(nameof(BalanceDisplay)); OnPropertyChanged(nameof(HaulingBudgetDisplay)); OnPropertyChanged(nameof(TradeBudgetHint)); QueueSave(); RecalculateHauling(); }
    partial void OnCurrentShipChanged(string value) => QueueSave();
    partial void OnCurrentSystemChanged(string value) { OnPropertyChanged(nameof(LocationDisplay)); OnPropertyChanged(nameof(HaulingStartDisplay)); QueueSave(); RecalculateHauling(); }
    partial void OnCurrentLocationChanged(string value) { OnPropertyChanged(nameof(LocationDisplay)); OnPropertyChanged(nameof(HaulingStartDisplay)); QueueSave(); RecalculateHauling(); }
    partial void OnCargoScuChanged(int value) => QueueSave();
    partial void OnReserveChanged(decimal value) { OnPropertyChanged(nameof(HaulingBudgetDisplay)); OnPropertyChanged(nameof(TradeBudgetHint)); QueueSave(); RecalculateHauling(); }
    partial void OnAllowRiskyChanged(bool value) { QueueSave(); RecalculateHauling(); }
    partial void OnAvoidPyroChanged(bool value) { QueueSave(); RecalculateHauling(); }
    partial void OnMinimumFillPercentChanged(int value) { QueueSave(); RecalculateHauling(); }
    partial void OnMinimumProfitChanged(decimal value) { QueueSave(); RecalculateHauling(); }
    partial void OnGameDirectoryPathChanged(string value) { gameLogService.GameDirectoryOverride = value; QueueSave(); }
    partial void OnSelectedShipChanged(ShipSummary? value)
    {
        OnPropertyChanged(nameof(CanStartFlight));
        if (!_loaded || value is null) return;
        CurrentShip = value.Name;
        CargoScu = value.Ship.CargoScu;
        RecalculateHauling();
    }
    partial void OnShipSortModeChanged(string value) { RefreshSortedShips(); RefreshCatalog(); }
    partial void OnSelectedCatalogVehicleChanged(VehicleCatalogItem? value)
    {
        OnPropertyChanged(nameof(HasSelectedCatalogVehicle));
        if (value is null) { NewShipName = ""; NewShipCargoScu = 0; NewShipRole = ""; return; }
        NewShipName = value.Name;
        NewShipCargoScu = (int)Math.Floor(value.Scu);
        NewShipRole = VehicleCatalog.InferRole(value);
        VehicleStatus = "Вместимость и роль заполнены автоматически.";
    }
    public bool HasSelectedCatalogVehicle => SelectedCatalogVehicle is not null;
    partial void OnSelectedSystemChanged(string value) => RefreshLocationSuggestions();
    partial void OnLocationQueryChanged(string value)
    {
        if (!_selectingLocation) RefreshLocationSuggestions();
    }

    private void RefreshSortedShips()
    {
        var currentId = SelectedShip?.Ship.Id;
        var sorted = ShipSortMode == "По вместимости"
            ? Ships.OrderByDescending(x => x.Ship.CargoScu).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            : Ships.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase);
        SortedShips.Clear();
        foreach (var ship in sorted) SortedShips.Add(ship);
        if (currentId is not null)
            SelectedShip = SortedShips.FirstOrDefault(x => x.Ship.Id == currentId);
    }

    private void RefreshLocationSuggestions()
    {
        FilteredLocations.Clear();
        foreach (var location in LocationCatalog.Search(_allLocations, SelectedSystem, LocationQuery))
            FilteredLocations.Add(location);
        ShowLocationSuggestions = FilteredLocations.Count > 0;
    }

    [RelayCommand]
    private void SelectLocation(LocationOption? location)
    {
        if (location is null) return;
        _selectingLocation = true;
        try
        {
            CurrentLocation = location.Name;
            CurrentSystem = location.System;
            LocationQuery = location.Name;
            SelectedSystem = location.System;
        }
        finally { _selectingLocation = false; }
        FilteredLocations.Clear();
        ShowLocationSuggestions = false;
        LocationStatus = $"Выбрано: {location.Display}";
    }

    [RelayCommand]
    private void ClearLocation()
    {
        _selectingLocation = true;
        try
        {
            CurrentLocation = "Не указана";
            CurrentSystem = "";
            LocationQuery = "";
            SelectedSystem = "Все системы";
        }
        finally { _selectingLocation = false; }
        FilteredLocations.Clear();
        ShowLocationSuggestions = false;
        LocationStatus = "Старт не выбран: поиск покажет маршруты из всех локаций.";
    }
    partial void OnIsSettingsOpenChanged(bool value) => OnPropertyChanged(nameof(IsDashboardOpen));
    partial void OnIsFleetOpenChanged(bool value) => OnPropertyChanged(nameof(IsDashboardOpen));
    partial void OnIsConfiguratorOpenChanged(bool value) => OnPropertyChanged(nameof(IsDashboardOpen));
    partial void OnIsHistoryOpenChanged(bool value) => OnPropertyChanged(nameof(IsDashboardOpen));
    partial void OnIsToolsOpenChanged(bool value) => OnPropertyChanged(nameof(IsDashboardOpen));
    partial void OnIsHaulingOpenChanged(bool value) => OnPropertyChanged(nameof(IsDashboardOpen));
    partial void OnHaulingSortModeChanged(string value) => RecalculateHauling();
    partial void OnHaulingCategoryChanged(string value) => RecalculateHauling();
    partial void OnHaulingSameSystemOnlyChanged(bool value) => RecalculateHauling();
    partial void OnHaulingBestRouteChanged(HaulingRoute? value) => OnPropertyChanged(nameof(HasHaulingBestRoute));
    partial void OnActiveFlightChanged(FlightRecord? value)
    {
        OnPropertyChanged(nameof(HasActiveFlight));
        OnPropertyChanged(nameof(ActiveFlightDisplay));
        OnPropertyChanged(nameof(CanStartFlight));
    }

    private async void QueueSave()
    {
        if (!_loaded) return;
        _pendingSave?.Cancel();
        _pendingSave = new CancellationTokenSource();
        var token = _pendingSave.Token;
        SaveStatus = "Сохранение…";
        try
        {
            await Task.Delay(350, token);
            var snapshot = SettingsSnapshot();
            await settingsService.SaveAsync(snapshot);
            if (!token.IsCancellationRequested) SaveStatus = "Сохранено локально";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { SaveStatus = $"Ошибка сохранения: {ex.Message}"; }
    }

    public async Task SaveNowAsync()
    {
        if (!_loaded) return;
        _pendingSave?.Cancel();
        await settingsService.SaveAsync(SettingsSnapshot());
    }

    private PersonalSettings SettingsSnapshot() => new()
        {
            Balance = Balance,
            CurrentShip = string.IsNullOrWhiteSpace(CurrentShip) ? "Не выбран" : CurrentShip.Trim(),
            CurrentLocation = string.IsNullOrWhiteSpace(CurrentLocation) ? "Не указана" : CurrentLocation.Trim(),
            CurrentSystem = CurrentSystem,
            CargoScu = Math.Max(0, CargoScu),
            Reserve = Math.Max(0, Reserve),
            AllowRisky = AllowRisky,
            AvoidPyro = AvoidPyro,
            MinimumFillPercent = Math.Clamp(MinimumFillPercent, 0, 100),
            MinimumProfit = Math.Max(0, MinimumProfit),
            GameDirectoryPath = GameDirectoryPath.Trim(),
            MonitorEnabled = MonitorEnabled,
            MonitorIntervalSeconds = Math.Clamp(MonitorIntervalSeconds, 15, 60),
            ShowRouteDetails = ShowRouteDetails,
            OcrEnabled = OcrEnabled,
            Language = Language
        };

    [RelayCommand] private void OpenDashboard() => Navigate("Обзор");
    [RelayCommand] private void OpenSettings() => Navigate("Настройки");
    [RelayCommand] private void OpenFleet() => Navigate("Флот");
    [RelayCommand] private void OpenConfigurator() => Navigate("Конфигуратор");
    [RelayCommand] private void OpenHistory() => Navigate("Рейсы");
    [RelayCommand]
    private async Task OpenToolsAsync()
    {
        Navigate("Инструменты");
        await RefreshToolsAsync();
    }

    [RelayCommand]
    private async Task RefreshToolsAsync()
    {
        try
        {
            await RefreshGameInfoAsync();
        }
        catch (Exception ex) { SessionStatus = $"Не удалось проверить игру: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task ChooseGameLogAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выбери Game.log в папке Star Citizen",
            Filter = "Журнал Star Citizen (Game.log)|Game.log",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true) return;
        if (!Path.GetFileName(dialog.FileName).Equals("Game.log", StringComparison.OrdinalIgnoreCase))
        {
            WorkspaceStatus = "Выбери файл Game.log. Другие журналы программа не читает.";
            return;
        }
        GameDirectoryPath = Path.GetDirectoryName(dialog.FileName) ?? "";
        await RefreshToolsAsync();
    }

    [RelayCommand]
    private async Task OpenHaulingAsync()
    {
        Navigate("Маршруты");
        if (_haulingData is null) await LoadMarketAsync(false);
        else RecalculateHauling();
    }

    private void RecalculateHauling()
    {
        InvalidateVoyages();
        if (_haulingData is null) return;
        HaulingRoutes.Clear();
        _allHaulingRoutes = [];
        _visibleHaulingCount = 0;
        OnPropertyChanged(nameof(HasMoreHaulingRoutes));
        OnPropertyChanged(nameof(HaulingCountDisplay));
        HaulingBestRoute = null;
        if (SelectedShip is null)
        {
            HaulingStatus = "Добавь корабль во флот и выбери его для расчёта.";
            return;
        }
        var budget = Math.Max(0, Balance - Reserve);
        _allHaulingRoutes = haulingService.Calculate(_haulingData, SelectedShip.Ship.CargoScu,
            budget, AllowRisky, HaulingSameSystemOnly, HaulingSortMode, HaulingCategory,
            CurrentLocation, CurrentSystem, AvoidPyro, MinimumFillPercent, MinimumProfit, allowedSystems: AllowedRouteSystems);
        ShowMoreHaulingRoutes();
        HaulingBestRoute = _allHaulingRoutes.FirstOrDefault();
        var oldQuote = _allHaulingRoutes.Any(x => DateTimeOffset.UtcNow - x.UpdatedAt > TimeSpan.FromHours(24));
        HaulingStatus = _allHaulingRoutes.Count == 0
            ? "Подходящих рейсов нет. Проверь бюджет, вместимость и фильтры."
            : $"Найдено {_allHaulingRoutes.Count} маршрутов · данные загружены {_haulingData.PricesFetchedAt.LocalDateTime:dd.MM HH:mm}" +
              (_haulingData.UsedOldCache || oldQuote ? " · есть устаревшие котировки" : "");
    }

    [RelayCommand]
    private void ShowMoreHaulingRoutes()
    {
        var next = Math.Min(_allHaulingRoutes.Count, _visibleHaulingCount + 30);
        for (var i = _visibleHaulingCount; i < next; i++) HaulingRoutes.Add(_allHaulingRoutes[i]);
        _visibleHaulingCount = next;
        OnPropertyChanged(nameof(HasMoreHaulingRoutes));
        OnPropertyChanged(nameof(HaulingCountDisplay));
    }

    [RelayCommand]
    private void PrepareHaulingFlight(HaulingRoute? route)
    {
        if (route is null) return;
        if (HasActiveFlight) { OpenHistory(); FlightStatus = "Сначала заверши текущий рейс, затем выбери следующий маршрут."; return; }
        FlightOrigin = route.BuyAt;
        FlightDestination = route.SellAt;
        FlightCommodity = route.Commodity;
        FlightInvestment = route.Investment;
        FlightRevenue = 0; FlightExpenses = 0; FlightLosses = 0;
        OpenHistory();
        FlightStatus = "Грузовой маршрут перенесён в рейс. Проверь цены и наличие в игре.";
    }

    private async Task ReloadFlightLogAsync()
    {
        var (ships, flights) = await flightLogService.LoadAsync();
        var selectedId = SelectedShip?.Ship.Id;
        Ships.Clear();
        foreach (var ship in ships) Ships.Add(ship);
        RefreshSortedShips();
        SelectedShip = Ships.FirstOrDefault(x => x.Ship.Id == selectedId)
            ?? Ships.FirstOrDefault(x => x.Name.Equals(CurrentShip, StringComparison.OrdinalIgnoreCase))
            ?? Ships.FirstOrDefault();
        Flights.Clear();
        foreach (var flight in flights) Flights.Add(flight);
        RefreshFlightStatistics();
        ActiveFlight = Flights.FirstOrDefault(x => x.EndedAtUtc is null);
        if (ActiveFlight is { } active)
        {
            FlightOrigin = active.Origin;
            FlightDestination = active.Destination;
            FlightCommodity = active.Commodity;
            FlightInvestment = active.Investment;
            FlightRevenue = active.Revenue;
            FlightExpenses = active.Expenses;
            FlightLosses = active.Losses;
        }
        OnPropertyChanged(nameof(PersonalProfitHourDisplay));
        OnPropertyChanged(nameof(TotalFlightProfitDisplay));
        OnPropertyChanged(nameof(TodayProfitDisplay));
        NotifyWorkspace();
    }

    [RelayCommand]
    private async Task AddShipAsync()
    {
        try
        {
            if (SelectedCatalogVehicle is null) throw new ArgumentException("Сначала выбери корабль из каталога.");
            var ship = await flightLogService.AddShipAsync(NewShipName, NewShipCargoScu, NewShipRole, NewShipBuild);
            NewShipName = ""; NewShipCargoScu = 0; NewShipRole = ""; NewShipBuild = "";
            SelectedCatalogVehicle = null;
            await ReloadFlightLogAsync();
            SelectedShip = Ships.First(x => x.Ship.Id == ship.Id);
            FlightStatus = "Корабль добавлен.";
        }
        catch (Exception ex) { FlightStatus = ex.Message; }
    }

    [RelayCommand]
    private async Task UseShipAsync(ShipSummary? ship)
    {
        if (ship is null) return;
        SelectedShip = ship;
        CurrentShip = ship.Name;
        CargoScu = ship.Ship.CargoScu;
        FlightStatus = $"{ship.Name} выбран для маршрутов.";
        await SaveNowAsync();
    }

    [RelayCommand]
    private async Task DeleteShipAsync(ShipSummary? ship)
    {
        if (ship is null) return;
        await flightLogService.DeleteShipAsync(ship.Ship.Id);
        if (CurrentShip == ship.Name) { CurrentShip = "Не выбран"; CargoScu = 0; }
        await ReloadFlightLogAsync();
        FlightStatus = "Корабль удалён из флота. История рейсов сохранена.";
    }

    [RelayCommand]
    private void PrepareFlight(TradeRoute? route)
    {
        if (route is null) return;
        FlightOrigin = route.BuyAt;
        FlightDestination = route.SellAt;
        FlightCommodity = route.Commodity;
        FlightInvestment = route.Investment;
        FlightRevenue = 0; FlightExpenses = 0; FlightLosses = 0;
        OpenHistory();
        FlightStatus = "Маршрут перенесён в трекер. Начни рейс, когда отправишься.";
    }

    [RelayCommand]
    private async Task StartFlightAsync()
    {
        try
        {
            var shipName = SelectedShip?.Name ?? CurrentShip;
            if (shipName == "Не выбран") throw new ArgumentException("Добавь или выбери корабль.");
            if (string.IsNullOrWhiteSpace(FlightOrigin) || string.IsNullOrWhiteSpace(FlightDestination))
                throw new ArgumentException("Укажи начало и конец маршрута.");
            await flightLogService.StartFlightAsync(SelectedShip?.Ship.Id, shipName,
                FlightOrigin, FlightDestination, FlightCommodity, FlightInvestment);
            await ReloadFlightLogAsync();
            FlightStatus = "Рейс начат. Время записано автоматически.";
        }
        catch (Exception ex) { FlightStatus = ex.Message; }
    }

    [RelayCommand]
    private async Task FinishFlightAsync()
    {
        if (ActiveFlight is null) return;
        try
        {
            var flight = await flightLogService.FinishFlightAsync(ActiveFlight.Id,
                FlightInvestment, FlightRevenue, FlightExpenses, FlightLosses);
            Balance += flight.Profit;
            await SaveNowAsync();
            await ReloadFlightLogAsync();
            FlightStatus = $"Рейс завершён. Фактическая прибыль: {flight.ProfitDisplay}. Баланс обновлён.";
        }
        catch (Exception ex) { FlightStatus = ex.Message; }
    }
    [RelayCommand]
    private async Task RecommendAsync()
    {
        ShowRecommendation = true;
        Routes.Clear();
        _recommendedRoutes = [];
        _visibleRouteCount = 0;
        OnPropertyChanged(nameof(HasMoreRoutes));
        OnPropertyChanged(nameof(RecommendedCountDisplay));
        if (Balance <= Reserve || CargoScu <= 0)
        {
            RecommendationMessage = "Укажи баланс и выбери корабль с грузовым отсеком в настройках.";
            return;
        }
        IsLoading = true;
        RecommendationMessage = "Загружаю котировки UEX и рассчитываю маршруты…";
        try
        {
            var (data, routes) = await tradingService.RecommendAsync(SettingsSnapshot());
            _recommendedRoutes = routes.Select(route =>
            {
                var similar = Flights.Where(x => x.EndedAtUtc != null &&
                    x.Commodity.Equals(route.Commodity, StringComparison.OrdinalIgnoreCase) &&
                    x.Origin.Equals(route.BuyAt, StringComparison.OrdinalIgnoreCase) &&
                    x.Destination.Equals(route.SellAt, StringComparison.OrdinalIgnoreCase)).ToList();
                var averageMinutes = similar.Count == 0 ? (double?)null : similar.Average(x => x.DurationHours * 60);
                return route with { PersonalDurationMinutes = averageMinutes };
            }).ToArray();
            ShowMoreRoutes();
            var age = DateTimeOffset.UtcNow - data.PricesFetchedAt;
            var oldQuote = routes.Any(x => DateTimeOffset.UtcNow - x.QuoteUpdatedAt > TimeSpan.FromHours(24));
            DataStatus = $"UEX • обновлено {data.PricesFetchedAt.LocalDateTime:dd.MM HH:mm}" +
                (data.UsedOldCache || age > TimeSpan.FromHours(1) || oldQuote ? " • ДАННЫЕ УСТАРЕЛИ" : "");
            RecommendationMessage = routes.Count == 0
                ? "Маршрутов с подходящей ценой и объёмом сейчас не найдено. Проверь фильтры или обнови данные позже."
                : "Расчёт по сообщениям игроков UEX. Наличие товара и спрос проверь в терминале перед закупкой.";
        }
        catch (Exception ex)
        {
            DataStatus = "UEX • данные недоступны";
            RecommendationMessage = ex.Message;
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private void ShowMoreRoutes()
    {
        var next = Math.Min(_recommendedRoutes.Count, _visibleRouteCount + 30);
        for (var i = _visibleRouteCount; i < next; i++) Routes.Add(_recommendedRoutes[i]);
        _visibleRouteCount = next;
        OnPropertyChanged(nameof(HasMoreRoutes));
        OnPropertyChanged(nameof(RecommendedCountDisplay));
    }
}
