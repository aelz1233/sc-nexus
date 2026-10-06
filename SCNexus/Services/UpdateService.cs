using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Diagnostics;
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
    private const string PublicVersionUrl = "https://raw.githubusercontent.com/aelz1233/sc-nexus/main/VERSION";
    private const string PublicReleaseBaseUrl = "https://github.com/aelz1233/sc-nexus/releases";
    private readonly string _tokenPath;
    private readonly string _latestReleaseUrl;
    private readonly string _stateDirectory;
    private readonly string _rollbackMarkerPath;
    private readonly HttpClient _client;

    public UpdateService() : this(new HttpClient { Timeout = TimeSpan.FromMinutes(10) },
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SCNexus", "github-token.bin"), LatestReleaseUrl) { }

    public UpdateService(HttpClient client, string tokenPath, string latestReleaseUrl, string? stateDirectory = null)
    {
        _client = client;
        _tokenPath = tokenPath;
        _latestReleaseUrl = latestReleaseUrl;
        _stateDirectory = stateDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SCNexus", "updates");
        _rollbackMarkerPath = Path.Combine(_stateDirectory, "pending-rollback.json");
    }

    public Version CurrentVersion => Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0);
    public bool HasToken => File.Exists(_tokenPath);
    public bool HasPendingRollback => File.Exists(_rollbackMarkerPath);

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
                ? "GitHub не открыл релиз. Проверь токен и право Contents: Read."
                : "GitHub не открыл релиз. Проверь доступность репозитория; для закрытого репозитория нужен токен с правом Contents: Read.");
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

    private bool UsesPublicFeed => _latestReleaseUrl.Equals(LatestReleaseUrl, StringComparison.OrdinalIgnoreCase);

    public async Task<UpdateRelease> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        if (UsesPublicFeed)
        {
            using var versionResponse = await SendAsync(PublicVersionUrl, null, false, HttpCompletionOption.ResponseContentRead, cancellationToken);
            var tag = (await versionResponse.Content.ReadAsStringAsync(cancellationToken)).Trim().TrimStart('v', 'V');
            if (!Version.TryParse(tag, out var version)) throw new InvalidDataException("В VERSION указан неверный номер версии.");
            var normalizedVersion = version.ToString(3);
            var installerName = $"SCNexus-Setup-{normalizedVersion}-win-x64.exe";
            var downloadBase = $"{PublicReleaseBaseUrl}/download/v{normalizedVersion}";
            return new(version,
                $"{PublicReleaseBaseUrl}/tag/v{normalizedVersion}",
                new UpdateAsset(installerName, $"{downloadBase}/{installerName}", 0),
                new UpdateAsset("SHA256SUMS.txt", $"{downloadBase}/SHA256SUMS.txt", 0));
        }

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
            var trustedHost = uri.Host.Equals(apiHost, StringComparison.OrdinalIgnoreCase) ||
                (UsesPublicFeed && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase));
            if (uri.Scheme != Uri.UriSchemeHttps || !trustedHost)
                throw new InvalidDataException("Адрес файла обновления не принадлежит доверенному GitHub-хосту.");
        }
        var checksumUri = new Uri(release.Checksums.ApiUrl);
        var checksumToken = checksumUri.Host.Equals(apiHost, StringComparison.OrdinalIgnoreCase) ? token : null;
        using var checksumResponse = await SendAsync(release.Checksums.ApiUrl, checksumToken, true, HttpCompletionOption.ResponseContentRead, cancellationToken);
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
            var installerUri = new Uri(release.Installer.ApiUrl);
            var installerToken = installerUri.Host.Equals(apiHost, StringComparison.OrdinalIgnoreCase) ? token : null;
            using var installerResponse = await SendAsync(release.Installer.ApiUrl, installerToken, true, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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

    public void PrepareRollback(Version targetVersion, string databasePath, string databaseBackupPath,
        string? executablePath = null)
    {
        executablePath ??= Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath) ||
            !Path.GetExtension(executablePath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Не удалось сохранить текущую версию программы для отката.");
        if (!File.Exists(databaseBackupPath))
            throw new FileNotFoundException("Резервная копия базы перед обновлением не найдена.", databaseBackupPath);

        Directory.CreateDirectory(_stateDirectory);
        var rollbackExecutable = Path.Combine(_stateDirectory,
            $"SCNexus-{CurrentVersion.ToString(3)}-rollback.exe");
        File.Copy(executablePath, rollbackExecutable, true);
        var state = new RollbackState(Path.GetFullPath(executablePath), rollbackExecutable,
            Path.GetFullPath(databasePath), Path.GetFullPath(databaseBackupPath),
            CurrentVersion.ToString(3), targetVersion.ToString(3), DateTimeOffset.UtcNow);
        File.WriteAllText(_rollbackMarkerPath, JsonSerializer.Serialize(state));
    }

    public bool MarkStartupHealthy()
    {
        var state = ReadRollbackState();
        if (state is null || !Version.TryParse(state.TargetVersion, out var target) || CurrentVersion < target)
            return false;
        TryDelete(_rollbackMarkerPath);
        TryDelete(state.RollbackExecutablePath);
        return true;
    }

    public bool TryScheduleRollback()
    {
        var state = ReadRollbackState();
        if (state is null || !Version.TryParse(state.TargetVersion, out var target) || CurrentVersion < target ||
            !File.Exists(state.RollbackExecutablePath) || !File.Exists(state.DatabaseBackupPath)) return false;
        var currentExecutable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExecutable) ||
            !Path.GetFullPath(currentExecutable).Equals(Path.GetFullPath(state.InstalledExecutablePath),
                StringComparison.OrdinalIgnoreCase)) return false;

        Directory.CreateDirectory(_stateDirectory);
        var scriptPath = Path.Combine(_stateDirectory, "restore-previous-version.ps1");
        File.WriteAllText(scriptPath, RollbackScript, new UTF8Encoding(false));
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        foreach (var argument in new[]
        {
            "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", scriptPath,
            "-ParentProcessId", Environment.ProcessId.ToString(),
            "-SourceExecutable", state.RollbackExecutablePath,
            "-DestinationExecutable", state.InstalledExecutablePath,
            "-DatabaseBackup", state.DatabaseBackupPath,
            "-DatabasePath", state.DatabasePath,
            "-MarkerPath", _rollbackMarkerPath
        }) startInfo.ArgumentList.Add(argument);
        Process.Start(startInfo);
        return true;
    }

    private RollbackState? ReadRollbackState()
    {
        if (!File.Exists(_rollbackMarkerPath)) return null;
        try { return JsonSerializer.Deserialize<RollbackState>(File.ReadAllText(_rollbackMarkerPath)); }
        catch (IOException) { return null; }
        catch (JsonException) { return null; }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record RollbackState(string InstalledExecutablePath, string RollbackExecutablePath,
        string DatabasePath, string DatabaseBackupPath, string PreviousVersion, string TargetVersion,
        DateTimeOffset CreatedAt);

    private const string RollbackScript = """
param(
    [int]$ParentProcessId,
    [string]$SourceExecutable,
    [string]$DestinationExecutable,
    [string]$DatabaseBackup,
    [string]$DatabasePath,
    [string]$MarkerPath
)
$ErrorActionPreference = 'Stop'
Wait-Process -Id $ParentProcessId -ErrorAction SilentlyContinue
Remove-Item -LiteralPath ($DatabasePath + '-wal') -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath ($DatabasePath + '-shm') -Force -ErrorAction SilentlyContinue
Copy-Item -LiteralPath $DatabaseBackup -Destination $DatabasePath -Force
Copy-Item -LiteralPath $SourceExecutable -Destination $DestinationExecutable -Force
Remove-Item -LiteralPath $MarkerPath -Force -ErrorAction SilentlyContinue
Start-Process -FilePath $DestinationExecutable -ArgumentList '--rollback-restored'
Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
""";
}
