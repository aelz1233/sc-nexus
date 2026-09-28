using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SCNexus.Services;

public sealed record UpdateAsset(string Name, string ApiUrl, long Size);
public sealed record UpdateRelease(Version Version, string PageUrl, UpdateAsset Installer, UpdateAsset Checksums);

public sealed class UpdateService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/aelz1233/sc-nexus/releases/latest";
    private readonly string _tokenPath;
    private readonly string _latestReleaseUrl;
    private readonly HttpClient _client;

    public UpdateService() : this(new HttpClient { Timeout = TimeSpan.FromMinutes(10) },
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SCNexus", "github-token.bin"), LatestReleaseUrl) { }

    public UpdateService(HttpClient client, string tokenPath, string latestReleaseUrl)
    {
        _client = client;
        _tokenPath = tokenPath;
        _latestReleaseUrl = latestReleaseUrl;
    }

    public Version CurrentVersion => Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0);
    public bool HasToken => File.Exists(_tokenPath);

    public void SaveToken(string token)
    {
        token = token.Trim();
        if (token.Length == 0) throw new ArgumentException("Вставь токен GitHub перед сохранением.");
        Directory.CreateDirectory(Path.GetDirectoryName(_tokenPath)!);
        File.WriteAllBytes(_tokenPath, ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser));
    }

    public void ClearToken()
    {
        if (File.Exists(_tokenPath)) File.Delete(_tokenPath);
    }

    private string? ReadToken()
    {
        if (!HasToken) return null;
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(_tokenPath), null, DataProtectionScope.CurrentUser));
    }

    private static HttpRequestMessage Request(string url, string? token, bool binary = false)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("SCNexus-Updater/1.0");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(binary ? "application/octet-stream" : "application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (!string.IsNullOrWhiteSpace(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static void EnsureSuccess(HttpResponseMessage response, bool hasToken)
    {
        if (response.IsSuccessStatusCode) return;
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new InvalidOperationException(hasToken
                ? "GitHub не открыл закрытый репозиторий. Проверь токен и право Contents: Read."
                : "Репозиторий закрытый. Сохрани токен GitHub с правом Contents: Read в настройках.");
        throw new HttpRequestException($"GitHub ответил кодом {(int)response.StatusCode}.");
    }

    private async Task<HttpResponseMessage> SendAsync(string url, string? token, bool binary, HttpCompletionOption completion, CancellationToken cancellationToken)
    {
        using var request = Request(url, token, binary);
        var response = await _client.SendAsync(request, completion, cancellationToken);
        if (token is not null && response.StatusCode is (HttpStatusCode.NotFound or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden))
        {
            response.Dispose();
            using var anonymous = Request(url, null, binary);
            response = await _client.SendAsync(anonymous, completion, cancellationToken);
        }
        try { EnsureSuccess(response, token is not null); return response; }
        catch { response.Dispose(); throw; }
    }

    public async Task<UpdateRelease> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        var token = ReadToken();
        using var response = await SendAsync(_latestReleaseUrl, token, false, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = json.RootElement;
        var tag = root.GetProperty("tag_name").GetString()?.TrimStart('v', 'V');
        if (!Version.TryParse(tag, out var version)) throw new InvalidDataException("У релиза GitHub неверный номер версии.");
        var assets = root.GetProperty("assets").EnumerateArray()
            .Select(x => new UpdateAsset(x.GetProperty("name").GetString()!, x.GetProperty("url").GetString()!, x.GetProperty("size").GetInt64())).ToArray();
        var installer = assets.SingleOrDefault(x => x.Name == $"SCNexus-Setup-{version.ToString(3)}-win-x64.exe")
            ?? throw new InvalidDataException("В релизе нет установщика Windows x64.");
        var checksums = assets.SingleOrDefault(x => x.Name == "SHA256SUMS.txt")
            ?? throw new InvalidDataException("В релизе нет контрольных сумм.");
        return new(version, root.GetProperty("html_url").GetString()!, installer, checksums);
    }

    public async Task<string> DownloadInstallerAsync(UpdateRelease release, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        var token = ReadToken();
        var apiHost = new Uri(_latestReleaseUrl).Host;
        foreach (var asset in new[] { release.Checksums, release.Installer })
        {
            var uri = new Uri(asset.ApiUrl);
            if (uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals(apiHost, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Адрес файла обновления не принадлежит GitHub API.");
        }
        using var checksumResponse = await SendAsync(release.Checksums.ApiUrl, token, true, HttpCompletionOption.ResponseContentRead, cancellationToken);
        var checksumText = await checksumResponse.Content.ReadAsStringAsync(cancellationToken);
        var expected = checksumText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(x => x.Length == 2 && x[1] == release.Installer.Name)
            .Select(x => x[0]).SingleOrDefault();
        if (expected is null || expected.Length != 64 || !expected.All(Uri.IsHexDigit))
            throw new InvalidDataException("Контрольная сумма установщика отсутствует или повреждена.");

        var directory = Path.Combine(Path.GetTempPath(), "SCNexus-Updates", release.Version.ToString(3));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, release.Installer.Name);
        var temporary = path + ".download";
        try
        {
            using var installerResponse = await SendAsync(release.Installer.ApiUrl, token, true, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            await using var source = await installerResponse.Content.ReadAsStreamAsync(cancellationToken);
            await using (var target = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long copied = 0;
                int count;
                while ((count = await source.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                    copied += count;
                    if (release.Installer.Size > 0) progress?.Report((int)Math.Min(100, copied * 100 / release.Installer.Size));
                }
            }
            string actual;
            await using (var verify = File.OpenRead(temporary))
                actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(verify, cancellationToken));
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Проверка установщика не прошла: файл скачан с ошибкой.");
            File.Move(temporary, path, true);
            return path;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
