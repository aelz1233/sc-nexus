using System.Globalization;
using SCNexus.Services;

namespace SCNexus.Models;

public enum DataSourceKind
{
    GameLog = 1,
    LocalGameData = 2,
    Uex = 3,
    StarCitizenWiki = 4,
    Ocr = 5,
    NexusHistory = 6,
    Manual = 7
}

public sealed record ObservedValue<T>(T Value, DataSourceKind Source, DateTimeOffset Timestamp,
    double Confidence)
{
    public string SourceDisplay => Source switch
    {
        DataSourceKind.GameLog => "Game.log",
        DataSourceKind.LocalGameData => "Local files",
        DataSourceKind.Uex => "UEX",
        DataSourceKind.StarCitizenWiki => "SC Wiki",
        DataSourceKind.Ocr => "OCR",
        DataSourceKind.NexusHistory => "Nexus history",
        _ => "Manual"
    };

    public string AgeDisplay
    {
        get
        {
            var age = DateTimeOffset.Now - Timestamp.ToLocalTime();
            var english = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en";
            if (age < TimeSpan.FromMinutes(1)) return english ? "just now" : "только что";
            if (age < TimeSpan.FromHours(1)) return english ? $"{Math.Max(1, (int)age.TotalMinutes)} min ago" : $"{Math.Max(1, (int)age.TotalMinutes)} мин назад";
            if (age < TimeSpan.FromDays(1)) return english ? $"{Math.Max(1, (int)age.TotalHours)} h ago" : $"{Math.Max(1, (int)age.TotalHours)} ч назад";
            return english ? $"{Math.Max(1, (int)age.TotalDays)} d ago" : $"{Math.Max(1, (int)age.TotalDays)} дн назад";
        }
    }
}

public sealed class PlayerState
{
    public ObservedValue<string>? GameBuild { get; init; }
    public ObservedValue<string>? Environment { get; init; }
    public ObservedValue<string>? CurrentSystem { get; init; }
    public ObservedValue<string>? CurrentLocation { get; init; }
    public ObservedValue<string>? CurrentShip { get; init; }
    public ObservedValue<decimal>? Balance { get; init; }
    public ObservedValue<string>? CurrentLoadout { get; init; }
}

public sealed class GameSession
{
    public required string Id { get; init; }
    public required ObservedValue<DateTimeOffset> StartedAt { get; init; }
    public ObservedValue<DateTimeOffset>? EndedAt { get; init; }
    public ObservedValue<string>? Environment { get; init; }
    public ObservedValue<string>? Build { get; init; }
    public ObservedValue<string>? Shard { get; init; }
}

public sealed class DetectedShip
{
    public required string Id { get; init; }
    public required ObservedValue<string> Name { get; init; }
    public ObservedValue<string>? Role { get; init; }
    public ObservedValue<string>? Loadout { get; init; }
    public bool IsCurrent { get; init; }
}

public sealed class MissionState
{
    public required string Id { get; init; }
    public required ObservedValue<string> Name { get; init; }
    public required ObservedValue<string> Status { get; init; }
    public ObservedValue<string>? Objective { get; init; }
    public string StatusDisplay => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? Status.Value : Status.Value switch
    {
        "active" => "активна", "completed" => "завершена", "failed" => "провалена",
        "abandoned" => "отменена", _ => Status.Value
    };
}

public sealed class TradeEvent
{
    public required string Id { get; init; }
    public required ObservedValue<string> Action { get; init; }
    public ObservedValue<string>? Commodity { get; init; }
    public required ObservedValue<decimal> Amount { get; init; }
    public ObservedValue<decimal>? Quantity { get; init; }
    public ObservedValue<string>? Terminal { get; init; }
}

public sealed class MovementEvent
{
    public required string Id { get; init; }
    public required ObservedValue<string> Location { get; init; }
    public ObservedValue<string>? System { get; init; }
}

public sealed class DeathEvent
{
    public required string Id { get; init; }
    public required ObservedValue<string> Description { get; init; }
    public ObservedValue<string>? Location { get; init; }
}

public sealed class ComponentCatalogSnapshot
{
    public required string ShipName { get; init; }
    public required string GameVersion { get; init; }
    public int Slots { get; init; }
    public int Components { get; init; }
}

