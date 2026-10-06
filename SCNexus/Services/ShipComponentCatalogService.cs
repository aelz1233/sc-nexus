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
    private const int CatalogSchemaVersion = 3;
    private const string Api = "https://api.star-citizen.wiki/api/";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(12);
    private static readonly TimeSpan MaximumPriceAge = TimeSpan.FromDays(45);
    private static readonly string[] SupportedTypes = ["Shield", "PowerPlant", "Cooler", "QuantumDrive", "WeaponGun"];
    private static readonly string UserAgent = "SCNexus/" +
        (typeof(ShipComponentCatalogService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0");
    private readonly HttpClient _client;
    private readonly string _cacheDirectory;
    private readonly SemaphoreSlim _requestLimit = new(2);

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
            throw new InvalidOperationException(
                $"Не удалось загрузить детали «{shipName}»: {ex.Message}",
                ex);
        }
    }

    private async Task<ShipComponentCatalog> DownloadAsync(string shipName, CancellationToken token)
    {
        var match = await FindVehicleAsync(shipName, token);

        using var vehicleDocument = await GetJsonAsync(
            Api + "vehicles/" + Uri.EscapeDataString(match.Slug), token);

        var vehicle = Get(vehicleDocument.RootElement, "data");
        var version = String(vehicle, "version");
        var slots = new List<ShipComponentSlot>();
        WalkPorts(Get(vehicle, "ports"), "", slots);

        if (slots.Count == 0)
            throw new InvalidDataException(
                $"Для «{match.Name}» нет подтверждённых заменяемых слотов.");

        var types = slots.Select(x => x.Type).Distinct(StringComparer.Ordinal).ToArray();

        // Грузим типы последовательно. API Wiki иногда рвёт TLS/HTTP при пачке параллельных запросов.
        var itemGroups = new List<List<JsonElement>>();
        foreach (var type in types)
            itemGroups.Add(await LoadItemsAsync(type, token));

        var components = new Dictionary<string, ShipComponent>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in itemGroups)
        foreach (var item in group)
        {
            var compatible = slots
                .Where(slot => IsCompatible(slot, item))
                .Select(slot => slot.Key)
                .ToArray();

            if (compatible.Length == 0) continue;

            var component = ParseComponent(item, version) with
            {
                CompatibleSlotKeys = compatible
            };

            if (!string.IsNullOrWhiteSpace(component.Uuid))
                components[component.Uuid] = component;
        }

        AddInstalledComponents(Get(vehicle, "ports"), "", slots, components, version);

        return new ShipComponentCatalog(
            match.Name,
            version,
            DateTimeOffset.UtcNow,
            slots,
            components.Values
                .OrderBy(x => x.Type)
                .ThenBy(x => x.Size)
                .ThenBy(x => x.Name)
                .ToArray())
        {
            SchemaVersion = CatalogSchemaVersion,
            QuantumFuelCapacityScu = Number(
                Get(vehicle, "quantum"),
                "quantum_fuel_capacity")
        };
    }

    private async Task<(string Name, string Slug)> FindVehicleAsync(
        string shipName,
        CancellationToken token)
    {
        var searchTerms = BuildVehicleSearchTerms(shipName);
        var candidates = new Dictionary<string, (string Name, string Slug)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var term in searchTerms)
        {
            var searchUrl = Api + "vehicles?filter%5Bname%5D=" +
                Uri.EscapeDataString(term) +
                "&page%5Bsize%5D=50";

            using var search = await GetJsonAsync(searchUrl, token);
            var matches = Get(search.RootElement, "data");

            if (matches.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException(
                    "Каталог кораблей вернул неожиданный ответ.");

            foreach (var item in matches.EnumerateArray())
            {
                var name = String(item, "name");
                var slug = String(item, "slug");

                if (string.IsNullOrWhiteSpace(name) ||
                    string.IsNullOrWhiteSpace(slug))
                    continue;

                candidates[slug] = (name, slug);
            }

            // Если получили точное каноническое совпадение — дальше API не дёргаем.
            var exact = candidates.Values.FirstOrDefault(x =>
                VehicleTokenKey(x.Name) == VehicleTokenKey(shipName));

            if (!string.IsNullOrWhiteSpace(exact.Slug))
                return exact;
        }

        if (candidates.Count == 0)
        {
            throw new InvalidDataException(
                $"Корабль «{shipName}» не найден. " +
                $"Запросы: {string.Join(", ", searchTerms.Select(x => $"«{x}»"))}.");
        }

        var ranked = candidates.Values
            .Select(x => (Candidate: x, Score: VehicleMatchScore(shipName, x.Name)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Candidate.Name.Length)
            .ToArray();

        var best = ranked[0];

        // Низкий score означает, что API вернул вообще не тот корабль.
        if (best.Score < 45)
        {
            throw new InvalidDataException(
                $"Не удалось однозначно сопоставить «{shipName}». " +
                $"API вернул: {string.Join(", ", ranked.Take(8).Select(x => x.Candidate.Name))}");
        }

        return best.Candidate;
    }

    private static IReadOnlyList<string> BuildVehicleSearchTerms(string shipName)
    {
        var result = new List<string>();
        Add(shipName);

        if (shipName.Contains("Starfighter", StringComparison.OrdinalIgnoreCase))
            Add(ReplaceIgnoreCase(shipName, "Starfighter", "Star Fighter"));

        if (shipName.Contains("Star Fighter", StringComparison.OrdinalIgnoreCase))
            Add(ReplaceIgnoreCase(shipName, "Star Fighter", "Starfighter"));

        // UEX и Wiki расходятся в названиях Ares.
        if (shipName.Contains("Ares", StringComparison.OrdinalIgnoreCase) &&
            shipName.Contains("Inferno", StringComparison.OrdinalIgnoreCase))
        {
            Add("Ares Star Fighter Inferno");
            Add("Ares Inferno");
            Add("Inferno");
        }
        else if (shipName.Contains("Ares", StringComparison.OrdinalIgnoreCase) &&
                 shipName.Contains("Ion", StringComparison.OrdinalIgnoreCase))
        {
            Add("Ares Star Fighter Ion");
            Add("Ares Ion");
            Add("Ares Ion Starfighter");
        }

        // Универсальный fallback: пробуем самый длинный отличительный токен модели.
        var distinctive = VehicleTokens(shipName)
            .Where(x => x.Length >= 4 &&
                        x is not "STARFIGHTER" and not "FIGHTER" and not "STAR" and
                        not "SHIP" and not "HERCULES" and not "SPIRIT")
            .OrderByDescending(x => x.Length)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(distinctive))
            Add(distinctive);

        return result;

        void Add(string value)
        {
            value = value.Trim();
            if (value.Length == 0) return;
            if (!result.Contains(value, StringComparer.OrdinalIgnoreCase))
                result.Add(value);
        }
    }

    private static double VehicleMatchScore(string requested, string candidate)
    {
        var requestedKey = VehicleTokenKey(requested);
        var candidateKey = VehicleTokenKey(candidate);

        if (requestedKey == candidateKey)
            return 100;

        var requestedTokens = VehicleTokens(requested);
        var candidateTokens = VehicleTokens(candidate);

        if (requestedTokens.Count == 0 || candidateTokens.Count == 0)
            return 0;

        var intersection = requestedTokens
            .Intersect(candidateTokens, StringComparer.OrdinalIgnoreCase)
            .Count();

        var union = requestedTokens
            .Union(candidateTokens, StringComparer.OrdinalIgnoreCase)
            .Count();

        var score = union == 0 ? 0 : 100d * intersection / union;

        var candidateUpper = candidate.ToUpperInvariant();
        var requestedUpper = requested.ToUpperInvariant();

        // Не выбираем спец-версии, если пользователь их явно не просил.
        foreach (var marker in new[] { "WIKELO", "WAR SPECIAL", "EXECUTIVE", "BEST IN SHOW" })
        {
            if (candidateUpper.Contains(marker, StringComparison.Ordinal) &&
                !requestedUpper.Contains(marker, StringComparison.Ordinal))
                score -= 25;
        }

        return score;
    }

    private static string VehicleTokenKey(string value) =>
        string.Join("|", VehicleTokens(value)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase));

    private static HashSet<string> VehicleTokens(string value)
    {
        value = ReplaceIgnoreCase(value, "Star Fighter", "Starfighter");
        value = ReplaceIgnoreCase(value, "Mark II", "MK2");
        value = ReplaceIgnoreCase(value, "MK II", "MK2");

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
            builder.Append(char.IsLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : ' ');

        return builder.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string ReplaceIgnoreCase(string value, string oldValue, string newValue)
    {
        var index = value.IndexOf(oldValue, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return value;

        return value[..index] + newValue + value[(index + oldValue.Length)..];
    }

    private async Task<List<JsonElement>> LoadItemsAsync(string type, CancellationToken token)
    {
        const int maxPages = 50;
        var items = new List<JsonElement>();

        for (var page = 1; page <= maxPages; page++)
        {
            var url = Api + "items?filter%5Btype%5D=" +
                Uri.EscapeDataString(type) +
                "&page%5Bsize%5D=100&page%5Bnumber%5D=" +
                page.ToString(CultureInfo.InvariantCulture);

            using var document = await GetJsonAsync(url, token);
            var data = Get(document.RootElement, "data");

            if (data.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException(
                    $"Каталог компонентов «{type}» вернул неожиданный ответ.");

            var pageItems = data.EnumerateArray()
                .Select(x => x.Clone())
                .ToArray();

            items.AddRange(pageItems);

            var lastPage = Int(Get(document.RootElement, "meta"), "last_page");

            if (lastPage > 0 && page >= lastPage)
                return items;

            // Fallback для API без корректного meta.last_page.
            if (pageItems.Length == 0 || (lastPage <= 0 && pageItems.Length < 100))
                return items;
        }

        throw new InvalidDataException(
            $"Каталог компонентов «{type}» превысил {maxPages} страниц.");
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken token)
    {
        const int maxAttempts = 3;

        await _requestLimit.WaitAsync(token);
        try
        {
            Exception? lastError = null;

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                token.ThrowIfCancellationRequested();

                try
                {
                    using var timeout =
                        CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(60));

                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.UserAgent.ParseAdd(UserAgent);
                    request.Headers.Accept.ParseAdd("application/json");

                    using var response = await _client.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        timeout.Token);

                    var statusCode = (int)response.StatusCode;

                    if (!response.IsSuccessStatusCode)
                    {
                        if (attempt < maxAttempts && IsTransientStatus(statusCode))
                        {
                            lastError = new HttpRequestException(
                                $"HTTP {statusCode} для {url}",
                                null,
                                response.StatusCode);

                            await DelayBeforeRetryAsync(attempt, token);
                            continue;
                        }

                        response.EnsureSuccessStatusCode();
                    }

                    await using var stream =
                        await response.Content.ReadAsStreamAsync(timeout.Token);

                    return await JsonDocument.ParseAsync(
                        stream,
                        cancellationToken: timeout.Token);
                }
                catch (OperationCanceledException ex)
                    when (!token.IsCancellationRequested)
                {
                    lastError = ex;

                    if (attempt == maxAttempts)
                        throw new HttpRequestException(
                            $"Таймаут после {maxAttempts} попыток. URL: {url}",
                            ex);

                    await DelayBeforeRetryAsync(attempt, token);
                }
                catch (HttpRequestException ex)
                {
                    lastError = ex;

                    var statusCode = ex.StatusCode is { } code ? (int)code : 0;
                    var canRetry = ex.StatusCode is null || IsTransientStatus(statusCode);

                    if (attempt == maxAttempts || !canRetry)
                        throw new HttpRequestException(
                            $"URL: {url} | {ex.Message}",
                            ex,
                            ex.StatusCode);

                    await DelayBeforeRetryAsync(attempt, token);
                }
                catch (IOException ex)
                {
                    lastError = ex;

                    if (attempt == maxAttempts)
                        throw new HttpRequestException(
                            $"Ошибка чтения ответа. URL: {url} | {ex.Message}",
                            ex);

                    await DelayBeforeRetryAsync(attempt, token);
                }
            }

            throw new HttpRequestException(
                $"Не удалось получить данные после {maxAttempts} попыток. URL: {url}",
                lastError);
        }
        finally
        {
            _requestLimit.Release();
        }
    }

    private static bool IsTransientStatus(int statusCode) =>
        statusCode is 408 or 429 || statusCode >= 500;

    private static Task DelayBeforeRetryAsync(int attempt, CancellationToken token) =>
        Task.Delay(TimeSpan.FromSeconds(Math.Min(4, 1 << (attempt - 1))), token);

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

        var fresh = purchase.EnumerateArray()
            .Select(x =>
            {
                var location = Get(x, "starmap_location");
                var updated = DateTimeOffset.TryParse(
                    String(x, "date_updated"),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out var date)
                    ? date
                    : (DateTimeOffset?)null;

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
            })
            .Where(x =>
                x.Amount > 0 &&
                x.Updated is { } updated &&
                now - updated <= MaximumPriceAge &&
                updated <= now.AddDays(1))
            .ToArray();

        if (fresh.Length == 0)
            return [];

        // Сначала используем цены текущей версии игры.
        // Если Wiki/UEX ещё не успели проставить версию — не оставляем компонент вообще без цены.
        var matchingVersion = fresh
            .Where(x => GameVersionMatches(x.Version, version))
            .ToArray();

        var selected = matchingVersion.Length > 0
            ? matchingVersion
            : fresh;

        return selected
            .OrderBy(x => x.Amount)
            .ThenByDescending(x => x.Updated)
            .Select(x => new ComponentShopOffer(
                x.Amount,
                x.Shop,
                x.Location,
                x.Parent,
                x.System,
                x.Updated!.Value))
            .ToArray();
    }

    private static bool GameVersionMatches(string offerVersion, string vehicleVersion)
    {
        if (string.IsNullOrWhiteSpace(offerVersion) ||
            string.IsNullOrWhiteSpace(vehicleVersion))
            return true;

        if (offerVersion.Equals(vehicleVersion, StringComparison.OrdinalIgnoreCase))
            return true;

        var offerBase = VersionPrefix(offerVersion);
        var vehicleBase = VersionPrefix(vehicleVersion);

        return offerBase.Equals(vehicleBase, StringComparison.OrdinalIgnoreCase);
    }

    private static string VersionPrefix(string value)
    {
        value = value.Trim();

        var cut = value.IndexOfAny(['-', ' ', '+']);
        return cut < 0 ? value : value[..cut];
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
