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
    public string Display => $"{Name}  ·  {Scu:N0} SCU  ·  {CompanyName}";
}
