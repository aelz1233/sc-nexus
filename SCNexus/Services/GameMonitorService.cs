using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace SCNexus.Services;

public static partial class GameMonitorService
{
    public sealed record Snapshot(bool IsRunning, string Shard, string Region, DateTime? LogUpdatedAt);

    public static Snapshot Inspect(string? gameDirectory)
    {
        var running = Process.GetProcessesByName("StarCitizen").Length > 0;
        if (gameDirectory is null) return new Snapshot(running, "Не найден", "Не определён", null);
        var log = Path.Combine(gameDirectory, "Game.log");
        if (!File.Exists(log)) return new Snapshot(running, "Не найден", "Не определён", null);
        var updated = File.GetLastWriteTime(log);
        var shard = ReadLastShard(log);
        var region = RegionFor(shard);
        return new Snapshot(running, shard, region, updated);
    }

    public static string ReadLastShard(string log)
    {
        string shard = "Не определён";
        try
        {
            using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > 1024 * 1024) stream.Seek(-1024 * 1024, SeekOrigin.End);
            using var reader = new StreamReader(stream);
            if (stream.Position > 0) reader.ReadLine();
            while (reader.ReadLine() is { } line)
            {
                var match = ShardPattern().Match(line);
                if (match.Success) shard = match.Value;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return shard;
    }

    public static string RegionFor(string shard) =>
        shard.StartsWith("pub_euw", StringComparison.OrdinalIgnoreCase) ? "Европа · запад" :
            shard.StartsWith("pub_euc", StringComparison.OrdinalIgnoreCase) ? "Европа · центр" :
            shard.StartsWith("pub_use", StringComparison.OrdinalIgnoreCase) ? "США · восток" :
            shard.StartsWith("pub_usw", StringComparison.OrdinalIgnoreCase) ? "США · запад" :
            shard.StartsWith("pub_apse", StringComparison.OrdinalIgnoreCase) ? "Азиатско-Тихоокеанский регион" :
            "Регион не определён";

    [GeneratedRegex(@"pub_[a-z0-9_]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ShardPattern();
}
