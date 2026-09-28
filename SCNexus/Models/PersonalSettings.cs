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
}
