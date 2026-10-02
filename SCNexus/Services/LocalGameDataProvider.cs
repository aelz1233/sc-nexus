using System.Diagnostics;
using System.IO;
using System.Text.Json;
using SCNexus.Models;

namespace SCNexus.Services;

public sealed class LocalGameDataProvider(GameLogService gameLogService) : IDataProvider
{
    public string Name => "Local Star Citizen files";
    public DataSourceKind Source => DataSourceKind.LocalGameData;
    public int Priority => 2;
    public TimeSpan RefreshInterval => TimeSpan.FromMinutes(2);

    public Task<DataProviderResult> CollectAsync(DataProviderContext context, CancellationToken token) =>
        Task.Run(() => Collect(context, token), token);

    private DataProviderResult Collect(DataProviderContext context, CancellationToken token)
    {
        var directory = context.GameDirectory ?? gameLogService.ResolveGameDirectory();
        if (directory is null) return new DataProviderResult { Status = "Installation not found" };
        var values = new List<ValueObservation>();
        var sessions = new List<TypedObservation>();
        var now = DateTimeOffset.UtcNow;
        var environment = new DirectoryInfo(directory).Name.ToUpperInvariant();
        if (environment is "LIVE" or "PTU" or "EPTU")
            values.Add(new ValueObservation("game.environment", environment, Source, now, .99));

        var build = ReadBuild(directory);
        if (!string.IsNullOrWhiteSpace(build))
            values.Add(new ValueObservation("game.build", build, Source, now, .96));

        foreach (var path in EnumerateLogs(directory).Take(15))
        {
            token.ThrowIfCancellationRequested();
            var modified = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
            var id = $"{environment}:{Path.GetFileName(path)}";
            var shard = GameMonitorService.ReadLastShard(path);
            sessions.Add(new TypedObservation("session", id, new GameSession
            {
                Id = id,
                StartedAt = new ObservedValue<DateTimeOffset>(new DateTimeOffset(File.GetCreationTimeUtc(path), TimeSpan.Zero), Source, modified, .65),
                EndedAt = path.EndsWith("Game.log", StringComparison.OrdinalIgnoreCase) ? null : new ObservedValue<DateTimeOffset>(modified, Source, modified, .85),
                Environment = new ObservedValue<string>(environment, Source, modified, .99),
                Build = string.IsNullOrWhiteSpace(build) ? null : new ObservedValue<string>(build, Source, modified, .96),
                Shard = shard == "Не определён" ? null : new ObservedValue<string>(shard, Source, modified, .96)
            }, Source, modified, .85));
        }
        return new DataProviderResult { Values = values, Records = sessions, Status = $"Read-only: {environment}" };
    }

    private static string? ReadBuild(string directory)
    {
        foreach (var name in new[] { "build_manifest.id", "build_manifest.json", "BuildManifest.json" })
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path)) continue;
            try
            {
                var content = File.ReadAllText(path).Trim();
                if (content.StartsWith('{'))
                {
                    using var document = JsonDocument.Parse(content);
                    foreach (var key in new[] { "build", "version", "buildId", "build_id" })
                        if (document.RootElement.TryGetProperty(key, out var property)) return property.ToString();
                }
                if (content.Length is > 0 and < 200) return content;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (JsonException) { }
        }
        var executable = Path.Combine(directory, "Bin64", "StarCitizen.exe");
        return File.Exists(executable) ? FileVersionInfo.GetVersionInfo(executable).FileVersion : null;
    }

    private static IEnumerable<string> EnumerateLogs(string directory)
    {
        var current = Path.Combine(directory, "Game.log");
        if (File.Exists(current)) yield return current;
        var backups = Path.Combine(directory, "logbackups");
        if (!Directory.Exists(backups)) yield break;
        foreach (var path in Directory.EnumerateFiles(backups, "*.log").OrderByDescending(File.GetLastWriteTimeUtc)) yield return path;
    }
}
