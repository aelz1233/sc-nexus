using System.IO;
using SCNexus.Models;

namespace SCNexus.Services;

public static class GameSessionService
{
    public static Task<IReadOnlyList<GameSessionSummary>> LoadAsync(string? gameDirectory,
        CancellationToken token = default) => Task.Run(() => Load(gameDirectory, token), token);

    private static IReadOnlyList<GameSessionSummary> Load(string? gameDirectory, CancellationToken token)
    {
        if (gameDirectory is null) return [];
        var logs = new List<string>();
        var active = Path.Combine(gameDirectory, "Game.log");
        if (File.Exists(active)) logs.Add(active);
        var backups = Path.Combine(gameDirectory, "logbackups");
        if (Directory.Exists(backups))
            logs.AddRange(Directory.EnumerateFiles(backups, "*.log")
                .OrderByDescending(File.GetLastWriteTime).Take(15));
        return logs.Select(path =>
        {
            token.ThrowIfCancellationRequested();
            var shard = GameMonitorService.ReadLastShard(path);
            return new GameSessionSummary(Path.GetFileName(path), File.GetLastWriteTime(path),
                shard, GameMonitorService.RegionFor(shard));
        }).OrderByDescending(x => x.LastWriteTime).Take(15).ToArray();
    }
}
