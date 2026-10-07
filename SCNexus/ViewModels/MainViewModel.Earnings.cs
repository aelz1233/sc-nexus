using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCNexus.Models;
using SCNexus.Services;

namespace SCNexus.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private bool contractEarningsEnabled = true;
    [ObservableProperty] private MissionState? selectedEarningMission;
    [ObservableProperty] private decimal contractRewardAmount;
    [ObservableProperty] private double contractDurationMinutes;
    [ObservableProperty] private string contractEarningStatus = "";
    public ObservableCollection<ContractEarning> ContractEarnings { get; } = [];
    public IEnumerable<MissionState> CompletedEarningMissions => _lastDataSnapshot.Missions
        .Where(x => x.Status.Value.Equals("completed", StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(x => x.Status.Timestamp);
    public IEnumerable<ContractEarning> VisibleContractEarnings => ContractEarnings
        .Where(x => !x.Excluded && x.CompletedAt >= FlightPeriodStart).OrderByDescending(x => x.CompletedAt);
    private IEnumerable<ContractEarning> CountedEarnings => ContractEarningsEnabled ? ContractEarnings.Where(x => !x.Excluded) : [];
    public string ContractEarningsLabel => IsEnglish ? "Include contract earnings in analytics" : "Учитывать заработок с контрактов в аналитике";
    public string ContractEarningsLimitations => IsEnglish
        ? "Automatic reward detection is not available for every contract. SC Wiki may have no amount or an estimate for another game version. Verify the actual payout before saving. Turn this option off to exclude contract earnings from totals and income per hour; saved records are kept and your wallet balance is unchanged."
        : "Автоматическое определение награды доступно не для всех контрактов. В SC Wiki сумма может отсутствовать или относиться к другой версии игры. Перед сохранением проверь фактическую выплату. Выключи учёт, чтобы исключить контракты из общего заработка и дохода в час. Сохранённые записи останутся, баланс не изменится.";
    public string TotalIncomeLabel => IsEnglish ? "Total income" : "Общий заработок";
    public string RecordTripLabel => IsEnglish ? "Record a trading trip" : "Записать торговый рейс";
    public string ContractEarningsTitle => IsEnglish ? "Contract payouts" : "Выплаты по контрактам";
    public string ContractEarningsHelp => IsEnglish
        ? "Log payout notifications are recorded automatically. If a payout is missing, select a completed contract and confirm its actual amount. Enter duration in minutes for hourly income (0 = unknown). Saving again corrects the existing entry. Balance is updated by OCR; this record only affects analytics."
        : "Уведомления о начислении из журнала учитываются автоматически. Если выплаты нет, выбери завершённый контракт и подтверди фактическую сумму. Укажи время в минутах для дохода в час (0 — неизвестно). Повторное сохранение исправляет запись. Баланс обновляется через OCR; эта запись влияет только на аналитику.";
    public string ContractAmountLabel => IsEnglish ? "Actual payout, aUEC" : "Фактическая выплата, aUEC";
    public string ContractMinutesLabel => IsEnglish ? "Duration, minutes" : "Время, минуты";
    public string SaveContractLabel => IsEnglish ? "Save payout" : "Сохранить выплату";
    public string DeleteContractLabel => IsEnglish ? "Remove payout" : "Удалить выплату";
    public string UseRewardLabel => IsEnglish ? "Use visible reward (verify contract)" : "Подставить награду с экрана (проверь контракт)";
    public string LookupRewardLabel => IsEnglish ? "Find reward by contract ID" : "Найти награду по ID контракта";
    [RelayCommand]
    private async Task LookupContractRewardAsync()
    {
        var mission = SelectedEarningMission;
        if (!Guid.TryParse(mission?.DefinitionId, out var id) || id == Guid.Empty)
        {
            ContractEarningStatus = IsEnglish ? "The log has no definition ID for this contract. Open its reward in the game." : "В журнале нет ID типа этого контракта. Открой его награду в игре.";
            return;
        }
        ContractEarningStatus = IsEnglish ? "Searching SC Wiki…" : "Поиск в SC Wiki…";
        try
        {
            var reward = await SCWikiProvider.GetMissionRewardAsync(id.ToString());
            if (SelectedEarningMission?.Id != mission!.Id) return;
            var source = $"SC Wiki · {reward.Version}";
            if (reward.Minimum is null || reward.Maximum is null)
                ContractEarningStatus = IsEnglish ? $"{source}: monetary reward unavailable. Use the in-game reward." : $"{source}: денежная награда отсутствует. Используй сумму с экрана игры.";
            else if (reward.Minimum != reward.Maximum)
                ContractEarningStatus = IsEnglish ? $"{source}: expected {reward.Minimum:N0}–{reward.Maximum:N0} aUEC. Confirm the actual payout." : $"{source}: ожидается {reward.Minimum:N0}–{reward.Maximum:N0} aUEC. Подтверди фактическую выплату.";
            else
            {
                ContractRewardAmount = reward.Minimum.Value;
                ContractEarningStatus = IsEnglish ? $"{source}: expected reward filled. Verify game version and actual payout before saving." : $"{source}: ожидаемая награда подставлена. Проверь версию игры и фактическую выплату перед сохранением.";
            }
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException)
        {
            if (SelectedEarningMission?.Id != mission!.Id) return;
            ContractEarningStatus = IsEnglish ? "Reward lookup unavailable. Retry or use the in-game reward." : "Поиск награды недоступен. Повтори или используй сумму из игры.";
            AppLogService.Write("Mission reward lookup", ex);
        }
    }
    [RelayCommand]
    private void UseVisibleContractReward()
    {
        if (_lastDataSnapshot.Values.TryGetValue("mission.offeredReward", out var value) &&
            value.Source == DataSourceKind.Ocr && DateTimeOffset.UtcNow - value.Timestamp < TimeSpan.FromMinutes(2) &&
            decimal.TryParse(value.Value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var amount))
        {
            ContractRewardAmount = amount;
            ContractEarningStatus = IsEnglish ? "Check the selected contract and actual payout before saving." : "Перед сохранением проверь выбранный контракт и фактическую выплату.";
        }
        else ContractEarningStatus = IsEnglish ? "Open the contract reward in the game with OCR enabled." : "Открой награду контракта в игре при включённом OCR.";
    }
    partial void OnSelectedEarningMissionChanged(MissionState? value)
    {
        var saved = ContractEarnings.FirstOrDefault(x => x.MissionId == value?.Id);
        ContractRewardAmount = saved?.Amount ?? 0;
        ContractDurationMinutes = saved?.Minutes ?? (value?.AcceptedAt is { } start && value.Status.Timestamp > start ? Math.Round((value.Status.Timestamp - start).TotalMinutes, 1) : 0);
    }
    partial void OnContractEarningsEnabledChanged(bool value) { NotifyEarnings(); QueueSave(); }
    [RelayCommand]
    private void SaveContractEarning()
    {
        if (SelectedEarningMission is not { } mission || mission.Status.Value != "completed" || ContractRewardAmount <= 0 ||
            !double.IsFinite(ContractDurationMinutes) || ContractDurationMinutes < 0 || ContractDurationMinutes > 525600)
        {
            ContractEarningStatus = IsEnglish ? "Select a completed contract, a positive payout and valid duration." : "Выбери завершённый контракт, положительную выплату и корректное время.";
            return;
        }
        if (ContractEarnings.Any(x => x.AutoDetected && x.RelatedMissionIds?.Contains(mission.Id) == true))
        {
            ContractEarningStatus = IsEnglish ? "A log payout already covers this contract. It will not be counted twice."
                : "Выплата из журнала уже относится к этому завершению. Повторное начисление отключено.";
            return;
        }
        var existing = ContractEarnings.FirstOrDefault(x => x.MissionId == mission.Id);
        if (existing is not null) ContractEarnings.Remove(existing);
        ContractEarnings.Add(new(mission.Id, mission.DisplayName, ContractRewardAmount, mission.Status.Timestamp, ContractDurationMinutes));
        ContractEarningStatus = IsEnglish ? "Payout recorded in analytics." : "Выплата записана в аналитику.";
        NotifyEarnings(); QueueSave();
    }
    [RelayCommand]
    private void DeleteContractEarning(ContractEarning? earning)
    {
        if (earning is null) return;
        ContractEarnings.Remove(earning);
        if (earning.AutoDetected) ContractEarnings.Add(earning with { Excluded = true });
        NotifyEarnings(); QueueSave();
    }
    private void ImportContractEarnings(DataCollectionSnapshot snapshot)
    {
        var changed = false;
        foreach (var earning in snapshot.ContractEarnings)
        {
            if (ContractEarnings.Any(x => x.MissionId == earning.MissionId)) continue;
            // A confirmed manual entry must not be counted again when a delayed log arrives.
            var manual = ContractEarnings.Where(x => !x.AutoDetected && earning.RelatedMissionIds?.Contains(x.MissionId) == true).ToArray();
            foreach (var item in manual) ContractEarnings.Remove(item);
            ContractEarnings.Add(earning);
            changed = true;
        }
        if (changed) { NotifyEarnings(); QueueSave(); }
    }
    private void LoadContractEarnings(string json)
    {
        try
        {
            foreach (var item in (JsonSerializer.Deserialize<ContractEarning[]>(json) ?? []).DistinctBy(x => x.MissionId))
                if (item.Amount > 0 && double.IsFinite(item.Minutes) && item.Minutes >= 0) ContractEarnings.Add(item);
        }
        catch (JsonException) { ContractEarningStatus = IsEnglish ? "Could not read saved payouts." : "Не удалось прочитать сохранённые выплаты."; }
    }
    private void NotifyEarnings()
    {
        OnPropertyChanged(nameof(VisibleContractEarnings));
        OnPropertyChanged(nameof(TodayProfitDisplay));
        OnPropertyChanged(nameof(TotalFlightProfitDisplay));
        OnPropertyChanged(nameof(PersonalProfitHourDisplay));
        OnPropertyChanged(nameof(FlightStatisticsDisplay));
    }
    internal static double EarningHours(IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> intervals)
    {
        DateTimeOffset? end = null;
        double hours = 0;
        foreach (var interval in intervals.Where(x => x.End > x.Start).OrderBy(x => x.Start))
        {
            var start = end is { } previous && previous > interval.Start ? previous : interval.Start;
            if (interval.End > start) hours += (interval.End - start).TotalHours;
            if (end is null || interval.End > end) end = interval.End;
        }
        return hours;
    }
    private string EarningsSummary(IEnumerable<FlightRecord> flights, IEnumerable<ContractEarning> earnings, bool hourlyOnly = false)
    {
        var trips = flights.Where(x => x.EndedAtUtc is not null).ToArray();
        var contracts = earnings.ToArray();
        var profit = trips.Sum(x => x.Profit) + contracts.Sum(x => x.Amount);
        var hours = EarningHours(trips.Select(x => (new DateTimeOffset(DateTime.SpecifyKind(x.StartedAtUtc, DateTimeKind.Utc)), new DateTimeOffset(DateTime.SpecifyKind(x.EndedAtUtc!.Value, DateTimeKind.Utc))))
            .Concat(contracts.Where(x => x.Minutes > 0).Select(x => (x.CompletedAt.AddMinutes(-x.Minutes), x.CompletedAt))));
        var rate = hours <= 0 || contracts.Any(x => x.Minutes <= 0) ? (IsEnglish ? "Duration missing" : "Не указано время")
            : $"{profit / (decimal)hours:N0} aUEC/{(IsEnglish ? "h" : "ч")}";
        return hourlyOnly ? rate : $"{trips.Length} {(IsEnglish ? "trips" : "рейсов")} · {contracts.Length} {(IsEnglish ? "contracts" : "контрактов")} · {profit:+#,##0;-#,##0;0} aUEC · {rate}";
    }
}
