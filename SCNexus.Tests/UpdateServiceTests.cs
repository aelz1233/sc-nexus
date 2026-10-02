using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SCNexus.Services;

namespace SCNexus.Tests;

public class UpdateServiceTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(send(request));
    }

    [Fact]
    public async Task PrivateReleaseUsesEncryptedTokenAndVerifiesInstaller()
    {
        var version = new Version(99, 88, 77);
        var name = $"SCNexus-Setup-{version.ToString(3)}-win-x64.exe";
        var bytes = Encoding.UTF8.GetBytes("installer fixture");
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var root = Path.Combine(Path.GetTempPath(), "SCNexusTests", Guid.NewGuid().ToString("N"));
        var tokenPath = Path.Combine(root, "token.bin");
        var release = JsonSerializer.Serialize(new
        {
            tag_name = $"v{version.ToString(3)}",
            html_url = "https://github.com/aelz1233/sc-nexus/releases/tag/test",
            assets = new[]
            {
                new { name, url = "https://api.github.test/installer", size = bytes.Length },
                new { name = "SHA256SUMS.txt", url = "https://api.github.test/checksums", size = 80 }
            }
        });
        using var client = new HttpClient(new Handler(request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("private-token", request.Headers.Authorization?.Parameter);
            var content = request.RequestUri!.AbsolutePath switch
            {
                "/latest" => new StringContent(release, Encoding.UTF8, "application/json"),
                "/checksums" => new ByteArrayContent(Encoding.UTF8.GetBytes($"{hash}  {name}\n")),
                "/installer" => new ByteArrayContent(bytes),
                _ => throw new InvalidOperationException("Unexpected API path")
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }));
        try
        {
            var service = new UpdateService(client, tokenPath, "https://api.github.test/latest");
            service.SaveToken("private-token");
            Assert.True(service.HasToken);
            Assert.DoesNotContain("private-token", Encoding.UTF8.GetString(File.ReadAllBytes(tokenPath)));
            var latest = await service.GetLatestAsync();
            Assert.Equal(version, latest.Version);
            var path = await service.DownloadInstallerAsync(latest);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
            File.Delete(path);
            service.ClearToken();
            Assert.False(service.HasToken);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task RejectsPrivateReleaseWithoutToken()
    {
        using var client = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        var service = new UpdateService(client, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), "https://api.github.test/latest");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetLatestAsync());
        Assert.Contains("токен", error.Message);
    }

    [Fact]
    public async Task PublicReleaseWorksWhenSavedTokenWasRevoked()
    {
        var root = Path.Combine(Path.GetTempPath(), "SCNexusTests", Guid.NewGuid().ToString("N"));
        var name = "SCNexus-Setup-99.88.76-win-x64.exe";
        var bytes = Encoding.UTF8.GetBytes("public installer fixture");
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var release = JsonSerializer.Serialize(new
        {
            tag_name = "v99.88.76", html_url = "https://github.test/release",
            assets = new[]
            {
                new { name, url = "https://api.github.test/installer", size = bytes.Length },
                new { name = "SHA256SUMS.txt", url = "https://api.github.test/checksums", size = 80 }
            }
        });
        var anonymousRequests = 0;
        using var client = new HttpClient(new Handler(request =>
        {
            if (request.Headers.Authorization is not null) return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            anonymousRequests++;
            var content = request.RequestUri!.AbsolutePath switch
            {
                "/latest" => new StringContent(release, Encoding.UTF8, "application/json"),
                "/checksums" => new ByteArrayContent(Encoding.UTF8.GetBytes($"{hash}  {name}\n")),
                "/installer" => new ByteArrayContent(bytes),
                _ => throw new InvalidOperationException()
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }));
        try
        {
            var service = new UpdateService(client, Path.Combine(root, "token.bin"), "https://api.github.test/latest");
            service.SaveToken("revoked-token");
            var latest = await service.GetLatestAsync();
            var path = await service.DownloadInstallerAsync(latest);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
            Assert.Equal(3, anonymousRequests);
            File.Delete(path);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void PreparedRollbackIsClearedOnlyAfterTargetVersionStartsSuccessfully()
    {
        var root = Path.Combine(Path.GetTempPath(), "SCNexusTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var executable = Path.Combine(root, "SCNexus.exe");
        var database = Path.Combine(root, "nexus.db");
        var databaseBackup = Path.Combine(root, "nexus-before-update.db");
        File.WriteAllText(executable, "old executable");
        File.WriteAllText(database, "database");
        File.WriteAllText(databaseBackup, "backup");
        using var client = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        try
        {
            var service = new UpdateService(client, Path.Combine(root, "token.bin"),
                "https://api.github.test/latest", Path.Combine(root, "updates"));
            service.PrepareRollback(new Version(0, 0, 0), database, databaseBackup, executable);

            Assert.True(service.HasPendingRollback);
            Assert.True(service.MarkStartupHealthy());
            Assert.False(service.HasPendingRollback);
            Assert.True(File.Exists(databaseBackup));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
