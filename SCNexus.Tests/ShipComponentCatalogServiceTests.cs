using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using SCNexus.Services;

namespace SCNexus.Tests;

public class ShipComponentCatalogServiceTests
{
    [Fact]
    public async Task LoadsNestedEditablePortAndRejectsItemWithoutRequiredTag()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SCNexusTests", Guid.NewGuid().ToString("N"));
        try
        {
            using var client = new HttpClient(new CatalogHandler());
            var catalog = await new ShipComponentCatalogService(client, directory).LoadAsync("Test Ship");

            Assert.Equal(2, catalog.Slots.Count);
            var shieldSlot = Assert.Single(catalog.Slots, x => x.Type == "Shield");
            var gunSlot = Assert.Single(catalog.Slots, x => x.Type == "WeaponGun");
            Assert.Contains("turret", gunSlot.Key);
            Assert.Equal("stock-shield", shieldSlot.InstalledUuid);
            Assert.Contains(catalog.Components, x => x.Uuid == "stock-shield" &&
                x.CompatibleSlotKeys.Contains(shieldSlot.Key));
            Assert.Contains(catalog.Components, x => x.Uuid == "military-shield" &&
                x.PriceAuec == 12_000 && x.Shop == "New Babbage" &&
                x.CompatibleSlotKeys.Contains(shieldSlot.Key));
            var military = Assert.Single(catalog.Components, x => x.Uuid == "military-shield");
            Assert.Equal(4, military.PowerDraw);
            Assert.Equal("Stanton", Assert.Single(military.Offers).System);
            Assert.Equal(10, catalog.QuantumFuelCapacityScu);
            Assert.Equal(2, catalog.SchemaVersion);
            Assert.DoesNotContain(catalog.Components, x => x.Uuid == "civilian-shield");
            Assert.Contains(catalog.Components, x => x.Uuid == "gun-upgrade" &&
                x.CompatibleSlotKeys.Contains(gunSlot.Key));

            using var offlineClient = new HttpClient(new CatalogHandler(fail: true));
            var cached = await new ShipComponentCatalogService(offlineClient, directory).LoadAsync("Test Ship");
            Assert.Equal(catalog.Slots.Count, cached.Slots.Count);
            Assert.False(cached.UsedOldCache);

            var cacheFile = Assert.Single(Directory.GetFiles(directory, "*.json"));
            var node = JsonNode.Parse(await File.ReadAllTextAsync(cacheFile))!;
            node["FetchedAt"] = DateTimeOffset.UtcNow.AddDays(-2);
            await File.WriteAllTextAsync(cacheFile, node.ToJsonString());
            var stale = await new ShipComponentCatalogService(offlineClient, directory).LoadAsync("Test Ship");
            Assert.True(stale.UsedOldCache);
            Assert.Equal(catalog.Slots.Count, stale.Slots.Count);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private sealed class CatalogHandler(bool fail = false) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (fail) throw new HttpRequestException("offline");
            var path = request.RequestUri!.AbsolutePath;
            string body;
            if (path.EndsWith("/vehicles"))
                body = """{"data":[{"name":"Test Ship","slug":"test-ship"}]}""";
            else if (path.EndsWith("/vehicles/test-ship"))
                body = """
                    {"data":{"name":"Test Ship","version":"4.10-LIVE","quantum":{"quantum_fuel_capacity":10},"ports":[
                      {"name":"shield","type":"Shield","editable":true,"sizes":{"min":2,"max":2},
                       "required_tags":["mil"],"equipped_item_uuid":"stock-shield",
                       "equipped_item":{"uuid":"stock-shield","name":"Stock Shield","type":"Shield","size":2,
                         "shield":{"max_health":100,"max_shield_regen":10}}},
                      {"name":"turret","type":"Turret","editable":false,"ports":[
                        {"name":"gun","type":"WeaponGun","editable":true,"sizes":{"min":4,"max":4},
                         "equipped_item_uuid":"stock-gun",
                         "equipped_item":{"uuid":"stock-gun","name":"Stock Gun","type":"WeaponGun","size":4,
                           "vehicle_weapon":{"damage":{"sustained_60s":40,"burst":60}}}}
                      ]}
                    ]}}
                    """;
            else if (path.EndsWith("/items") && request.RequestUri.Query.Contains("Shield"))
                body = """
                    {"data":[
                      {"uuid":"military-shield","name":"Military Shield","type":"Shield","size":2,"tags":["mil"],
                       "version":"4.10-LIVE","shield":{"max_health":200,"max_shield_regen":20},
                       "resource_network":{"usage":{"power":{"max":4},"coolant":{"max":3}},"generation":{}},
                       "uex_prices":{"purchase":[{"price_buy":12000,"terminal_name":"New Babbage",
                         "starmap_location":{"name":"New Babbage","parent_name":"microTech","star_system_name":"Stanton"},
                         "game_version":"4.10-LIVE","date_updated":"__DATE__"}]}},
                      {"uuid":"civilian-shield","name":"Civilian Shield","type":"Shield","size":2,"tags":[],
                       "version":"4.10-LIVE","shield":{"max_health":250,"max_shield_regen":25},
                       "uex_prices":{"purchase":[{"price_buy":10000,"terminal_name":"Area18",
                         "game_version":"4.10-LIVE","date_updated":"__DATE__"}]}}
                    ],"meta":{"last_page":1}}
                    """;
            else if (path.EndsWith("/items") && request.RequestUri.Query.Contains("WeaponGun"))
                body = """
                    {"data":[{"uuid":"gun-upgrade","name":"Better Gun","type":"WeaponGun","size":4,
                      "version":"4.10-LIVE","vehicle_weapon":{"damage":{"sustained_60s":80,"burst":100}},
                      "uex_prices":{"purchase":[{"price_buy":5000,"terminal_name":"Area18",
                        "game_version":"4.10-LIVE","date_updated":"__DATE__"}]}}],"meta":{"last_page":1}}
                    """;
            else throw new InvalidOperationException(request.RequestUri.ToString());

            body = body.Replace("__DATE__", DateTimeOffset.UtcNow.ToString("O"));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
