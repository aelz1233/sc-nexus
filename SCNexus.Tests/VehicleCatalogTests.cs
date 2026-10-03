using SCNexus.Models;
using SCNexus.Services;

namespace SCNexus.Tests;

public class VehicleCatalogTests
{
    [Fact]
    public void UsesCatalogRoleFlagsForModelsOutsideTheNameFallbackList()
    {
        Assert.Equal("Боевое", VehicleCatalog.InferRole(new() { Name = "Guardian MX", IsMilitary = 1 }));
        Assert.Equal("Утилизация", VehicleCatalog.InferRole(new() { Name = "New salvage model", IsCargo = 1, IsSalvage = 1 }));
        Assert.Equal("Медицинский", VehicleCatalog.InferRole(new() { Name = "New rescue model", IsMilitary = 1, IsMedical = 1 }));
    }
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
        Assert.Equal(3, VehicleCatalog.Search(ships, "", "По названию").Count);
    }

    [Theory]
    [InlineData("C2 Hercules Starlifter", "Грузоперевозки")]
    [InlineData("A2 Hercules Starlifter", "Бомбардировщик")]
    [InlineData("MOLE", "Добыча ресурсов")]
    [InlineData("Apollo Medivac", "Медицинский")]
    [InlineData("Unknown Ship", "Универсальный")]
    public void InfersRoleFromModel(string name, string expected)
    {
        Assert.Equal(expected, VehicleCatalog.InferRole(new VehicleCatalogItem { Name = name }));
    }
}
