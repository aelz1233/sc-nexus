using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCNexus.Models;

namespace SCNexus.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private bool autoFillFlightData = true;
    [ObservableProperty] private string autoFillStatus = "Автозаполнение включено: ожидаю запросы из Game.log.";
    [ObservableProperty] private string flightStatisticsPeriod = "Месяц";
    private DateTimeOffset? _lastAutoPurchase;
    private DateTimeOffset? _lastAutoSale;

    public string[] FlightStatisticsPeriods { get; } = ["Час", "День", "Три дня", "Неделя", "Месяц", "Полгода", "Год"];
    public ObservableCollection<FlightRecord> FilteredFlights { get; } = [];
    public string FlightStatisticsDisplay
    {
        get
        {
            var completed = FilteredFlights.Where(x => x.EndedAtUtc is not null).ToArray();
            var profit = completed.Sum(x => x.Profit);
            var hours = completed.Sum(x => x.DurationHours);
            var hourly = hours <= 0 ? "нет данных по времени" : $"{profit / (decimal)hours:N0} aUEC/ч";
            return completed.Length == 0 ? "За выбранный период завершённых рейсов нет." :
                $"{completed.Length} рейс(ов) · {profit:+#,##0;-#,##0;0} aUEC · {hourly}";
        }
    }

    private DateTimeOffset FlightPeriodStart => DateTimeOffset.Now - FlightStatisticsPeriod switch
    {
        "Час" => TimeSpan.FromHours(1), "День" => TimeSpan.FromDays(1), "Три дня" => TimeSpan.FromDays(3),
        "Неделя" => TimeSpan.FromDays(7), "Месяц" => TimeSpan.FromDays(30), "Полгода" => TimeSpan.FromDays(183),
        "Год" => TimeSpan.FromDays(365), _ => TimeSpan.FromDays(30)
    };

    partial void OnFlightStatisticsPeriodChanged(string value) => RefreshFlightStatistics();
    partial void OnAutoFillFlightDataChanged(bool value) => AutoFillStatus = value
        ? "Автозаполнение включено: новые запросы из Game.log будут подставляться в форму. Поля можно исправить вручную."
        : "Автозаполнение выключено. Используй «Обновить из Game.log» или «Подставить» у запроса.";

    private void RefreshFlightStatistics()
    {
        var start = FlightPeriodStart.UtcDateTime;
        FilteredFlights.Clear();
        foreach (var flight in Flights.Where(x => x.EndedAtUtc is null || x.EndedAtUtc >= start).OrderByDescending(x => x.StartedAtUtc))
            FilteredFlights.Add(flight);
        OnPropertyChanged(nameof(FlightStatisticsDisplay));
    }

    private void ApplyGameTradesToFlight(bool onlyNew)
    {
        if (!AutoFillFlightData && onlyNew) return;
        // Фоновый монитор не должен подменять подготовленный рейс событиями из старых журналов.
        if (onlyNew && ActiveFlight is null) return;
        var trades = ActiveFlight is { } active
            ? GameTrades.Where(x => x.TimeUtc.UtcDateTime >= active.StartedAtUtc)
            : GameTrades;
        var purchase = trades.Where(x => x.IsPurchase).OrderByDescending(x => x.TimeUtc).FirstOrDefault();
        var sale = trades.Where(x => !x.IsPurchase).OrderByDescending(x => x.TimeUtc).FirstOrDefault();
        var changed = false;
        if (purchase is not null && (!onlyNew || purchase.TimeUtc > _lastAutoPurchase)) { FlightInvestment = purchase.Amount; _lastAutoPurchase = purchase.TimeUtc; changed = true; }
        if (sale is not null && (!onlyNew || sale.TimeUtc > _lastAutoSale)) { FlightRevenue = sale.Amount; _lastAutoSale = sale.TimeUtc; changed = true; }
        if (changed)
            AutoFillStatus = $"Суммы обновлены из Game.log: покупка {purchase?.AmountDisplay ?? "—"}, продажа {sale?.AmountDisplay ?? "—"}. Проверь и при необходимости исправь поля.";
        else if (!trades.Any()) AutoFillStatus = ActiveFlight is null
            ? "В Game.log пока нет запросов торговли. Поля можно заполнить вручную."
            : "После начала этого рейса запросов торговли пока нет. Поля можно заполнить вручную.";
    }

    [RelayCommand]
    private async Task RefreshFlightFromGameLogAsync()
    {
        try { await RefreshGameInfoAsync(); ApplyGameTradesToFlight(false); }
        catch (Exception ex) { AutoFillStatus = $"Не удалось обновить данные: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task DeleteFlightFromStatisticsAsync(FlightRecord? flight)
    {
        if (flight is null) return;
        if (flight.EndedAtUtc is null) { FlightStatus = "Активный рейс нельзя удалить из статистики."; return; }
        var answer = System.Windows.MessageBox.Show($"Удалить рейс «{flight.Commodity}» от {flight.DateDisplay} из истории?\n\nЕго прибыль будет вычтена из баланса.",
            "SC NEXUS", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (answer != System.Windows.MessageBoxResult.Yes) return;
        try
        {
            await SaveNowAsync();
            var deleted = await flightLogService.DeleteFinishedFlightAsync(flight.Id, updateBalance: true);
            if (deleted is null) return;
            Balance -= deleted.Profit;
            await ReloadFlightLogAsync();
            FlightStatus = $"Рейс удалён из статистики. Баланс скорректирован на {-deleted.Profit:+#,##0;-#,##0;0} aUEC.";
        }
        catch (Exception ex) { FlightStatus = ex.Message; }
    }
}
