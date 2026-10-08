using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;

namespace SCNexus.Services;

// Portraits are optional: the UI remains fully usable if the public wiki is offline.
public sealed class ShipPortraitService
{
    private static readonly HttpClient Client = CreateClient();
    private readonly ConcurrentDictionary<string, Uri?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<Uri?> GetPortraitAsync(string shipName, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(shipName)) return null;
        if (_cache.TryGetValue(shipName, out var cached)) return cached;

        try
        {
            var title = Uri.EscapeDataString(shipName);
            var url = "https://starcitizen.tools/api.php?action=query&format=json&redirects=1&prop=pageimages&pithumbsize=640&titles=" + title;
            using var response = await Client.GetAsync(url, token);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(token));
            if (!document.RootElement.TryGetProperty("query", out var query) ||
                !query.TryGetProperty("pages", out var pages)) return Cache(shipName, null);

            foreach (var page in pages.EnumerateObject())
            {
                if (page.Value.TryGetProperty("thumbnail", out var thumbnail) &&
                    thumbnail.TryGetProperty("source", out var source) &&
                    Uri.TryCreate(source.GetString(), UriKind.Absolute, out var portrait))
                    return Cache(shipName, portrait);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { }
        return Cache(shipName, null);
    }

    private Uri? Cache(string name, Uri? portrait)
    {
        _cache[name] = portrait;
        return portrait;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SCNexus/1.0 (ship portrait preview)");
        return client;
    }
}
