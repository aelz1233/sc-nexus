using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCNexus.Models;
using SCNexus.Services;

namespace SCNexus.ViewModels;

public partial class MainViewModel(SettingsService settingsService, TradingService tradingService, FlightLogService flightLogService) : ObservableObject
{
    private bool _loaded;
    private CancellationTokenSource? _pendingSave;

    [ObservableProperty] private decimal balance;
    [ObservableProperty] private string currentShip = "Не выбран";
    [ObservableProperty] private string currentLocation = "Не указана";
    [ObservableProperty] private int cargoScu;
    [ObservableProperty] private decimal reserve;
    [ObservableProperty] private bool allowRisky;
    [ObservableProperty] private bool isSettingsOpen;
    [ObservableProperty] private bool isFleetOpen;
    [ObservableProperty] private bool isHistoryOpen;
    [ObservableProperty] private bool showRecommendation;
    [ObservableProperty] private string recommendationMessage = "";
    [ObservableProperty] private string dataStatus = "UEX • ещё не загружено";
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string saveStatus = "Локальные настройки";
    [ObservableProperty] private string flightStatus = "";
    [ObservableProperty] private string newShipName = "";
    [ObservableProperty] private int newShipCargoScu;
    [ObservableProperty] private string newShipRole = "Торговля";
    [ObservableProperty] private string newShipBuild = "";
    [ObservableProperty] private ShipSummary? selectedShip;
    [ObservableProperty] private string flightOrigin = "";
    [ObservableProperty] private string flightDestination = "";
    [ObservableProperty] private string flightCommodity = "";
    [ObservableProperty] private decimal flightInvestment;
    [ObservableProperty] private decimal flightRevenue;
    [ObservableProperty] private decimal flightExpenses;
    [ObservableProperty] private decimal flightLosses;
    [ObservableProperty] private FlightRecord? activeFlight;

    public string BalanceDisplay => $"{Balance:N0} aUEC";
    public ObservableCollection<TradeRoute> Routes { get; } = [];
    public ObservableCollection<ShipSummary> Ships { get; } = [];
    public ObservableCollection<FlightRecord> Flights { get; } = [];
    public bool IsDashboardOpen => !IsSettingsOpen && !IsFleetOpen && !IsHistoryOpen;
    public bool HasActiveFlight => ActiveFlight is not null;
    public string ActiveFlightDisplay => ActiveFlight is null ? "Нет активного рейса" :
        $"{ActiveFlight.Commodity} • {ActiveFlight.Origin} → {ActiveFlight.Destination}";
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

    public async Task InitializeAsync()
    {
        var settings = await settingsService.LoadAsync();
        Balance = settings.Balance;
        CurrentShip = settings.CurrentShip;
        CurrentLocation = settings.CurrentLocation;
        CargoScu = settings.CargoScu;
        Reserve = settings.Reserve;
        AllowRisky = settings.AllowRisky;
        await ReloadFlightLogAsync();
        _loaded = true;
    }

    partial void OnBalanceChanged(decimal value) { OnPropertyChanged(nameof(BalanceDisplay)); QueueSave(); }
    partial void OnCurrentShipChanged(string value) => QueueSave();
    partial void OnCurrentLocationChanged(string value) => QueueSave();
    partial void OnCargoScuChanged(int value) => QueueSave();
    partial void OnReserveChanged(decimal value) => QueueSave();
    partial void OnAllowRiskyChanged(bool value) => QueueSave();
    partial void OnIsSettingsOpenChanged(bool value) => OnPropertyChanged(nameof(IsDashboardOpen));
    partial void OnIsFleetOpenChanged(bool value) => OnPropertyChanged(nameof(IsDashboardOpen));
    partial void OnIsHistoryOpenChanged(bool value) => OnPropertyChanged(nameof(IsDashboardOpen));
    partial void OnActiveFlightChanged(FlightRecord? value)
    {
        OnPropertyChanged(nameof(HasActiveFlight));
        OnPropertyChanged(nameof(ActiveFlightDisplay));
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
            CargoScu = Math.Max(0, CargoScu),
            Reserve = Math.Max(0, Reserve),
            AllowRisky = AllowRisky
        };

    [RelayCommand] private void OpenDashboard() { IsSettingsOpen = false; IsFleetOpen = false; IsHistoryOpen = false; }
    [RelayCommand] private void OpenSettings() { IsFleetOpen = false; IsHistoryOpen = false; IsSettingsOpen = true; }
    [RelayCommand] private void OpenFleet() { IsSettingsOpen = false; IsHistoryOpen = false; IsFleetOpen = true; }
    [RelayCommand] private void OpenHistory() { IsSettingsOpen = false; IsFleetOpen = false; IsHistoryOpen = true; }

    private async Task ReloadFlightLogAsync()
    {
        var (ships, flights) = await flightLogService.LoadAsync();
        var selectedId = SelectedShip?.Ship.Id;
        Ships.Clear();
        foreach (var ship in ships) Ships.Add(ship);
        SelectedShip = Ships.FirstOrDefault(x => x.Ship.Id == selectedId)
            ?? Ships.FirstOrDefault(x => x.Name.Equals(CurrentShip, StringComparison.OrdinalIgnoreCase))
            ?? Ships.FirstOrDefault();
        Flights.Clear();
        foreach (var flight in flights) Flights.Add(flight);
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
    }

    [RelayCommand]
    private async Task AddShipAsync()
    {
        try
        {
            var ship = await flightLogService.AddShipAsync(NewShipName, NewShipCargoScu, NewShipRole, NewShipBuild);
            NewShipName = ""; NewShipCargoScu = 0; NewShipBuild = "";
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
        if (Balance <= Reserve || CargoScu <= 0 || CurrentLocation == "Не указана")
        {
            RecommendationMessage = "Укажи баланс, резерв, объём груза и текущую локацию в настройках.";
            return;
        }
        IsLoading = true;
        RecommendationMessage = "Загружаю котировки UEX и рассчитываю маршруты…";
        try
        {
            var (data, routes) = await tradingService.RecommendAsync(SettingsSnapshot());
            foreach (var route in routes)
            {
                var similar = Flights.Where(x => x.EndedAtUtc != null &&
                    x.Commodity.Equals(route.Commodity, StringComparison.OrdinalIgnoreCase) &&
                    x.Origin.Equals(route.BuyAt, StringComparison.OrdinalIgnoreCase) &&
                    x.Destination.Equals(route.SellAt, StringComparison.OrdinalIgnoreCase)).ToList();
                var averageMinutes = similar.Count == 0 ? (double?)null : similar.Average(x => x.DurationHours * 60);
                Routes.Add(route with { PersonalDurationMinutes = averageMinutes });
            }
            var age = DateTimeOffset.UtcNow - data.PricesFetchedAt;
            var oldQuote = routes.Any(x => DateTimeOffset.UtcNow - x.QuoteUpdatedAt > TimeSpan.FromHours(24));
            DataStatus = $"UEX • данные получены {data.PricesFetchedAt.LocalDateTime:dd.MM HH:mm}" +
                (data.UsedOldCache || age > TimeSpan.FromHours(1) || oldQuote ? " • DATA OLD" : "");
            RecommendationMessage = routes.Count == 0
                ? "Маршрутов с подтверждённой ценой и указанным объёмом сейчас не найдено. Проверь локацию или попробуй обновить позже."
                : "Расчёт по сообщениям игроков UEX. Наличие товара и спрос проверь в терминале перед закупкой.";
        }
        catch (Exception ex)
        {
            DataStatus = "UEX • данные недоступны";
            RecommendationMessage = ex.Message;
        }
        finally { IsLoading = false; }
    }
}
