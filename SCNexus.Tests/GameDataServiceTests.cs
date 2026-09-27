using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using SCNexus.Services;

namespace SCNexus.Tests;

public class GameDataServiceTests
{
    [Fact]
    public async Task ParsesSnakeCaseCachesAndFallsBackOffline()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SCNexusTests", Guid.NewGuid().ToString("N"));
        try
        {
            var handler = new StubHandler(false);
            using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/2.0/") };
            var first = await new GameDataService(client, directory).GetSnapshotAsync();
            Assert.Single(first.Quotes);
            Assert.Equal(120, first.Quotes[0].PriceBuy);
            Assert.Equal("New Babbage", first.Terminals[0].CityName);
            Assert.Equal(2, handler.CallCount);

            using var offlineClient = new HttpClient(new StubHandler(true)) { BaseAddress = client.BaseAddress };
            var freshCache = await new GameDataService(offlineClient, directory).GetSnapshotAsync();
            Assert.False(freshCache.UsedOldCache);

            foreach (var path in Directory.GetFiles(directory, "*.json"))
            {
                var node = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
                node["fetched_at"] = DateTimeOffset.UtcNow.AddDays(-2);
                await File.WriteAllTextAsync(path, node.ToJsonString());
            }
            var oldCache = await new GameDataService(offlineClient, directory).GetSnapshotAsync();
            Assert.True(oldCache.UsedOldCache);
            Assert.Single(oldCache.Quotes);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private sealed class StubHandler(bool fail) : HttpMessageHandler
    {
        public int CallCount;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref CallCount);
            if (fail) throw new HttpRequestException("offline");
            const string prices = """
                {"status":"ok","data":[{"id_commodity":1,"id_terminal":10,"commodity_name":"Gold","price_buy":120,"scu_buy":20,"date_modified":1790000000}]}
                """;
            const string terminals = """
                {"status":"ok","data":[{"id":10,"name":"TDD New Babbage","city_name":"New Babbage","type":"commodity","is_available_live":1}]}
                """;
            var body = request.RequestUri!.AbsolutePath.EndsWith("terminals", StringComparison.Ordinal) ? terminals : prices;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
