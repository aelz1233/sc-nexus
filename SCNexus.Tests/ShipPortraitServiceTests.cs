using System.Text.Json;
using SCNexus.Services;

namespace SCNexus.Tests;

public class ShipPortraitServiceTests
{
    [Fact]
    public void RejectsGenericPlanetPageForSpecificShip()
    {
        Assert.Contains("CRUSADER", ShipComponentCatalogService.BuildVehicleSearchTerms("Crusader Spirit C1"));
        using var pages = JsonDocument.Parse("""
            {"1":{"title":"Crusader","thumbnail":{"source":"https://example.test/crusader-planet.png"}}}
            """);

        Assert.Null(ShipPortraitService.SelectPortrait("Crusader Spirit C1", pages.RootElement));
    }

    [Fact]
    public void SelectsMatchingModelInsteadOfFirstSearchResult()
    {
        using var pages = JsonDocument.Parse("""
            {
              "1":{"title":"A1 Spirit","index":3,"thumbnail":{"source":"https://example.test/a1.png"}},
              "2":{"title":"C1 Spirit","index":1,"thumbnail":{"source":"https://example.test/c1.png"}},
              "3":{"title":"E1 Spirit","index":2,"thumbnail":{"source":"https://example.test/e1.png"}}
            }
            """);

        Assert.Equal("https://example.test/c1.png",
            ShipPortraitService.SelectPortrait("Crusader Spirit C1", pages.RootElement)?.AbsoluteUri);
    }

    [Fact]
    public void UsesSearchRankWhenModelScoresAreEqual()
    {
        using var pages = JsonDocument.Parse("""
            {
              "1":{"title":"Ares Star Fighter Inferno","index":2,"thumbnail":{"source":"https://example.test/inferno.png"}},
              "2":{"title":"Ares Star Fighter Ion","index":1,"thumbnail":{"source":"https://example.test/ion.png"}}
            }
            """);

        Assert.Equal("https://example.test/ion.png",
            ShipPortraitService.SelectPortrait("Ares Star Fighter", pages.RootElement)?.AbsoluteUri);
    }
}
