using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCNexus.Models;
using SCNexus.Services;

namespace SCNexus.ViewModels;

public partial class MainViewModel(SettingsService settingsService, TradingService tradingService) : ObservableObject
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
    [ObservableProperty] private bool showRecommendation;
    [ObservableProperty] private string recommendationMessage = "";
    [ObservableProperty] private string dataStatus = "UEX • ещё не загружено";
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string saveStatus = "Локальные настройки";

    public string BalanceDisplay => $"{Balance:N0} aUEC";
    public ObservableCollection<TradeRoute> Routes { get; } = [];

    public async Task InitializeAsync()
    {
        var settings = await settingsService.LoadAsync();
        Balance = settings.Balance;
        CurrentShip = settings.CurrentShip;
        CurrentLocation = settings.CurrentLocation;
        CargoScu = settings.CargoScu;
        Reserve = settings.Reserve;
        AllowRisky = settings.AllowRisky;
        _loaded = true;
    }

    partial void OnBalanceChanged(decimal value) { OnPropertyChanged(nameof(BalanceDisplay)); QueueSave(); }
    partial void OnCurrentShipChanged(string value) => QueueSave();
    partial void OnCurrentLocationChanged(string value) => QueueSave();
    partial void OnCargoScuChanged(int value) => QueueSave();
    partial void OnReserveChanged(decimal value) => QueueSave();
    partial void OnAllowRiskyChanged(bool value) => QueueSave();

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

    [RelayCommand] private void OpenDashboard() => IsSettingsOpen = false;
    [RelayCommand] private void OpenSettings() => IsSettingsOpen = true;
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
            foreach (var route in routes) Routes.Add(route);
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
