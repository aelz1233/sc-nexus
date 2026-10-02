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
            return observed is null ? value : $"{value} • {observed.SourceDisplay} • {observed.AgeDisplay}";
        }
    }
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
            return location is null ? place : $"{place} • {location.SourceDisplay} • {location.AgeDisplay}";
        }
    }
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
        : (IsEnglish ? "Route guidance will appear here." : "Здесь появятся действия по маршруту.");
    public string OverlayCargoDisplay => HasActiveVoyage ? LocalizationService.T(ActiveVoyageCargoDisplay) : "";
    public string OverlayProfitDisplay => HasActiveVoyage ? ActiveVoyageProfitDisplay : "";
    public string OverlayNextDisplay => HasActiveVoyage ? LocalizationService.T(ActiveVoyageNextDisplay) : "";
    public string OverlayMissionDisplay => LiveMissionDisplay;
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
    public bool OverlayDanger => CurrentSystem.Equals("Pyro", StringComparison.OrdinalIgnoreCase) ||
        (ActiveVoyageStop?.IsPyro ?? false) ||
        (ActiveVoyagePlan?.Trades.Any(x => x.Risky) ?? false);
    public string OverlayDangerDisplay => CurrentSystem.Equals("Pyro", StringComparison.OrdinalIgnoreCase) ||
        (ActiveVoyageStop?.IsPyro ?? false)
        ? (IsEnglish ? "DANGER • PYRO" : "ОПАСНО • PYRO")
        : (IsEnglish ? "RISKY NQA TERMINAL" : "РИСК • ТЕРМИНАЛ NQA");

    [RelayCommand]
    private void ToggleOverlayPreview() => OverlayPreview = !OverlayPreview;

    [RelayCommand]
    private void ToggleOverlayMode() => OverlayExpanded = !OverlayExpanded;

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
        var currentlyVisible = OverlayPreview || _overlayHotkeyVisible ||
            (automaticVisible && !_overlaySuppressed);
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
        QueueSave();
    }
    partial void OnIsOverlayHotkeyCapturingChanged(bool value) =>
        OnPropertyChanged(nameof(OverlayHotkeyCaptureText));
    partial void OnOverlayEditModeChanged(bool value) =>
        OnPropertyChanged(nameof(OverlayEditorButtonText));

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
        OnPropertyChanged(nameof(OverlayShipDisplay));
        OnPropertyChanged(nameof(OverlayLocationDisplay));
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
