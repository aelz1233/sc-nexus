using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCNexus.Models;

namespace SCNexus.ViewModels;

public partial class MainViewModel
{
    private bool _sessionStateInitialized;
    private bool _sessionWasRunning;
    private DateTimeOffset? _sessionStartedAt;
    private string _lastSessionSummary = "";
    private DateTimeOffset? _lastSessionEndedAt;
    private readonly Dictionary<string, bool> _knownSourceAvailability = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _notificationKeys = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty] private string sessionInsightTitle = "Последняя игровая сессия";
    [ObservableProperty] private string sessionInsightSummary = "Сессия ещё не зафиксирована.";
    [ObservableProperty] private string sessionInsightDetails = "Nexus сформирует отчёт после запуска и закрытия Star Citizen.";
    [ObservableProperty] private string sessionInsightProfit = "0 aUEC";

    public ObservableCollection<NexusNotification> Notifications { get; } = [];
    public bool HasNotifications => Notifications.Count > 0;
    public string NotificationsEmptyText => HasNotifications ? "" :
        (IsEnglish ? "No new notifications." : "Новых уведомлений нет.");
    public string LastSessionSummaryForStorage => _lastSessionSummary;
    public DateTimeOffset? LastSessionEndedAtForStorage => _lastSessionEndedAt;
    public event EventHandler<NexusNotification>? NotificationRaised;

    internal void RestoreLastSessionSummary(string summary, DateTimeOffset? endedAt)
    {
        _lastSessionSummary = summary ?? "";
        _lastSessionEndedAt = endedAt;
        if (string.IsNullOrWhiteSpace(summary)) return;
        SessionInsightTitle = IsEnglish ? "Last game session" : "Последняя игровая сессия";
        SessionInsightSummary = summary;
        SessionInsightDetails = endedAt is null ? "" : (IsEnglish
            ? $"Finished {endedAt.Value.LocalDateTime:g}"
            : $"Завершена {endedAt.Value.LocalDateTime:g}");
    }

    internal void UpdateSessionInsights(DataCollectionSnapshot snapshot, bool running)
    {
        if (!_sessionStateInitialized)
        {
            _sessionStateInitialized = true;
            _sessionWasRunning = running;
            if (running) StartTrackedSession(snapshot, notify: false);
        }
        else if (running && !_sessionWasRunning)
        {
            StartTrackedSession(snapshot, notify: true);
        }

        if (running)
        {
            _sessionStartedAt ??= FindSessionStart(snapshot);
            if (snapshot.Values.ContainsKey("game.process.started")) _sessionStartedAt = FindSessionStart(snapshot);
            ApplySessionReport(snapshot, _sessionStartedAt.Value, DateTimeOffset.UtcNow, active: true);
        }
        else if (_sessionWasRunning && _sessionStartedAt is { } started)
        {
            var ended = DateTimeOffset.UtcNow;
            ApplySessionReport(snapshot, started, ended, active: false);
            _lastSessionSummary = SessionInsightSummary;
            _lastSessionEndedAt = ended;
            RaiseNotification($"session-ended:{ended:O}",
                IsEnglish ? "Session report is ready" : "Отчёт по сессии готов",
                SessionInsightSummary, NexusNotificationKind.Success);
            _sessionStartedAt = null;
            QueueSave();
        }
        _sessionWasRunning = running;
        UpdateProviderNotifications(snapshot.Sources);
    }

    private void StartTrackedSession(DataCollectionSnapshot snapshot, bool notify)
    {
        _sessionStartedAt = FindSessionStart(snapshot);
        if (notify)
            RaiseNotification($"session-started:{_sessionStartedAt:O}",
                IsEnglish ? "Star Citizen started" : "Star Citizen запущен",
                IsEnglish ? "Live monitoring is active." : "Мониторинг игровых данных активен.",
                NexusNotificationKind.Info);
    }

    private static DateTimeOffset FindSessionStart(DataCollectionSnapshot snapshot)
    {
        DateTimeOffset? processStart = snapshot.Values.TryGetValue("game.process.started", out var process) &&
            DateTimeOffset.TryParse(process.Value, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var parsed) && parsed <= DateTimeOffset.UtcNow
            ? parsed : null;
        if (processStart is null && snapshot.Values.TryGetValue("game.running", out var running) &&
            running.Source == DataSourceKind.LocalGameData && running.Value.Equals("true", StringComparison.OrdinalIgnoreCase))
            processStart = running.Timestamp;
        var candidate = snapshot.Sessions.Where(x => x.EndedAt is null && x.StartedAt.Value <= DateTimeOffset.UtcNow &&
            (processStart is null || x.StartedAt.Value >= processStart)).OrderByDescending(x => x.StartedAt.Value).FirstOrDefault()?.StartedAt.Value;
        if (processStart is not null) return candidate ?? processStart.Value;
        if (candidate is null || candidate > DateTimeOffset.UtcNow ||
            DateTimeOffset.UtcNow - candidate > TimeSpan.FromDays(2)) return DateTimeOffset.UtcNow;
        return candidate.Value;
    }

    private void ApplySessionReport(DataCollectionSnapshot snapshot, DateTimeOffset started,
        DateTimeOffset ended, bool active)
    {
        var trades = snapshot.Trades.Where(x => x.Amount.Timestamp >= started && x.Amount.Timestamp <= ended).ToArray();
        var purchases = trades.Where(x => x.Action.Value.Equals("purchase", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Amount.Value);
        var sales = trades.Where(x => x.Action.Value.Equals("sale", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Amount.Value);
        var flights = Flights.Where(x => x.EndedAtUtc is { } flightEnd &&
            flightEnd >= started.UtcDateTime && flightEnd <= ended.UtcDateTime).ToArray();
        var flightProfit = flights.Sum(x => x.Profit);
        var completedMissions = snapshot.Missions.Count(x => x.Status.Timestamp >= started &&
            x.Status.Value.Equals("completed", StringComparison.OrdinalIgnoreCase));
        var deaths = snapshot.Deaths.Count(x => x.Description.Timestamp >= started);
        var movements = snapshot.Movements.Count(x => x.Location.Timestamp >= started);
        var duration = ended - started;
        var durationText = IsEnglish
            ? duration.TotalHours >= 1
                ? $"{(int)duration.TotalHours} h {duration.Minutes} min"
                : $"{Math.Max(1, duration.Minutes)} min"
            : duration.TotalHours >= 1
                ? $"{(int)duration.TotalHours} ч {duration.Minutes} мин"
                : $"{Math.Max(1, duration.Minutes)} мин";
        var result = (flightProfit != 0 ? flightProfit : sales - purchases) + CountedEarnings.Where(x => x.CompletedAt >= started && x.CompletedAt <= ended).Sum(x => x.Amount);

        SessionInsightTitle = active
            ? (IsEnglish ? "Current game session" : "Текущая игровая сессия")
            : (IsEnglish ? "Last game session" : "Последняя игровая сессия");
        SessionInsightProfit = $"{result:+#,##0;-#,##0;0} aUEC";
        SessionInsightSummary = IsEnglish
            ? $"{durationText} · {flights.Length} trips · {completedMissions} missions · {deaths} deaths"
            : $"{durationText} · рейсов: {flights.Length} · миссий: {completedMissions} · смертей: {deaths}";
        SessionInsightDetails = IsEnglish
            ? $"Purchases {purchases:N0} · sales {sales:N0} · movements {movements}"
            : $"Покупки {purchases:N0} · продажи {sales:N0} · перемещения {movements}";
    }

    private void UpdateProviderNotifications(IEnumerable<DataSourceInfo> sources)
    {
        foreach (var source in sources)
        {
            if (source.Status is "Paused" or "Disabled" or "Updating" || source.Status.StartsWith("Waiting", StringComparison.Ordinal)) continue;
            if (!_knownSourceAvailability.TryGetValue(source.Name, out var previous))
            {
                _knownSourceAvailability[source.Name] = source.IsAvailable;
                continue;
            }
            if (previous == source.IsAvailable) continue;
            _knownSourceAvailability[source.Name] = source.IsAvailable;
            RaiseNotification($"source:{source.Name}:{source.IsAvailable}",
                source.IsAvailable
                    ? (IsEnglish ? "Data source restored" : "Источник данных восстановлен")
                    : (IsEnglish ? "Data source unavailable" : "Источник данных недоступен"),
                $"{source.NameDisplay}: {source.StatusDisplay}",
                source.IsAvailable ? NexusNotificationKind.Success : NexusNotificationKind.Warning,
                TimeSpan.FromMinutes(10));
        }
    }

    internal void RaiseNotification(string key, string title, string message,
        NexusNotificationKind kind = NexusNotificationKind.Info, TimeSpan? deduplicationWindow = null)
    {
        var now = DateTimeOffset.UtcNow;
        if (_notificationKeys.TryGetValue(key, out var previous) &&
            now - previous < (deduplicationWindow ?? TimeSpan.FromSeconds(5))) return;
        _notificationKeys[key] = now;
        var notification = new NexusNotification(key, title, message, kind, now);
        Notifications.Insert(0, notification);
        while (Notifications.Count > 8) Notifications.RemoveAt(Notifications.Count - 1);
        OnPropertyChanged(nameof(HasNotifications));
        OnPropertyChanged(nameof(NotificationsEmptyText));
        NotificationRaised?.Invoke(this, notification);
    }

    [RelayCommand]
    private void ClearNotifications()
    {
        Notifications.Clear();
        OnPropertyChanged(nameof(HasNotifications));
        OnPropertyChanged(nameof(NotificationsEmptyText));
    }
}
