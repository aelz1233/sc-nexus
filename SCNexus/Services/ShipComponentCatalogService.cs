using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SCNexus.Models;

namespace SCNexus.Services;

/// <summary>Ship ports and item statistics from Star Citizen Wiki API; shop prices are supplied by UEX.</summary>
public sealed class ShipComponentCatalogService
{
    private const int CatalogSchemaVersion = 2;
    private const string Api = "https://api.star-citizen.wiki/api/";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(12);
    private static readonly TimeSpan MaximumPriceAge = TimeSpan.FromDays(45);
    private static readonly string[] SupportedTypes = ["Shield", "PowerPlant", "Cooler", "QuantumDrive", "WeaponGun"];
    private static readonly string UserAgent = "SCNexus/" +
        (typeof(ShipComponentCatalogService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0");
    private readonly HttpClient _client;
    private readonly string _cacheDirectory;
    private readonly SemaphoreSlim _requestLimit = new(3);

    public ShipComponentCatalogService(HttpClient client, string? cacheDirectory = null)
    {
        _client = client;
        _cacheDirectory = cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SCNexus", "cache", "ship-components");
    }

    public async Task<ShipComponentCatalog> LoadAsync(string shipName, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(shipName))
            throw new ArgumentException("Выбери корабль для конфигуратора.", nameof(shipName));

        Directory.CreateDirectory(_cacheDirectory);
        var cachePath = Path.Combine(_cacheDirectory,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(shipName.Trim().ToUpperInvariant()))) + ".json");
        ShipComponentCatalog? cached = null;
        if (File.Exists(cachePath))
        {
            try
            {
                await using var cache = File.OpenRead(cachePath);
                cached = await JsonSerializer.DeserializeAsync<ShipComponentCatalog>(cache, cancellationToken: token);
            }
            catch (JsonException) { }
            catch (IOException) { }
        }

        if (cached is { Slots.Count: > 0, SchemaVersion: >= CatalogSchemaVersion } &&
            DateTimeOffset.UtcNow - cached.FetchedAt < CacheLifetime)
            return cached;

