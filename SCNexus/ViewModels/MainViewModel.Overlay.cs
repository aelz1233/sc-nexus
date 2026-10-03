using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCNexus.Services;

namespace SCNexus.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private bool overlayEnabled = true;
    [ObservableProperty] private bool overlayExpanded;
    [ObservableProperty] private double overlayOpacity = 0.92;
    [ObservableProperty] private double overlayTextOpacity = 1;
    [ObservableProperty] private double overlayScale = 1;
    [ObservableProperty] private string overlayAnchor = "BottomRight";
    [ObservableProperty] private double overlayCustomLeft = -1;
    [ObservableProperty] private double overlayCustomTop = -1;
    [ObservableProperty] private bool overlayShowShip = true;
    [ObservableProperty] private bool overlayShowLocation = true;
    [ObservableProperty] private bool overlayShowRoute = true;
    [ObservableProperty] private bool overlayShowMission = true;
    [ObservableProperty] private bool overlayShowFreshness = true;
    [ObservableProperty] private string overlayHotkey = "";
    [ObservableProperty] private bool isOverlayHotkeyCapturing;
    [ObservableProperty] private bool overlayPreview;
    [ObservableProperty] private bool overlayEditMode;
    [ObservableProperty] private bool isGameRunning;
    private bool _overlayHotkeyVisible;
    private bool _overlaySuppressed;
    private string _overlayHotkeyRegistrationState = "none";

    public string OverlayHotkeyCaptureText => IsOverlayHotkeyCapturing
        ? (IsEnglish ? "Press a shortcut…" : "Нажми сочетание…")
        : string.IsNullOrWhiteSpace(OverlayHotkey)
            ? (IsEnglish ? "Click to assign" : "Нажми, чтобы назначить")
            : OverlayHotkey;
    public string OverlayOpacityDisplay => $"{Math.Round(OverlayOpacity * 100):N0}%";
    public string OverlayTextOpacityDisplay => $"{Math.Round(OverlayTextOpacity * 100):N0}%";
    public string OverlayScaleDisplay => $"{Math.Round(OverlayScale * 100):N0}%";
    public IReadOnlyList<OverlayAnchorOption> OverlayAnchorOptions =>
    [
        new("TopLeft", IsEnglish ? "Top left" : "Слева сверху"),
        new("TopRight", IsEnglish ? "Top right" : "Справа сверху"),
        new("BottomLeft", IsEnglish ? "Bottom left" : "Слева снизу"),
        new("BottomRight", IsEnglish ? "Bottom right" : "Справа снизу"),
        new("Custom", IsEnglish ? "Custom position" : "Своя позиция")
    ];
    public string OverlayHotkeyStatus => _overlayHotkeyRegistrationState switch
    {
        "active" => IsEnglish
            ? $"{OverlayHotkey} is active. Press it to show or hide the overlay."
            : $"{OverlayHotkey} активен. Нажми его, чтобы показать или скрыть оверлей.",
        "busy" => IsEnglish
            ? $"{OverlayHotkey} is already used by another app. Choose another shortcut."
            : $"{OverlayHotkey} уже занят другой программой. Выбери другое сочетание.",
        "invalid" => IsEnglish
            ? "This shortcut could not be registered. Choose another one."
            : "Не удалось зарегистрировать этот бинд. Выбери другой.",
        _ => IsEnglish
            ? "No shortcut is assigned. The overlay can still appear automatically with the game."
            : "Бинд не назначен. Оверлей всё равно может появляться автоматически вместе с игрой."
    };
    internal bool OverlayHotkeyVisible => _overlayHotkeyVisible;
    internal bool OverlaySuppressed => _overlaySuppressed;

    public string OverlayModeDisplay => OverlayExpanded
        ? (IsEnglish ? "Expanded" : "Расширенный")
        : (IsEnglish ? "Compact" : "Компактный");
    public string OverlayModeButtonText => OverlayExpanded
        ? (IsEnglish ? "Compact view" : "Компактный вид") : (IsEnglish ? "More details" : "Подробнее");
    public string OverlayHideButtonText => IsEnglish ? "Hide" : "Скрыть";
    public string OverlayControlHint => OverlayEditMode
        ? (IsEnglish ? "Drag the panel · finish positioning in Settings" : "Перетащи панель · заверши размещение в настройках")
        : string.IsNullOrWhiteSpace(OverlayHotkey) ? (IsEnglish ? "Shortcut not assigned" : "Бинд не назначен")
        : $"{OverlayHotkey} · {(IsEnglish ? "show / hide" : "показать / скрыть")}";
    public string OverlayCompatibilityHint => IsEnglish
        ? "Use Borderless or Windowed mode in Star Citizen. A shortcut also checks the assigned keys while the game is active. If it works only outside the game, check that the game and Nexus run with the same Windows privileges."
        : "В Star Citizen выбери оконный режим или окно без рамки. При активной игре Nexus дополнительно проверяет назначенные клавиши. Если бинд работает только вне игры, проверь, что игра и Nexus запущены с одинаковыми правами Windows.";
    public string OverlayBalanceDisplay => IsEnglish ? $"Nexus balance: {BalanceDisplay}" : $"Баланс Nexus: {BalanceDisplay}";
    public bool OverlayHasRoute => HasActiveVoyage || HasActiveFlight;
    public bool OverlayHasMission => _lastDataSnapshot.Missions.Any(x => x.Status.Value.Equals("active", StringComparison.OrdinalIgnoreCase));
    public string OverlayPreviewButtonText => OverlayPreview
        ? (IsEnglish ? "Hide preview" : "Скрыть пример")
        : (IsEnglish ? "Show preview" : "Показать пример");
    public string OverlayEditorButtonText => OverlayEditMode
        ? (IsEnglish ? "Finish positioning" : "Завершить размещение")
        : (IsEnglish ? "Move on screen" : "Разместить на экране");
    public string OverlayShipDetectionStatus => string.IsNullOrWhiteSpace(ShipDetectionStatus)
        ? (IsEnglish ? "Open a ship screen, then run detection." : "Открой экран корабля и запусти обнаружение.")
        : ShipDetectionStatus;
    public string OverlayShipDisplay
    {
        get
        {
            var observed = _lastDataSnapshot.Player.CurrentShip;
            var value = observed?.Value ?? CurrentShip;
            if (string.IsNullOrWhiteSpace(value) || value is "Не выбран" or "Not selected")
                value = IsEnglish ? "Ship not detected" : "Корабль не определён";
            return value;
        }
    }
    public string OverlayShipSourceDisplay => _lastDataSnapshot.Player.CurrentShip is { } ship
        ? $"{SourceName(ship.Source)} • {ship.AgeDisplay}" : (IsEnglish ? "Selected in Nexus" : "Выбран в Nexus");
    public string OverlayLocationDisplay
    {
        get
        {
            var location = _lastDataSnapshot.Player.CurrentLocation;
            var system = _lastDataSnapshot.Player.CurrentSystem?.Value ?? CurrentSystem;
            var value = location?.Value ?? CurrentLocation;
            if (string.IsNullOrWhiteSpace(value) || value is "Не указана" or "Not specified")
                return IsEnglish ? "Location not detected" : "Локация не определена";
            var place = string.IsNullOrWhiteSpace(system) ? value : $"{system} · {value}";
            return place;
        }
    }
    public string OverlayLocationSourceDisplay => _lastDataSnapshot.Player.CurrentLocation is { } location
        ? $"{SourceName(location.Source)} • {location.AgeDisplay}" : (IsEnglish ? "Selected in Nexus" : "Выбрана в Nexus");
    public string OverlayRouteHeading => HasActiveVoyage
        ? (IsEnglish ? "ACTIVE ROUTE" : "АКТИВНЫЙ МАРШРУТ")
        : HasActiveFlight ? (IsEnglish ? "ACTIVE TRIP" : "АКТИВНЫЙ РЕЙС")
        : (IsEnglish ? "NO ACTIVE ROUTE" : "НЕТ АКТИВНОГО МАРШРУТА");
    public string OverlayStopDisplay => HasActiveVoyage
        ? ActiveVoyageStopDisplay
        : HasActiveFlight ? ActiveFlightDisplay
        : (IsEnglish ? "Choose a route in Nexus" : "Выбери маршрут в Nexus");
    public string OverlayActionDisplay => HasActiveVoyage
        ? LocalizationService.T(ActiveVoyageActionDisplay)
        : HasActiveFlight ? (IsEnglish ? "Confirm actual amounts in the journal after delivery." : "После доставки проверь фактические суммы в журнале.") : "";
    public string OverlayCargoDisplay => HasActiveVoyage ? LocalizationService.T(ActiveVoyageCargoDisplay) : "";
    public string OverlayProfitDisplay => HasActiveVoyage ? ActiveVoyageProfitDisplay : "";
    public string OverlayNextDisplay => HasActiveVoyage ? LocalizationService.T(ActiveVoyageNextDisplay) : "";
    public string OverlayMissionDisplay
    {
        get
        {
            var mission = _lastDataSnapshot.Missions.Where(x => x.Status.Value.Equals("active", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.Status.Timestamp).FirstOrDefault();
            if (mission is null) return "";
            var session = _lastDataSnapshot.Sessions.Where(x => x.EndedAt is null).OrderByDescending(x => x.StartedAt.Value).FirstOrDefault();
            var historic = session is null || mission.Status.Timestamp < session.StartedAt.Value;
            var label = historic ? (IsEnglish ? "From history" : "Из истории") : (IsEnglish ? "Mission" : "Миссия");
            var objective = mission.Objective?.Value;
            return $"{label}: {mission.Name.Value}\n{SourceName(mission.Status.Source)} • {mission.Status.AgeDisplay}" +
                (string.IsNullOrWhiteSpace(objective) ? "" : $"\n{objective}");
        }
    }
    public string OverlayFreshnessDisplay
    {
        get
        {
            if (HasActiveVoyage && ActiveVoyagePlan is { Trades.Count: > 0 } plan)
            {
                var oldest = plan.Trades.Min(x => x.UpdatedAt);
                return $"UEX • {new Models.ObservedValue<string>("", Models.DataSourceKind.Uex, oldest, 1).AgeDisplay}";
            }
            var value = _lastDataSnapshot.Player.CurrentLocation ?? _lastDataSnapshot.Player.CurrentShip;
            return value is null ? (IsEnglish ? "Waiting for live data" : "Ожидание данных")
                : $"{value.SourceDisplay} • {value.AgeDisplay}";
        }
    }
    public string OverlayEnvironmentDisplay => _lastDataSnapshot.Player.Environment?.Value ?? (IsEnglish ? "UNKNOWN" : "НЕИЗВЕСТНО");
    public bool OverlayDanger => string.Equals(CurrentSystem, "Pyro", StringComparison.OrdinalIgnoreCase) ||
        (ActiveVoyageStop?.IsPyro ?? false) ||
        (ActiveVoyagePlan?.Trades.Any(x => x.Risky) ?? false);
    public string OverlayDangerDisplay => string.Equals(CurrentSystem, "Pyro", StringComparison.OrdinalIgnoreCase) ||
        (ActiveVoyageStop?.IsPyro ?? false)
        ? (IsEnglish ? "DANGER • PYRO" : "ОПАСНО • PYRO")
        : (IsEnglish ? "RISKY NQA TERMINAL" : "РИСК • ТЕРМИНАЛ NQA");

    [RelayCommand]
    private void ToggleOverlayPreview() => OverlayPreview = !OverlayPreview;

    [RelayCommand]
    private void ToggleOverlayMode() => OverlayExpanded = !OverlayExpanded;

    [RelayCommand]
    private void HideOverlay()
    {
        _overlayHotkeyVisible = false;
        _overlaySuppressed = true;
        OverlayEditMode = false;
        OverlayPreview = false;
        OnPropertyChanged(nameof(OverlayHotkeyVisible));
        OnPropertyChanged(nameof(OverlaySuppressed));
    }

    [RelayCommand]
    private void ToggleOverlayEditor()
    {
        OverlayEditMode = !OverlayEditMode;
        OverlayPreview = OverlayEditMode;
    }

    [RelayCommand]
    private void ResetOverlayPosition()
    {
        OverlayAnchor = "BottomRight";
        OverlayCustomLeft = -1;
        OverlayCustomTop = -1;
    }

    internal void SetOverlayCustomPosition(double left, double top)
    {
        OverlayCustomLeft = left;
        OverlayCustomTop = top;
        OverlayAnchor = "Custom";
    }

    internal void ToggleOverlayFromHotkey()
    {
        var automaticVisible = OverlayEnabled && IsGameRunning;
        var currentlyVisible = OverlayEditMode || OverlayPreview || _overlayHotkeyVisible ||
            (automaticVisible && !_overlaySuppressed);
        OverlayEditMode = false;
        OverlayPreview = false;
        _overlayHotkeyVisible = !currentlyVisible;
        _overlaySuppressed = currentlyVisible && automaticVisible;
        OnPropertyChanged(nameof(OverlayHotkeyVisible));
        OnPropertyChanged(nameof(OverlaySuppressed));
    }

    internal void SetOverlayHotkeyRegistrationState(string state)
    {
        _overlayHotkeyRegistrationState = state;
        OnPropertyChanged(nameof(OverlayHotkeyStatus));
    }

    partial void OnOverlayEnabledChanged(bool value)
    {
        if (!value) OverlayPreview = false;
        QueueSave();
    }

    partial void OnOverlayExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(OverlayModeDisplay));
        OnPropertyChanged(nameof(OverlayModeButtonText));
        NotifyOverlayChanged();
        QueueSave();
    }

    partial void OnOverlayOpacityChanged(double value)
    {
        OnPropertyChanged(nameof(OverlayOpacityDisplay));
        QueueSave();
    }
    partial void OnOverlayTextOpacityChanged(double value)
    {
        OnPropertyChanged(nameof(OverlayTextOpacityDisplay));
        QueueSave();
    }
    partial void OnOverlayScaleChanged(double value)
    {
        OnPropertyChanged(nameof(OverlayScaleDisplay));
        QueueSave();
    }
    partial void OnOverlayAnchorChanged(string value) => QueueSave();
    partial void OnOverlayCustomLeftChanged(double value) => QueueSave();
    partial void OnOverlayCustomTopChanged(double value) => QueueSave();
    partial void OnOverlayShowShipChanged(bool value) => QueueSave();
    partial void OnOverlayShowLocationChanged(bool value) => QueueSave();
    partial void OnOverlayShowRouteChanged(bool value) => QueueSave();
    partial void OnOverlayShowMissionChanged(bool value) => QueueSave();
    partial void OnOverlayShowFreshnessChanged(bool value) => QueueSave();
    partial void OnOverlayPreviewChanged(bool value) => OnPropertyChanged(nameof(OverlayPreviewButtonText));
    partial void OnOverlayHotkeyChanged(string value)
    {
        OnPropertyChanged(nameof(OverlayHotkeyCaptureText));
        OnPropertyChanged(nameof(OverlayControlHint));
        QueueSave();
    }
    partial void OnIsOverlayHotkeyCapturingChanged(bool value) =>
        OnPropertyChanged(nameof(OverlayHotkeyCaptureText));
    partial void OnOverlayEditModeChanged(bool value)
    {
        OnPropertyChanged(nameof(OverlayEditorButtonText));
        OnPropertyChanged(nameof(OverlayControlHint));
    }

    partial void OnIsGameRunningChanged(bool value)
    {
        if (value)
        {
            _overlaySuppressed = false;
            OnPropertyChanged(nameof(OverlaySuppressed));
        }
    }

    private void NotifyOverlayChanged()
    {
        OnPropertyChanged(nameof(OverlayEnvironmentDisplay));
        OnPropertyChanged(nameof(OverlayShipDisplay));
        OnPropertyChanged(nameof(OverlayShipSourceDisplay));
        OnPropertyChanged(nameof(OverlayLocationDisplay));
        OnPropertyChanged(nameof(OverlayLocationSourceDisplay));
        OnPropertyChanged(nameof(OverlayBalanceDisplay));
        OnPropertyChanged(nameof(OverlayHasRoute));
        OnPropertyChanged(nameof(OverlayHasMission));
        OnPropertyChanged(nameof(OverlayControlHint));
        OnPropertyChanged(nameof(OverlayModeButtonText));
        OnPropertyChanged(nameof(OverlayHideButtonText));
        OnPropertyChanged(nameof(OverlayCompatibilityHint));
        OnPropertyChanged(nameof(OverlayRouteHeading));
        OnPropertyChanged(nameof(OverlayStopDisplay));
        OnPropertyChanged(nameof(OverlayActionDisplay));
        OnPropertyChanged(nameof(OverlayCargoDisplay));
        OnPropertyChanged(nameof(OverlayProfitDisplay));
        OnPropertyChanged(nameof(OverlayNextDisplay));
        OnPropertyChanged(nameof(OverlayMissionDisplay));
        OnPropertyChanged(nameof(OverlayFreshnessDisplay));
        OnPropertyChanged(nameof(OverlayDanger));
        OnPropertyChanged(nameof(OverlayDangerDisplay));
        OnPropertyChanged(nameof(OverlayShipDetectionStatus));
    }

    public sealed record OverlayAnchorOption(string Value, string Display)
    {
        public override string ToString() => Display;
    }
}