public sealed class DataSourceInfo
{
    public required string Name { get; init; }
    public required DataSourceKind Source { get; init; }
    public int Priority { get; init; }
    public DateTimeOffset? LastSuccess { get; init; }
    public string Status { get; init; } = "Waiting";
    public bool IsAvailable { get; init; }
    public string SourceDisplay => Source switch
    {
        DataSourceKind.GameLog => "Game.log", DataSourceKind.LocalGameData => "Local files",
        DataSourceKind.Uex => "UEX", DataSourceKind.StarCitizenWiki => "SC Wiki",
        DataSourceKind.Ocr => "OCR", DataSourceKind.NexusHistory => "Nexus history", _ => "Manual"
    };
    public string NameDisplay
    {
        get
        {
            if (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en") return Name;
            if (Name == "Star Citizen process") return "Процесс Star Citizen";
            return Source switch
            {
                DataSourceKind.GameLog => "Game.log и резервные журналы",
                DataSourceKind.LocalGameData => "Локальные файлы Star Citizen",
                DataSourceKind.Uex => "Рыночные данные UEX",
                DataSourceKind.StarCitizenWiki => "Star Citizen Wiki",
                DataSourceKind.Ocr => "OCR экрана",
                DataSourceKind.NexusHistory => "История Nexus",
                _ => "Сохранённые ручные значения"
            };
        }
    }
    public string StatusDisplay
    {
        get
        {
            if (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en") return Status;
            if (Status == "Waiting") return "Ожидание";
            if (Status == "Updating") return "Обновление";
            if (Status == "Disabled") return "Выключен";
            if (Status == "Game is running") return "Игра запущена";
            if (Status == "Game is not running") return "Игра не запущена";
            if (Status == "Watching for new lines") return "Ожидание новых строк";
            if (Status is "Installation not found" or "Star Citizen installation not found") return "Установка не найдена";
            if (Status == "Network data unavailable") return "Сетевые данные недоступны";
            if (Status == "Read access denied") return "Нет доступа на чтение";
            if (Status == "Cached market data") return "Сохранённые рыночные данные";
            if (Status == "Waiting for current ship") return "Ожидание текущего корабля";
            if (Status == "No manual fallback values") return "Ручные резервные значения не заданы";
            if (Status.StartsWith("Read-only: ")) return Status.Replace("Read-only: ", "Только чтение: ");
            if (Status.StartsWith("Processed ")) return Status.Replace("Processed ", "Обработано ").Replace(" new lines", " новых строк");
            if (Status.StartsWith("Recognized ")) return Status.Replace("Recognized ", "Распознано ").Replace(" values", " значений");
            if (Status.EndsWith(" prices")) return Status.Replace(" prices", " цен");
            if (Status.Contains(" slots for ")) return Status.Replace(" slots for ", " слотов для ");
            if (Status.EndsWith(" fallback values")) return Status.Replace(" fallback values", " резервных значений");
            if (Status.EndsWith(" saved observations")) return Status.Replace(" saved observations", " сохранённых наблюдений");
            return Status;
        }
    }
    public string LastSuccessDisplay
    {
        get
        {
            if (LastSuccess is null) return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en"
                ? "No successful update" : "Успешных обновлений ещё нет";
            return new ObservedValue<string>("", Source, LastSuccess.Value, 1).AgeDisplay;
        }
    }
}

public sealed record DataFieldDisplay(string Label, string Value, string Source, string Age, double Confidence,
    string? DataVersion = null)
{
    public string ValueAndSource => string.IsNullOrWhiteSpace(DataVersion)
        ? $"{Value} • {Source}" : $"{Value} • {Source} • {DataVersion}";
    public string Freshness => $"{Age} • {Confidence:P0}";
}

public sealed class DataCollectionSnapshot
{
    public PlayerState Player { get; init; } = new();
    public IReadOnlyList<GameSession> Sessions { get; init; } = [];
    public IReadOnlyList<DetectedShip> Ships { get; init; } = [];
    public IReadOnlyList<MissionState> Missions { get; init; } = [];
    public IReadOnlyList<TradeEvent> Trades { get; init; } = [];
    public IReadOnlyList<MovementEvent> Movements { get; init; } = [];
    public IReadOnlyList<DeathEvent> Deaths { get; init; } = [];
    public IReadOnlyList<DataSourceInfo> Sources { get; init; } = [];
    public IReadOnlyDictionary<string, ValueObservation> Values { get; init; } = new Dictionary<string, ValueObservation>();
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;

    public IReadOnlyList<DataFieldDisplay> ToDisplayFields(bool english = false)
    {
        var fields = new List<DataFieldDisplay>();
        Add(Player.CurrentShip, english ? "Current ship" : "Текущий корабль", TimeSpan.FromMinutes(30));
        Add(Player.Balance, english ? "Balance" : "Баланс", TimeSpan.FromHours(1), x => x >= 1_000_000
            ? english ? $"{x / 1_000_000:0.#}m aUEC" : $"{x / 1_000_000:0.#} млн aUEC"
            : $"{x:N0} aUEC");
        Add(Player.CurrentLocation, english ? "Location" : "Локация", TimeSpan.FromMinutes(30));
        Add(Player.CurrentSystem, english ? "System" : "Система", TimeSpan.FromMinutes(30));
        Add(Player.Environment, english ? "Environment" : "Контур игры", TimeSpan.FromDays(30));
        Add(Player.GameBuild, english ? "Game build" : "Версия игры", TimeSpan.FromDays(30));
        Add(Player.CurrentLoadout, english ? "Loadout" : "Текущая конфигурация", TimeSpan.FromMinutes(30));
        return fields;

        void Add<T>(ObservedValue<T>? item, string label, TimeSpan freshness, Func<T, string>? format = null)
        {
            if (item is null) return;
            var value = format is null ? Convert.ToString(item.Value, CultureInfo.CurrentCulture) ?? "—" : format(item.Value);
            var age = item.AgeDisplay;
            if (DateTimeOffset.UtcNow - item.Timestamp.ToUniversalTime() > freshness)
                age = (english ? "last known" : "последнее известное") + " · " + age;
            fields.Add(new DataFieldDisplay(label, value, item.SourceDisplay, age, item.Confidence));
        }
    }
}

public sealed class DataObservation
{
    public long Id { get; set; }
    public string Kind { get; set; } = "";
    public string RecordKey { get; set; } = "";
    public string PayloadJson { get; set; } = "";
    public DataSourceKind Source { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public long TimestampUnixMs { get; set; }
    public double Confidence { get; set; }
    public string Fingerprint { get; set; } = "";
}
