namespace SCNexus.Models;

public sealed class VehicleCatalogItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string NameFull { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public decimal Scu { get; set; }
    public int IsSpaceship { get; set; }
    public int IsGroundVehicle { get; set; }
    public int? IsMilitary { get; set; }
    public int IsCargo { get; set; }
    public int IsMedical { get; set; }
    public int IsMining { get; set; }
    public int IsSalvage { get; set; }
    public int IsRefuel { get; set; }
    public int IsRepair { get; set; }
    public int IsBomber { get; set; }
    public int IsRacing { get; set; }
    public int IsDatarunner { get; set; }
    public int IsExploration { get; set; }
    public int IsResearch { get; set; }
    public int IsScience { get; set; }
    public int IsPassenger { get; set; }
    public int IsBoarding { get; set; }
    public string Display => $"{Name}  ·  {Scu:N0} SCU  ·  {CompanyName}";
    public override string ToString() => Name;
}