        try
        {
            var catalog = await DownloadAsync(shipName.Trim(), token);
            var temporary = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var file = File.Create(temporary))
                    await JsonSerializer.SerializeAsync(file, catalog, cancellationToken: token);
                File.Move(temporary, cachePath, true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            return catalog;
        }
        catch (Exception ex) when (!token.IsCancellationRequested &&
            ex is HttpRequestException or IOException or JsonException or InvalidDataException or TaskCanceledException)
        {
            if (cached is { Slots.Count: > 0 }) return cached with { UsedOldCache = true };
            throw new InvalidOperationException("Не удалось загрузить детали корабля. Проверь интернет и повтори попытку.", ex);
        }
    }

    private async Task<ShipComponentCatalog> DownloadAsync(string shipName, CancellationToken token)
    {
        var searchUrl = Api + "vehicles?filter%5Bname%5D=" + Uri.EscapeDataString(shipName) + "&page%5Bsize%5D=30";
        using var search = await GetJsonAsync(searchUrl, token);
        var matches = Get(search.RootElement, "data");
        if (matches.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Каталог кораблей вернул неожиданный ответ.");

        var candidates = matches.EnumerateArray()
            .Select(x => (Name: String(x, "name"), Slug: String(x, "slug")))
            .Where(x => !string.IsNullOrWhiteSpace(x.Slug)).ToList();
        var match = candidates.FirstOrDefault(x => x.Name.Equals(shipName, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(match.Slug) && candidates.Count == 1)
            match = candidates[0];
        if (string.IsNullOrWhiteSpace(match.Slug))
            throw new InvalidDataException($"Не удалось однозначно найти «{shipName}» в каталоге кораблей.");

        using var vehicleDocument = await GetJsonAsync(Api + "vehicles/" + Uri.EscapeDataString(match.Slug), token);
        var vehicle = Get(vehicleDocument.RootElement, "data");
        var version = String(vehicle, "version");
        var slots = new List<ShipComponentSlot>();
        WalkPorts(Get(vehicle, "ports"), "", slots);
        if (slots.Count == 0)
            throw new InvalidDataException($"Для «{match.Name}» нет подтверждённых заменяемых слотов.");

        var types = slots.Select(x => x.Type).Distinct(StringComparer.Ordinal).ToArray();
        var itemGroups = await Task.WhenAll(types.Select(x => LoadItemsAsync(x, token)));
        var components = new Dictionary<string, ShipComponent>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in itemGroups)
        foreach (var item in group)
        {
            var compatible = slots.Where(slot => IsCompatible(slot, item)).Select(slot => slot.Key).ToArray();
            if (compatible.Length == 0) continue;
            var component = ParseComponent(item, version) with { CompatibleSlotKeys = compatible };
            if (!string.IsNullOrWhiteSpace(component.Uuid)) components[component.Uuid] = component;
        }

        AddInstalledComponents(Get(vehicle, "ports"), "", slots, components, version);
        return new ShipComponentCatalog(match.Name, version, DateTimeOffset.UtcNow, slots,
            components.Values.OrderBy(x => x.Type).ThenBy(x => x.Size).ThenBy(x => x.Name).ToArray())
        {
            SchemaVersion = CatalogSchemaVersion,
            QuantumFuelCapacityScu = Number(Get(vehicle, "quantum"), "quantum_fuel_capacity")
        };
    }

    private async Task<List<JsonElement>> LoadItemsAsync(string type, CancellationToken token)
    {
        var items = new List<JsonElement>();
        for (var page = 1; page <= 12; page++)
        {
            var url = Api + "items?filter%5Btype%5D=" + Uri.EscapeDataString(type) +
                "&page%5Bsize%5D=100&page%5Bnumber%5D=" + page.ToString(CultureInfo.InvariantCulture);
            using var document = await GetJsonAsync(url, token);
            var data = Get(document.RootElement, "data");
            if (data.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Каталог компонентов вернул неожиданный ответ.");
            items.AddRange(data.EnumerateArray().Select(x => x.Clone()));
            var lastPage = Int(Get(document.RootElement, "meta"), "last_page");
            if (lastPage <= page) return items;
        }
        throw new InvalidDataException("Каталог компонентов превысил допустимое число страниц.");
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken token)
    {
        await _requestLimit.WaitAsync(token);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(18));
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(UserAgent);
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            return await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
        }
        finally
        {
            _requestLimit.Release();
        }
    }

    private static void WalkPorts(JsonElement ports, string parent, List<ShipComponentSlot> slots)
    {
        if (ports.ValueKind != JsonValueKind.Array) return;
        var index = 0;
        foreach (var port in ports.EnumerateArray())
        {
            var name = String(port, "name");
            var key = parent + "/" + (string.IsNullOrWhiteSpace(name) ? "port" : name) + "#" + index++;
            var type = String(port, "type");
            if (Bool(port, "editable") && SupportedTypes.Contains(type, StringComparer.Ordinal))
            {
                var sizes = Get(port, "sizes");
                var minSize = Int(sizes, "min");
                var maxSize = Int(sizes, "max");
                if (minSize > 0 && maxSize >= minSize)
                    slots.Add(new ShipComponentSlot(key, type, minSize, maxSize,
                        String(port, "equipped_item_uuid"), String(Get(port, "equipped_item"), "name"))
                    {
                        RequiredTags = Tags(Get(port, "required_tags")),
                        PortTags = Tags(Get(port, "port_tags"))
                    });
            }
            WalkPorts(Get(port, "ports"), key, slots);
        }
    }

    private static void AddInstalledComponents(JsonElement ports, string parent,
        IReadOnlyList<ShipComponentSlot> slots, Dictionary<string, ShipComponent> components, string version)
    {
        if (ports.ValueKind != JsonValueKind.Array) return;
        var index = 0;
        foreach (var port in ports.EnumerateArray())
        {
            var name = String(port, "name");
            var key = parent + "/" + (string.IsNullOrWhiteSpace(name) ? "port" : name) + "#" + index++;
            var slot = slots.FirstOrDefault(x => x.Key == key);
            var item = Get(port, "equipped_item");
            if (slot is not null && item.ValueKind == JsonValueKind.Object &&
                !string.IsNullOrWhiteSpace(slot.InstalledUuid))
            {
                if (components.TryGetValue(slot.InstalledUuid, out var existing))
                {
                    if (!existing.CompatibleSlotKeys.Contains(slot.Key))
                        components[slot.InstalledUuid] = existing with
                        { CompatibleSlotKeys = existing.CompatibleSlotKeys.Append(slot.Key).ToArray() };
                }
                else
                {
                    components[slot.InstalledUuid] = ParseComponent(item, version) with
                    { CompatibleSlotKeys = [slot.Key] };
                }
            }
            AddInstalledComponents(Get(port, "ports"), key, slots, components, version);
        }
    }

    private static bool IsCompatible(ShipComponentSlot slot, JsonElement item)
    {
        if (!String(item, "type").Equals(slot.Type, StringComparison.Ordinal)) return false;
        var size = Int(item, "size");
        if (size < slot.MinSize || size > slot.MaxSize) return false;
        var itemTags = Tags(Get(item, "tags"));
        var itemRequiredTags = Tags(Get(item, "required_tags"));
        return slot.RequiredTags.All(x => itemTags.Contains(x, StringComparer.OrdinalIgnoreCase)) &&
            itemRequiredTags.All(x => slot.PortTags.Contains(x, StringComparer.OrdinalIgnoreCase));
    }

    private static ShipComponent ParseComponent(JsonElement item, string fallbackVersion)
    {
        var type = String(item, "type");
        var offers = ReadPrices(Get(Get(item, "uex_prices"), "purchase"), fallbackVersion);
        var cheapest = offers.FirstOrDefault();
        var metric = type switch
        {
            "Shield" => (Number(Get(item, "shield"), "max_health"), Number(Get(item, "shield"), "max_shield_regen"), "Прочность щита / восстановление"),
            "PowerPlant" => (Number(Get(item, "power_plant"), "power_segment_generation"), Number(Get(item, "durability"), "health"), "Выработка энергии / прочность"),
            "Cooler" => (Number(Get(item, "cooler"), "coolant_segment_generation"), Number(Get(item, "durability"), "health"), "Охлаждение / прочность"),
            "QuantumDrive" => (Number(Get(Get(item, "quantum_drive"), "standard_jump"), "drive_speed"),
                Reciprocal(Number(Get(item, "quantum_drive"), "fuel_consumption_scu_per_gm")), "Скорость / экономичность"),
            "WeaponGun" => (Number(Get(Get(item, "vehicle_weapon"), "damage"), "sustained_60s"),
                Number(Get(Get(item, "vehicle_weapon"), "damage"), "burst"), "Урон за 60 секунд / пиковый урон"),
            _ => (0d, 0d, "")
        };
        var resource = Get(item, "resource_network");
        var usage = Get(resource, "usage");
        var generation = Get(resource, "generation");
        return new ShipComponent(String(item, "uuid"), String(item, "name"), type, Int(item, "size"),
            cheapest?.PriceAuec, cheapest?.Shop, cheapest?.UpdatedAt,
            String(item, "version", fallbackVersion), metric.Item1, metric.Item2, metric.Item3)
        {
            Offers = offers,
            PowerDraw = Number(Get(usage, "power"), "max"),
            CoolantDraw = Number(Get(usage, "coolant"), "max"),
            PowerGeneration = Number(generation, "power"),
            CoolantGeneration = Number(generation, "coolant"),
            QuantumFuelConsumptionScuPerGm = Number(Get(item, "quantum_drive"), "fuel_consumption_scu_per_gm")
        };
    }

    private static IReadOnlyList<ComponentShopOffer> ReadPrices(JsonElement purchase, string version)
    {
        if (purchase.ValueKind != JsonValueKind.Array) return [];
        var now = DateTimeOffset.UtcNow;
        return purchase.EnumerateArray().Select(x =>
        {
            var location = Get(x, "starmap_location");
            var updated = DateTimeOffset.TryParse(String(x, "date_updated"), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var date) ? date : (DateTimeOffset?)null;
            return new
            {
                Amount = Decimal(x, "price_buy"),
                Shop = String(x, "terminal_name"),
                Location = String(location, "name"),
                Parent = String(location, "parent_name"),
                System = String(location, "star_system_name"),
                Version = String(x, "game_version"),
                Updated = updated
            };
        }).Where(x => x.Amount > 0 && x.Updated is { } updated &&
            now - updated <= MaximumPriceAge && updated <= now.AddDays(1) &&
            (string.IsNullOrWhiteSpace(x.Version) || string.IsNullOrWhiteSpace(version) || x.Version == version))
          .OrderBy(x => x.Amount).ThenByDescending(x => x.Updated)
          .Select(x => new ComponentShopOffer(x.Amount, x.Shop, x.Location, x.Parent, x.System, x.Updated!.Value))
          .ToArray();
    }

    private static double Reciprocal(double value) => value > 0 ? 1 / value : 0;
    private static JsonElement Get(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var found) ? found : default;
    private static string String(JsonElement value, string name, string fallback = "") =>
        Get(value, name) is { ValueKind: JsonValueKind.String } field ? field.GetString() ?? fallback : fallback;
    private static int Int(JsonElement value, string name) =>
        Get(value, name) is { ValueKind: JsonValueKind.Number } field && field.TryGetInt32(out var result) ? result : 0;
    private static bool Bool(JsonElement value, string name) =>
        Get(value, name) is { ValueKind: JsonValueKind.True };
    private static double Number(JsonElement value, string name) =>
        Get(value, name) is { ValueKind: JsonValueKind.Number } field && field.TryGetDouble(out var result) ? result : 0;
    private static decimal Decimal(JsonElement value, string name) =>
        Get(value, name) is { ValueKind: JsonValueKind.Number } field && field.TryGetDecimal(out var result) ? result : 0;
    private static IReadOnlyList<string> Tags(JsonElement value) => value.ValueKind == JsonValueKind.Array
        ? value.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : String(x, "name"))
            .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToArray()
        : [];
}
