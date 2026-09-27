using SCNexus.Models;
using SCNexus.Services;

namespace SCNexus.Tests;

public class LocationCatalogTests
{
    [Fact]
    public void SortsBySystemAndNameAndFindsBySubstring()
    {
        var terminals = new[]
        {
            new TradeTerminal { Name = "TDD", CityName = "New Babbage", StarSystemName = "Stanton", IsAvailableLive = 1 },
            new TradeTerminal { Name = "Other TDD", CityName = "New Babbage", StarSystemName = "Stanton", IsAvailableLive = 1 },
            new TradeTerminal { Name = "Terminal", CityName = "Area18", StarSystemName = "Stanton", IsAvailableLive = 1 },
            new TradeTerminal { Name = "Terminal", CityName = "Orison", StarSystemName = "Pyro", IsAvailableLive = 1 }
        };
        var options = LocationCatalog.Build(terminals);
        Assert.Equal(new[] { "Pyro · Orison", "Stanton · Area18", "Stanton · New Babbage" },
            options.Select(x => x.Display));
        Assert.Equal("New Babbage", Assert.Single(LocationCatalog.Search(options, "Stanton", "bab")).Name);
        Assert.Empty(LocationCatalog.Search(options, "Pyro", "bab"));
        Assert.Empty(LocationCatalog.Search(options, "Все системы", "b"));
    }
}
