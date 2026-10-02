namespace SCNexus.Models;

public sealed class PersonalSettings
{
    public int Id { get; set; } = 1;
    public decimal Balance { get; set; }
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
    public bool OcrEnabled { get; set; }
    public string Language { get; set; } = "ru";
}
