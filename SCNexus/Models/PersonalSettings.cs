namespace SCNexus.Models;

public sealed class PersonalSettings
{
    public int Id { get; set; } = 1;
    public decimal Balance { get; set; }
    public DateTimeOffset? BalanceManualUpdatedAt { get; set; }
    public string CurrentShip { get; set; } = "Не выбран";
    public string CurrentLocation { get; set; } = "Не указана";
    public string CurrentSystem { get; set; } = "";
    public int CargoScu { get; set; }
    public decimal Reserve { get; set; }
    public bool AllowRisky { get; set; }
    public bool AvoidPyro { get; set; }
    public int MinimumFillPercent { get; set; }
    public decimal MinimumProfit { get; set; }
    public string GameDirectoryPath { get; set; } = "";
    public bool MonitorEnabled { get; set; } = true;
    public int MonitorIntervalSeconds { get; set; } = 15;
    public bool ShowRouteDetails { get; set; } = true;
    public bool ContractEarningsEnabled { get; set; } = true;
    public string ContractEarningsJson { get; set; } = "[]";
    public bool OcrEnabled { get; set; }
    public bool AutoFleetOcrEnabled { get; set; } = true;
    public bool FleetOcrAutoScroll { get; set; } = true;
    public int OcrIntervalSeconds { get; set; } = 5;
    public bool LightTheme { get; set; }
    public string Language { get; set; } = "ru";
    public bool OverlayEnabled { get; set; } = true;
    public bool OverlayExpanded { get; set; }
    public double OverlayOpacity { get; set; } = 0.92;
    public double OverlayTextOpacity { get; set; } = 1;
    public double OverlayScale { get; set; } = 1;
    public string OverlayAnchor { get; set; } = "BottomRight";
    public double OverlayCustomLeft { get; set; } = -1;
    public double OverlayCustomTop { get; set; } = -1;
    public bool OverlayShowShip { get; set; } = true;
    public bool OverlayShowLocation { get; set; } = true;
    public bool OverlayShowRoute { get; set; } = true;
    public bool OverlayShowMission { get; set; } = true;
    public bool OverlayShowFreshness { get; set; } = true;
    public string OverlayHotkey { get; set; } = "";
    public string ActiveVoyageJson { get; set; } = "";
    public string ActiveShoppingJson { get; set; } = "";
    public string LastSessionSummary { get; set; } = "";
    public DateTimeOffset? LastSessionEndedAt { get; set; }
}
