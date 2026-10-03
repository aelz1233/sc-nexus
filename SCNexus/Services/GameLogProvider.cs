using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SCNexus.Models;

namespace SCNexus.Services;

public sealed partial class GameLogProvider(GameLogService gameLogService) : IDataProvider
{
    private const long InitialTailBytes = 4 * 1024 * 1024;
    private readonly Dictionary<string, Cursor> _cursors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MissionState> _missions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GameSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private string? _cachedLogDirectory;
    private string[] _cachedBackupLogs = [];
    private DateTimeOffset _lastBackupScan;
    private string? _lastLocation;

    public string Name => "Game.log + logbackups";
    public DataSourceKind Source => DataSourceKind.GameLog;
    public int Priority => 1;
    public TimeSpan RefreshInterval => TimeSpan.FromSeconds(1);

    public Task<DataProviderResult> CollectAsync(DataProviderContext context, CancellationToken token) =>
        Task.Run(() => Collect(context, token), token);

    private DataProviderResult Collect(DataProviderContext context, CancellationToken token)
    {
        var directory = context.GameDirectory ?? gameLogService.ResolveGameDirectory();
        if (directory is null) return new DataProviderResult { Status = "Star Citizen installation not found" };
        var paths = EnumerateLogs(directory).ToArray();
        var currentPaths = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var stale in _cursors.Keys.Where(x => !currentPaths.Contains(x)).ToArray()) _cursors.Remove(stale);
        var values = new List<ValueObservation>();
        var records = new List<TypedObservation>();
        var readLines = 0;
        foreach (var path in paths)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                foreach (var entry in ReadNewLines(path, path.EndsWith("Game.log", StringComparison.OrdinalIgnoreCase)))
                {
                    readLines++;
                    var logIdentity = $"{Path.GetFileName(path)}:{File.GetCreationTimeUtc(path).Ticks}:{entry.Generation}";
                    ParseLine(entry.Line, logIdentity, values, records);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        PruneParserState();
        return new DataProviderResult
        {
            Values = values, Records = records,
            Status = readLines == 0 ? "Watching for new lines" : $"Processed {readLines} new lines"
        };
    }

    private void PruneParserState()
    {
        if (_missions.Count > 500)
            foreach (var key in _missions.OrderByDescending(x => x.Value.Status.Timestamp).Skip(500).Select(x => x.Key).ToArray())
                _missions.Remove(key);
        if (_sessions.Count > 100)
            foreach (var key in _sessions.OrderByDescending(x => x.Value.StartedAt.Timestamp).Skip(100).Select(x => x.Key).ToArray())
                _sessions.Remove(key);
    }

    private IEnumerable<string> EnumerateLogs(string directory)
    {
        var active = Path.Combine(directory, "Game.log");
        if (File.Exists(active)) yield return active;
        var backups = Path.Combine(directory, "logbackups");
        if (!Directory.Exists(backups)) yield break;
        var now = DateTimeOffset.UtcNow;
        if (!string.Equals(_cachedLogDirectory, directory, StringComparison.OrdinalIgnoreCase) ||
            now - _lastBackupScan >= TimeSpan.FromMinutes(1))
        {
            _cachedLogDirectory = directory;
            _lastBackupScan = now;
            _cachedBackupLogs = Directory.EnumerateFiles(backups, "*.log")
                .OrderByDescending(File.GetLastWriteTimeUtc).Take(20).ToArray();
        }
        foreach (var path in _cachedBackupLogs)
            yield return path;
    }

