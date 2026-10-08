using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Collections.Concurrent;
using SCNexus.Models;

namespace SCNexus.Services;

public sealed class GameDataService
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };
    private readonly string _cacheDirectory;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _cacheGates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, object> _memoryCache = new(StringComparer.OrdinalIgnoreCase);

    public GameDataService(HttpClient client, string? cacheDirectory = null)
    {
        _client = client;
        _cacheDirectory = cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SCNexus", "cache");
    }

    public async Task<DataSnapshot> GetSnapshotAsync(CancellationToken token = default, bool forceRefresh = false)
    {
        var pricesTask = LoadAsync<CommodityQuote>("prices", "commodities_prices_all", forceRefresh ? TimeSpan.Zero : TimeSpan.FromMinutes(30), token);
        var terminalsTask = LoadAsync<TradeTerminal>("terminals", "terminals?type=commodity", forceRefresh ? TimeSpan.Zero : TimeSpan.FromHours(12), token);
        var prices = await pricesTask;
        var terminals = await terminalsTask;
        return new DataSnapshot(prices.Data, terminals.Data, prices.FetchedAt, terminals.FetchedAt,
            prices.UsedOldCache || terminals.UsedOldCache);
    }

    public async Task<IReadOnlyList<TradeTerminal>> GetTerminalsAsync(CancellationToken token = default)
    {
        var terminals = await LoadAsync<TradeTerminal>("terminals", "terminals?type=commodity", TimeSpan.FromHours(12), token);
        return terminals.Data;
    }

    public async Task<IReadOnlyList<VehicleCatalogItem>> GetVehiclesAsync(CancellationToken token = default)
    {
        var vehicles = await LoadAsync<VehicleCatalogItem>("vehicles", "vehicles", TimeSpan.FromHours(12), token);
        if (vehicles.Data.All(x => x.IsMilitary is null))
            vehicles = await LoadAsync<VehicleCatalogItem>("vehicles", "vehicles", TimeSpan.Zero, token);
        return vehicles.Data;
    }

    private async Task<CachedData<T>> LoadAsync<T>(string key, string endpoint, TimeSpan ttl, CancellationToken token)
    {
        var gate = _cacheGates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token);
        try
        {
            return await LoadCoreAsync<T>(key, endpoint, ttl, token);
        }
        finally { gate.Release(); }
    }

    private async Task<CachedData<T>> LoadCoreAsync<T>(string key, string endpoint, TimeSpan ttl,
        CancellationToken token)
    {
        var path = Path.Combine(_cacheDirectory, key + ".json");
        var cached = _memoryCache.TryGetValue(key, out var memory) ? memory as CachedData<T> : null;
        if (cached is null && File.Exists(path))
        {
            try
            {
                await using var file = File.OpenRead(path);
                cached = await JsonSerializer.DeserializeAsync<CachedData<T>>(file, Json, token);
                if (cached is not null) _memoryCache[key] = cached;
            }
            catch (JsonException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        if (cached is { Data.Count: > 0 } && DateTimeOffset.UtcNow - cached.FetchedAt < ttl)
            return cached;

        try
        {
            using var response = await _client.GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            var payload = await JsonSerializer.DeserializeAsync<UexResponse<T>>(stream, Json, token);
            if (payload?.Status != "ok" || payload.Data.Count == 0)
                throw new InvalidDataException("UEX не вернул торговые данные.");
            var result = new CachedData<T>(DateTimeOffset.UtcNow, payload.Data, false);
            _memoryCache[key] = result;
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(_cacheDirectory);
                await using (var file = File.Create(temporary))
                    await JsonSerializer.SerializeAsync(file, result, Json, token);
                File.Move(temporary, path, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The downloaded data remains usable when the disk cache cannot be updated.
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or JsonException or TaskCanceledException)
        {
            if (cached is { Data.Count: > 0 }) return cached with { UsedOldCache = true };
            throw new InvalidOperationException("Торговые данные UEX недоступны, сохранённого кэша пока нет.", ex);
        }
    }

    private sealed record CachedData<T>(DateTimeOffset FetchedAt, List<T> Data, bool UsedOldCache);
}
