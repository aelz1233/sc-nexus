using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using SCNexus.Models;

namespace SCNexus.Services;

public sealed class GameLogService(string? gameDirectory = null)
{
    private const int TailBytes = 4 * 1024 * 1024;
    public string GameDirectoryOverride { get; set; } = "";
    public sealed record Snapshot(string? GameDirectory, IReadOnlyList<GameTradeCandidate> Candidates);

    public string? ResolveGameDirectory() =>
        Directory.Exists(GameDirectoryOverride) ? GameDirectoryOverride : gameDirectory ?? FindGameDirectory();

    public Task<Snapshot> ReadRecentAsync(CancellationToken token = default) =>
        Task.Run(() => ReadRecent(token), token);

    private Snapshot ReadRecent(CancellationToken token)
    {
        var directory = ResolveGameDirectory();
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

    public static string? FindGameDirectory()
    {
        foreach (var process in Process.GetProcessesByName("StarCitizen"))
        {
            try
            {
                var binary = process.MainModule?.FileName;
                var install = binary is null ? null : Directory.GetParent(Path.GetDirectoryName(binary)!)?.FullName;
                if (install is not null && Directory.Exists(install)) return install;
            }
            catch (System.ComponentModel.Win32Exception) { }
            catch (InvalidOperationException) { }
        }
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var drive in DriveInfo.GetDrives().Where(x => x.IsReady && x.DriveType == DriveType.Fixed))
        {
            var root = drive.RootDirectory.FullName;
            foreach (var relative in new[]
            {
                @"RSI\StarCitizen", @"StarCitizen", @"Roberts Space Industries\StarCitizen",
                @"Games\StarCitizen", @"Program Files\Roberts Space Industries\StarCitizen",
                @"RSI\Roberts Space Industries\StarCitizen"
            })
            foreach (var channel in new[] { "LIVE", "PTU", "EPTU" }) candidates.Add(Path.Combine(root, relative, channel));
        }
        AddLauncherLibraries(candidates);
        return candidates.Where(IsGameDirectory)
            .OrderByDescending(x => File.Exists(Path.Combine(x, "Game.log")) ? File.GetLastWriteTimeUtc(Path.Combine(x, "Game.log")) : DateTime.MinValue)
            .ThenBy(x => ChannelPriority(new DirectoryInfo(x).Name)).FirstOrDefault();
    }

    private static bool IsGameDirectory(string path) => File.Exists(Path.Combine(path, "Game.log")) ||
        File.Exists(Path.Combine(path, "Bin64", "StarCitizen.exe"));

    private static int ChannelPriority(string channel) => channel.Equals("LIVE", StringComparison.OrdinalIgnoreCase) ? 0 :
        channel.Equals("PTU", StringComparison.OrdinalIgnoreCase) ? 1 : 2;

    private static void AddLauncherLibraries(HashSet<string> candidates)
    {
        var launcher = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "rsilauncher");
        if (!Directory.Exists(launcher)) return;
        try
        {
            foreach (var json in Directory.EnumerateFiles(launcher, "*.json", SearchOption.TopDirectoryOnly))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(json));
                foreach (var path in JsonStrings(document.RootElement).Where(Path.IsPathFullyQualified))
                {
                    foreach (var channel in new[] { "LIVE", "PTU", "EPTU" })
                    {
                        candidates.Add(Path.Combine(path, "StarCitizen", channel));
                        candidates.Add(Path.Combine(path, channel));
                    }
                }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (JsonException) { }
    }

    private static IEnumerable<string> JsonStrings(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 2 } text) yield return text;
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var child in value.EnumerateArray()) foreach (var nested in JsonStrings(child)) yield return nested;
        else if (value.ValueKind == JsonValueKind.Object)
            foreach (var property in value.EnumerateObject()) foreach (var nested in JsonStrings(property.Value)) yield return nested;
    }
}