    private IEnumerable<LogLine> ReadNewLines(string path, bool active)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        var creation = File.GetCreationTimeUtc(path);
        var skipPartialFirstLine = false;
        if (!_cursors.TryGetValue(path, out var cursor))
        {
            var start = Math.Max(0, stream.Length - InitialTailBytes);
            cursor = new Cursor(start, "", creation, 0);
            _cursors[path] = cursor;
            skipPartialFirstLine = start > 0;
        }
        if (stream.Length < cursor.Offset || cursor.CreationUtc != creation)
            cursor = new Cursor(0, "", creation, cursor.Generation + 1);
        stream.Position = cursor.Offset;
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, true);
        var chunk = cursor.Partial + reader.ReadToEnd();
        if (skipPartialFirstLine)
        {
            var firstBreak = chunk.IndexOfAny(['\r', '\n']);
            chunk = firstBreak < 0 ? "" : chunk[(firstBreak + 1)..];
        }
        var endsWithLineBreak = chunk.EndsWith('\n') || chunk.EndsWith('\r');
        var lines = chunk.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var complete = endsWithLineBreak ? lines.Length : Math.Max(0, lines.Length - 1);
        for (var i = 0; i < complete; i++) yield return new LogLine(lines[i], cursor.Generation);
        var partial = endsWithLineBreak || lines.Length == 0 ? "" : lines[^1];
        _cursors[path] = new Cursor(stream.Position, partial, creation, cursor.Generation);
        if (!active && string.IsNullOrEmpty(partial)) _cursors[path] = new Cursor(stream.Position, "", creation, cursor.Generation);
    }

    private void ParseLine(string line, string logIdentity, List<ValueObservation> values,
        List<TypedObservation> records)
    {
        var timestamp = ReadTimestamp(line) ?? DateTimeOffset.UtcNow;
        if (GameLogService.TryParse(line, out var candidate) && candidate is not null)
        {
            var action = candidate.IsPurchase ? "purchase" : "sale";
            var id = Id("trade", candidate.TimeUtc, action, candidate.ShopName, candidate.Amount.ToString(CultureInfo.InvariantCulture));
            var source = new ObservedValue<string>(action, Source, candidate.TimeUtc, .98);
            records.Add(new TypedObservation("trade", id, new TradeEvent
            {
                Id = id, Action = source,
                Amount = new ObservedValue<decimal>(candidate.Amount, Source, candidate.TimeUtc, .98),
                Terminal = new ObservedValue<string>(candidate.ShopName, Source, candidate.TimeUtc, .9),
                Quantity = ReadQuantity(candidate.QuantityText, candidate.TimeUtc)
            }, Source, candidate.TimeUtc, .98));
        }

        AddBracketValue("player.balance", "balance", line, timestamp, values, .92);
        AddBracketValue("game.build", "build", line, timestamp, values, .92);
        var currency = CurrencyPattern().Match(line);
        if (currency.Success && BalanceText.TryParse(currency.Groups["value"].Value, out var balanceAmount))
            values.Add(new ValueObservation("player.balance", balanceAmount.ToString(CultureInfo.InvariantCulture), Source, timestamp, .95, "aUEC"));
        var build = BuildPattern().Match(line);
        if (build.Success)
            values.Add(new ValueObservation("game.build", build.Groups["value"].Value, Source, timestamp, .94));

        var shard = ShardPattern().Match(line);
        if (shard.Success)
        {
            values.Add(new ValueObservation("game.shard", shard.Value, Source, timestamp, .98));
            var sessionId = Id("session", DateTimeOffset.UnixEpoch, logIdentity, shard.Value);
            if (!_sessions.TryGetValue(sessionId, out var session))
            {
                session = new GameSession
                {
                    Id = sessionId,
                    StartedAt = new ObservedValue<DateTimeOffset>(timestamp, Source, timestamp, .8),
                    Shard = new ObservedValue<string>(shard.Value, Source, timestamp, .98)
                };
            }
            else if (timestamp < session.StartedAt.Value)
            {
                session = new GameSession
                {
                    Id = session.Id,
                    StartedAt = new ObservedValue<DateTimeOffset>(timestamp, Source, timestamp, .8),
                    Shard = session.Shard
                };
            }
            _sessions[sessionId] = session;
            records.Add(new TypedObservation("session", sessionId, session, Source, timestamp, .9));
        }

        var location = FirstGroup(LocationPatterns(), line);
        if (!string.IsNullOrWhiteSpace(location) && IsUsefulName(location))
        {
            location = NormalizeLocation(location);
            values.Add(new ValueObservation("player.location", location, Source, timestamp, .82));
            var system = KnownSystem(location);
            if (system is not null) values.Add(new ValueObservation("player.system", system, Source, timestamp, .9));
            if (!location.Equals(_lastLocation, StringComparison.OrdinalIgnoreCase))
            {
                _lastLocation = location;
                var movementId = Id("movement", timestamp, location);
                records.Add(new TypedObservation("movement", movementId, new MovementEvent
                {
                    Id = movementId,
                    Location = new ObservedValue<string>(location, Source, timestamp, .82),
                    System = system is null ? null : new ObservedValue<string>(system, Source, timestamp, .9)
                }, Source, timestamp, .82));
            }
        }

        var ship = FirstGroup(ShipPatterns(), line);
        if (!string.IsNullOrWhiteSpace(ship) && IsUsefulName(ship))
        {
            values.Add(new ValueObservation("player.ship", ship, Source, timestamp, .82));
            var shipId = Id("ship", DateTimeOffset.UnixEpoch, ship);
            records.Add(new TypedObservation("ship", shipId, new DetectedShip
            {
                Id = shipId, Name = new ObservedValue<string>(ship, Source, timestamp, .82), IsCurrent = true
            }, Source, timestamp, .82));
        }

        var mission = MissionPattern().Match(line);
        if (mission.Success)
        {
            var name = mission.Groups["name"].Value.Trim();
            var status = mission.Groups["status"].Value.Trim();
            if (IsUsefulName(name) && IsUsefulName(status))
            {
                var missionId = Id("mission", DateTimeOffset.UnixEpoch, name);
                records.Add(new TypedObservation("mission", missionId, new MissionState
                {
                    Id = missionId,
                    Name = new ObservedValue<string>(name, Source, timestamp, .72),
                    Status = new ObservedValue<string>(status, Source, timestamp, .75),
                    Objective = ReadGroup(mission, "objective") is { Length: > 0 } objective
                        ? new ObservedValue<string>(objective, Source, timestamp, .7) : null
                }, Source, timestamp, .73));
            }
        }
        ParseStructuredMission(line, timestamp, records);

        if (DeathPattern().IsMatch(line))
        {
            var description = StripPrefix(line);
            var deathId = Id("death", timestamp, description);
            records.Add(new TypedObservation("death", deathId, new DeathEvent
            {
                Id = deathId,
                Description = new ObservedValue<string>(description, Source, timestamp, .7),
                Location = _lastLocation is null ? null : new ObservedValue<string>(_lastLocation, Source, timestamp, .65)
            }, Source, timestamp, .7));
        }
    }

    private void ParseStructuredMission(string line, DateTimeOffset timestamp, List<TypedObservation> records)
    {
        var marker = MissionMarkerPattern().Match(line);
        if (marker.Success)
        {
            var id = marker.Groups["id"].Value;
            var name = marker.Groups["name"].Value;
            var objective = marker.Groups["objective"].Value;
            var state = new MissionState
            {
                Id = id, Name = new ObservedValue<string>(name, Source, timestamp, .9),
                Status = new ObservedValue<string>("active", Source, timestamp, .9),
                Objective = string.IsNullOrWhiteSpace(objective) ? null : new ObservedValue<string>(objective, Source, timestamp, .82)
            };
            _missions[id] = state;
            records.Add(new TypedObservation("mission", id, state, Source, timestamp, .9));
        }
        var objectiveEvent = ObjectivePattern().Match(line);
        if (objectiveEvent.Success)
        {
            var id = objectiveEvent.Groups["id"].Value;
            var status = CleanMissionState(objectiveEvent.Groups["state"].Value);
            var objective = objectiveEvent.Groups["objective"].Value;
            var previous = _missions.GetValueOrDefault(id);
            // An objective's completion/failure does not end the whole contract.
            var missionStatus = previous?.Status.Value ??
                (status is "active" or "in_progress" ? "active" : "unknown");
            var state = new MissionState
            {
                Id = id,
                Name = previous?.Name ?? new ObservedValue<string>($"Mission {id[..8]}", Source, timestamp, .65),
                Status = new ObservedValue<string>(missionStatus, Source, timestamp, .95),
                Objective = new ObservedValue<string>(objective, Source, timestamp, .85)
            };
            _missions[id] = state;
            records.Add(new TypedObservation("mission", id, state, Source, timestamp, .9));
        }
        var ended = MissionEndedPattern().Match(line);
        if (ended.Success)
        {
            var id = ended.Groups["id"].Value;
            var status = CleanMissionState(ended.Groups["state"].Value);
            var previous = _missions.GetValueOrDefault(id);
            var state = new MissionState
            {
                Id = id,
                Name = previous?.Name ?? new ObservedValue<string>($"Mission {id[..8]}", Source, timestamp, .65),
                Status = new ObservedValue<string>(status, Source, timestamp, .99),
                Objective = previous?.Objective
            };
            _missions[id] = state;
            records.Add(new TypedObservation("mission", id, state, Source, timestamp, .96));
        }
    }

    private static ObservedValue<decimal>? ReadQuantity(string text, DateTimeOffset timestamp)
    {
        var number = Regex.Match(text ?? "", @"[0-9]+(?:\.[0-9]+)?", RegexOptions.CultureInvariant).Value;
        return decimal.TryParse(number, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? new ObservedValue<decimal>(value, DataSourceKind.GameLog, timestamp, .8) : null;
    }

    private static void AddBracketValue(string key, string bracket, string line, DateTimeOffset timestamp,
        List<ValueObservation> values, double confidence)
    {
        var match = Regex.Match(line, $@"\b{Regex.Escape(bracket)}\[(?<value>[^\]]+)\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (match.Success && IsUsefulName(match.Groups["value"].Value))
        {
            var value = match.Groups["value"].Value.Trim();
            if (key == "player.balance")
            {
                if (!BalanceText.TryParse(value, out var amount)) return;
                value = amount.ToString(CultureInfo.InvariantCulture);
            }
            values.Add(new ValueObservation(key, value, DataSourceKind.GameLog, timestamp, confidence));
        }
    }

    private static DateTimeOffset? ReadTimestamp(string line)
    {
        var end = line.IndexOf('>');
        return line.StartsWith('<') && end > 1 && DateTimeOffset.TryParse(line[1..end], CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var value) ? value : null;
    }

    private static string? FirstGroup(IEnumerable<Regex> patterns, string line)
    {
        foreach (var pattern in patterns)
        {
            var match = pattern.Match(line);
            if (match.Success) return match.Groups["value"].Value.Trim();
        }
        return null;
    }

    private static string? KnownSystem(string value) =>
        new[] { "Stanton", "Pyro", "Nyx" }.FirstOrDefault(x => value.Contains(x, StringComparison.OrdinalIgnoreCase)) ??
        (value is "Orison" or "GrimHEX" || value.StartsWith("RR_", StringComparison.OrdinalIgnoreCase) ? "Stanton" : null);
    private static string NormalizeLocation(string value)
    {
        value = value.Trim();
        if (value.StartsWith("Stanton2_", StringComparison.OrdinalIgnoreCase)) return value[9..].Replace('_', ' ');
        if (value.StartsWith("Stanton", StringComparison.OrdinalIgnoreCase) && value.Contains('_')) return value[(value.IndexOf('_') + 1)..].Replace('_', ' ');
        return value.Replace('_', ' ');
    }
    private static string CleanMissionState(string value) => value
        .Replace("MISSION_OBJECTIVE_STATE_", "", StringComparison.OrdinalIgnoreCase)
        .Replace("MISSION_STATE_", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
    private static bool IsUsefulName(string value) => value.Length is > 1 and < 180 && value != "0" && value != "None";
    private static string StripPrefix(string line) => line[(Math.Min(line.Length, Math.Max(0, line.IndexOf('>') + 1)))..].Trim();
    private static string ReadGroup(Match match, string name) => match.Groups[name].Success ? match.Groups[name].Value.Trim() : "";
    private static string Id(string type, DateTimeOffset timestamp, params object[] parts)
    {
        var raw = type + "|" + timestamp.UtcTicks + "|" + string.Join('|', parts);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..24];
    }

    private sealed record Cursor(long Offset, string Partial, DateTime CreationUtc, int Generation);
    private sealed record LogLine(string Line, int Generation);
    private static IEnumerable<Regex> LocationPatterns() => [InventoryLocationPattern(), LocationBracketPattern(), ZoneBracketPattern(), LocationJsonPattern()];
    private static IEnumerable<Regex> ShipPatterns() => [VehicleNamePattern(), ShipNamePattern(), VehicleJsonPattern()];

    [GeneratedRegex(@"pub_[a-z0-9_]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex ShardPattern();
    [GeneratedRegex(@"\blocation\[(?<value>[^\]]+)\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex LocationBracketPattern();
    [GeneratedRegex(@"<RequestLocationInventory>.*?Location\[(?<value>[^\]]+)\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex InventoryLocationPattern();
    [GeneratedRegex(@"\bzone\[(?<value>[^\]]+)\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex ZoneBracketPattern();
    [GeneratedRegex("\\\"location\\\"\\s*:\\s*\\\"(?<value>[^\\\"]+)\\\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex LocationJsonPattern();
    [GeneratedRegex(@"\bvehicleName\[(?<value>[^\]]+)\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex VehicleNamePattern();
    [GeneratedRegex(@"\bship(?:Name)?\[(?<value>[^\]]+)\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex ShipNamePattern();
    [GeneratedRegex("\\\"vehicle(?:Name)?\\\"\\s*:\\s*\\\"(?<value>[^\\\"]+)\\\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex VehicleJsonPattern();
    [GeneratedRegex(@"mission(?:Name)?\[(?<name>[^\]]+)\].*?(?:mission)?status\[(?<status>[^\]]+)\](?:.*?objective\[(?<objective>[^\]]+)\])?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex MissionPattern();
    [GeneratedRegex(@"Creating objective marker:\s*missionId \[(?<id>[0-9a-f-]{36})\].*?generator name \[(?<objective>[^\]]+)\].*?contract \[(?<name>[^\]]+)\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex MissionMarkerPattern();
    [GeneratedRegex(@"<ObjectiveUpserted>.*?mission_id (?<id>[0-9a-f-]{36})\s+-\s+objective_id (?<objective>[^\s]+)\s+-\s+state (?<state>[A-Z_]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex ObjectivePattern();
    [GeneratedRegex(@"<MissionEnded>.*?mission_id (?<id>[0-9a-f-]{36})\s+-\s+mission_state (?<state>[A-Z_]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex MissionEndedPattern();
    [GeneratedRegex(@"(?:\bBalance|\bWallet|Баланс|Кошел[её]к)[ \t]*[:=][ \t]*(?<value>[0-9][0-9, .]*)[ \t]*aUEC\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex CurrencyPattern();
    [GeneratedRegex(@"Game Version Identifier:\s*(?<value>[a-z0-9-]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex BuildPattern();
    [GeneratedRegex(@"(<Actor Death>|CActor::Kill:.*(?:killed|destroyed)|player.*(?:died|dead))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex DeathPattern();
}
