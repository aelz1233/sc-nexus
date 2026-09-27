using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCNexus.Models;
using SCNexus.Services;

namespace SCNexus.ViewModels;

public partial class MainViewModel(SettingsService settingsService) : ObservableObject
{
    private bool _loaded;
    private CancellationTokenSource? _pendingSave;

    [ObservableProperty] private decimal balance;
    [ObservableProperty] private string currentShip = "Не выбран";
    [ObservableProperty] private string currentLocation = "Не указана";
    [ObservableProperty] private bool isSettingsOpen;
    [ObservableProperty] private bool showRecommendation;
    [ObservableProperty] private string saveStatus = "Локальные настройки";

    public string BalanceDisplay => $"{Balance:N0} aUEC";
    public ObservableCollection<DemoRoute> DemoRoutes { get; } =
    [
        new("МАКС. ПРОФИТ", "Gold", "Точка A", "Точка B", "Высокий"),
        new("БЫСТРЫЙ", "Beryl", "Точка C", "Точка D", "Средний"),
        new("ОСТОРОЖНЫЙ", "Medical Supplies", "Точка E", "Точка F", "Низкий")
    ];

    public async Task InitializeAsync()
    {
        var settings = await settingsService.LoadAsync();
        Balance = settings.Balance;
        CurrentShip = settings.CurrentShip;
        CurrentLocation = settings.CurrentLocation;
        _loaded = true;
    }

    partial void OnBalanceChanged(decimal value) { OnPropertyChanged(nameof(BalanceDisplay)); QueueSave(); }
    partial void OnCurrentShipChanged(string value) => QueueSave();
    partial void OnCurrentLocationChanged(string value) => QueueSave();

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
            var snapshot = new PersonalSettings
            {
                Balance = Balance,
                CurrentShip = string.IsNullOrWhiteSpace(CurrentShip) ? "Не выбран" : CurrentShip.Trim(),
                CurrentLocation = string.IsNullOrWhiteSpace(CurrentLocation) ? "Не указана" : CurrentLocation.Trim()
            };
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
        await settingsService.SaveAsync(new PersonalSettings
        {
            Balance = Balance,
            CurrentShip = string.IsNullOrWhiteSpace(CurrentShip) ? "Не выбран" : CurrentShip.Trim(),
            CurrentLocation = string.IsNullOrWhiteSpace(CurrentLocation) ? "Не указана" : CurrentLocation.Trim()
        });
    }

    [RelayCommand] private void OpenDashboard() => IsSettingsOpen = false;
    [RelayCommand] private void OpenSettings() => IsSettingsOpen = true;
    [RelayCommand] private void Recommend() => ShowRecommendation = true;
}
