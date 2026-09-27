using SCNexus.Models;
using SCNexus.Services;

namespace SCNexus.Tests;

public class VehicleCatalogTests
{
    [Fact]
    public void FindsShipsByNameAndSortsByCargoOrName()
    {
        var ships = new[]
        {
            new VehicleCatalogItem { Name = "C2 Hercules", Scu = 696, IsSpaceship = 1 },
            new VehicleCatalogItem { Name = "A2 Hercules", Scu = 216, IsSpaceship = 1 },
            new VehicleCatalogItem { Name = "M2 Hercules", Scu = 522, IsSpaceship = 1 },
            new VehicleCatalogItem { Name = "Hercules Rover", Scu = 1, IsGroundVehicle = 1 }
        };
        Assert.Equal(new[] { "A2 Hercules", "C2 Hercules", "M2 Hercules" },
            VehicleCatalog.Search(ships, "her", "По названию").Select(x => x.Name));
        Assert.Equal(new[] { "C2 Hercules", "M2 Hercules", "A2 Hercules" },
            VehicleCatalog.Search(ships, "her", "По вместимости").Select(x => x.Name));
        Assert.Empty(VehicleCatalog.Search(ships, "h", "По названию"));
    }
}
