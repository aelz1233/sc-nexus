using System.Globalization;
using System.IO;
using SCNexus.Models;

namespace SCNexus.Services;

public sealed class GameLogService(string? gameDirectory = null)
{
    private const int TailBytes = 4 * 1024 * 1024;
    public sealed record Snapshot(string? GameDirectory, IReadOnlyList<GameTradeCandidate> Candidates);

    public Task<Snapshot> ReadRecentAsync(CancellationToken token = default) =>
        Task.Run(() => ReadRecent(token), token);

    private Snapshot ReadRecent(CancellationToken token)
    {
        var directory = gameDirectory ?? FindGameDirectory();
        if (directory is null) return new Snapshot(null, []);
        var logs = new List<string>();
        var liveLog = Path.Combine(directory, "Game.log");
        if (File.Exists(liveLog)) logs.Add(liveLog);
        var backups = Path.Combine(directory, "logbackups");
        if (Directory.Exists(backups))
            logs.AddRange(Directory.EnumerateFiles(backups, "*.log")
                .OrderByDescending(File.GetLastWriteTimeUtc).Take(3));
        var candidates = new List<GameTradeCandidate>();
        foreach (var path in logs)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                foreach (var line in ReadTail(path))
                    if (TryParse(line, out var candidate)) candidates.Add(candidate!);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return new Snapshot(directory, candidates
            .Distinct().OrderByDescending(x => x.TimeUtc).Take(12).ToArray());
    }

    public static bool TryParse(string line, out GameTradeCandidate? candidate)
    {
        candidate = null;
        var buy = line.Contains("Sending SShopCommodityBuyRequest", StringComparison.Ordinal);
        var sell = line.Contains("Sending SShopCommoditySellRequest", StringComparison.Ordinal);
        if (!buy && !sell) return false;
        var timeEnd = line.IndexOf('>');
        if (!line.StartsWith('<') || timeEnd < 2 ||
            !DateTimeOffset.TryParse(line[1..timeEnd], CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var time)) return false;
        var amountText = GetBracket(line, buy ? "price" : "amount");
        if (!decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
            return false;
        var shop = GetBracket(line, "shopName") ?? "Торговый терминал";
        var quantity = GetBracket(line, "quantity") ?? "";
        candidate = new GameTradeCandidate(time, buy, amount, shop, quantity);
        return true;
    }

    private static string? GetBracket(string line, string key)
    {
        var start = line.IndexOf(key + "[", StringComparison.Ordinal);
        if (start < 0) return null;
        start += key.Length + 1;
        var end = line.IndexOf(']', start);
        return end < 0 ? null : line[start..end];
    }

    private static IEnumerable<string> ReadTail(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > TailBytes)
        {
            stream.Seek(-TailBytes, SeekOrigin.End);
            using var reader = new StreamReader(stream);
            reader.ReadLine(); // пропускаем неполную строку после перехода в хвост
            while (reader.ReadLine() is { } line) yield return line;
        }
        else
        {
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line) yield return line;
        }
    }

    private static string? FindGameDirectory()
    {
        foreach (var drive in DriveInfo.GetDrives().Where(x => x.IsReady && x.DriveType == DriveType.Fixed))
        {
            var root = drive.RootDirectory.FullName;
            foreach (var relative in new[]
            {
                @"RSI\StarCitizen\LIVE", @"StarCitizen\LIVE",
                @"Roberts Space Industries\StarCitizen\LIVE",
                @"Games\StarCitizen\LIVE", @"Program Files\Roberts Space Industries\StarCitizen\LIVE"
            })
            {
                var candidate = Path.Combine(root, relative);
                if (File.Exists(Path.Combine(candidate, "Game.log"))) return candidate;
            }
        }
        return null;
    }
}
