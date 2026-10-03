using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCNexus.Models;
using SCNexus.Services;

namespace SCNexus.ViewModels;

public partial class MainViewModel
{
    private readonly HashSet<string> _fleetSyncPending = new(StringComparer.OrdinalIgnoreCase);
    private DataCollectionSnapshot _lastDataSnapshot = new();
    private ObservedValue<decimal>? _lastAppliedBalance;
    private ObservedValue<string>? _lastAppliedLocation;
    private ObservedValue<string>? _lastAppliedSystem;
    private ObservedValue<string>? _lastAppliedShip;
    [ObservableProperty] private bool ocrEnabled;
    [ObservableProperty] private string dataCollectionStatus = "Источники данных запускаются…";
    [ObservableProperty] private bool needsManualGamePath = true;
    [ObservableProperty] private string language = "ru";
    [ObservableProperty] private string shipDetectionStatus = "";

    public bool IsEnglish => Language == "en";
    public string OcrShipDetectionTitle => IsEnglish ? "OCR ship detection" : "OCR и обнаружение корабля";
    public string OcrShipDetectionGuide => IsEnglish
        ? "Open ASOP, Fleet Manager or Vehicle Loadout with readable model names. Press Force ship detection, then switch to Star Citizen within 3 seconds. Only the active game window is scanned. Catalog matches are added to the detected fleet; the current ship is changed only with an explicit Current Ship label. OCR never controls the game."
        : "Открой ASOP, «Мой флот» или Vehicle Loadout с читаемыми названиями моделей. Нажми «Принудительно обнаружить корабль» и за 3 секунды переключись в Star Citizen. Сканируется только активное окно игры. Модели из каталога добавляются в обнаруженный флот; текущий корабль меняется только при явной подписи Current Ship. OCR не управляет игрой.";
    public string OverlayDetectShipButtonText => IsEnglish ? "Force ship detection" : "Принудительно обнаружить корабль";
    public string[] Languages { get; } = ["Русский", "English"];
    public string SelectedLanguage
    {
        get => IsEnglish ? "English" : "Русский";
        set => Language = value == "English" ? "en" : "ru";
    }

    public ObservableCollection<DataFieldDisplay> AutomaticDataFields { get; } = [];
    public ObservableCollection<DataSourceInfo> DataSources { get; } = [];
    public ObservableCollection<MissionState> DetectedMissions { get; } = [];
    public int DetectedDeaths { get; private set; }
    public int DetectedMovements { get; private set; }
    public string LiveLocationDisplay => _lastDataSnapshot.Player.CurrentLocation is { } location
        ? $"{location.Value} • {location.SourceDisplay} • {location.AgeDisplay}"
        : IsEnglish ? "Location has not been detected yet" : "Локация пока не определена";
    public string LiveServerDisplay => GameShard is "Не определён" or "Unknown"
        ? (IsEnglish ? "Join the universe to detect the server" : "Зайди во вселенную, чтобы определить сервер")
        : $"{GameRegion} • {GameShard}";
    public string LiveMissionDisplay
    {
        get
        {
            var mission = _lastDataSnapshot.Missions.FirstOrDefault(x =>
                x.Status.Value.Equals("active", StringComparison.OrdinalIgnoreCase));
            return mission is null ? (IsEnglish ? "No active mission detected" : "Активная миссия не обнаружена")
                : $"{mission.Name.Value} • {mission.StatusDisplay} • {mission.Status.AgeDisplay}";
        }
    }

    private void OnDataSnapshotUpdated(object? sender, DataCollectionSnapshot snapshot)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => ApplyDataSnapshot(snapshot));
            return;
        }
        ApplyDataSnapshot(snapshot);
    }

    private void ApplyDataSnapshot(DataCollectionSnapshot snapshot)
    {
        _lastDataSnapshot = snapshot;
        AutomaticDataFields.Clear();
        foreach (var field in snapshot.ToDisplayFields(IsEnglish)) AutomaticDataFields.Add(field);
        if (snapshot.Values.TryGetValue("market.prices", out var market))
            AutomaticDataFields.Add(new DataFieldDisplay(IsEnglish ? "Market prices" : "Рыночные цены",
                IsEnglish ? market.Value : market.Value.Replace(" quotes", " котировок"),
                SourceName(market.Source), Age(market.Timestamp), market.Confidence, market.DataVersion));
        if (snapshot.Values.TryGetValue("ship.components", out var components))
            AutomaticDataFields.Add(new DataFieldDisplay(IsEnglish ? "Ship components" : "Компоненты корабля",
                IsEnglish ? components.Value : components.Value.Replace(" components", " компонентов"),
                SourceName(components.Source), Age(components.Timestamp), components.Confidence, components.DataVersion));

        DataSources.Clear();
        foreach (var source in snapshot.Sources.OrderBy(x => x.Priority)) DataSources.Add(source);
        DetectedMissions.Clear();
        foreach (var mission in snapshot.Missions.Take(30)) DetectedMissions.Add(mission);
        _ = SyncDetectedFleetAsync(snapshot.Ships);
        DetectedDeaths = snapshot.Deaths.Count;
        DetectedMovements = snapshot.Movements.Count;
        OnPropertyChanged(nameof(DetectedDeaths));
        OnPropertyChanged(nameof(DetectedMovements));
        var liveTrades = snapshot.Trades.Select(x => new GameTradeCandidate(x.Amount.Timestamp,
            x.Action.Value.Equals("purchase", StringComparison.OrdinalIgnoreCase), x.Amount.Value,
            x.Terminal?.Value ?? (IsEnglish ? "Trade terminal" : "Торговый терминал"),
            x.Quantity is null ? "" : x.Quantity.Value.ToString("N0"))).OrderByDescending(x => x.TimeUtc).Take(50).ToArray();
        if (!GameTrades.SequenceEqual(liveTrades))
        {
            GameTrades.Clear();
            foreach (var trade in liveTrades) GameTrades.Add(trade);
            ApplyGameTradesToFlight(onlyNew: true);
        }

        var gameDirectory = gameLogService.ResolveGameDirectory();
        ResolvedGameDirectory = gameDirectory ?? (IsEnglish
            ? "Game not found — choose its folder manually"
            : "Игра не найдена — выбери папку вручную");
        var isRunning = snapshot.Values.TryGetValue("game.running", out var runningValue)
            ? bool.TryParse(runningValue.Value, out var parsedRunning) && parsedRunning
            : IsStarCitizenRunning();
        IsGameRunning = isRunning;
        UpdateSessionInsights(snapshot, isRunning);
        GameProcessStatus = isRunning ? (IsEnglish ? "Game is running" : "Игра запущена")
            : (IsEnglish ? "Game is not running" : "Игра не запущена");
        if (snapshot.Values.TryGetValue("game.shard", out var shard))
        {
            GameShard = shard.Value;
            GameRegion = GameMonitorService.RegionFor(shard.Value);
        }
        if (gameDirectory is not null)
        {
            var log = Path.Combine(gameDirectory, "Game.log");
            GameLogUpdated = File.Exists(log) ? File.GetLastWriteTime(log).ToString("dd.MM.yyyy HH:mm:ss")
                : (IsEnglish ? "No data" : "Нет данных");
            GameLogStatus = File.Exists(log)
                ? $"Game.log: {gameDirectory} · {(IsEnglish ? "events" : "событий")}: {snapshot.Trades.Count + snapshot.Movements.Count + snapshot.Deaths.Count}"
                : IsEnglish ? $"Game found: {gameDirectory}; Game.log will appear after launch."
                    : $"Игра найдена: {gameDirectory} · Game.log появится после запуска игры.";
        }
        else GameLogStatus = IsEnglish ? "Star Citizen installation was not found."
            : "Установка Star Citizen не найдена.";

        var sessions = snapshot.Sessions.OrderByDescending(x => x.StartedAt.Value).Take(30)
            .Select(x => new GameSessionSummary(
                string.Join(" · ", new[] { x.Environment?.Value, x.Build?.Value }.Where(v => !string.IsNullOrWhiteSpace(v))),
                (x.EndedAt?.Value ?? x.StartedAt.Value).LocalDateTime,
                x.Shard?.Value ?? (IsEnglish ? "Unknown" : "Не определён"),
                x.Shard is null ? (IsEnglish ? "Unknown region" : "Регион не определён") : GameMonitorService.RegionFor(x.Shard.Value)))
            .ToArray();
        if (!GameSessions.SequenceEqual(sessions))
        {
            GameSessions.Clear();
            foreach (var session in sessions) GameSessions.Add(session);
        }
        SessionStatus = sessions.Length == 0
            ? (IsEnglish ? "No game sessions detected yet" : "Игровые сессии пока не найдены")
            : IsEnglish ? $"Sessions found: {sessions.Length}" : $"Найдено сессий: {sessions.Length}";

        var player = snapshot.Player;
        if (player.CurrentLocation is { Confidence: >= .75 } location && location.Source != DataSourceKind.Manual &&
            location != _lastAppliedLocation && IsFresh(location, TimeSpan.FromMinutes(30)))
        {
            _lastAppliedLocation = location;
            CurrentLocation = location.Value;
            LocationQuery = location.Value;
        }
        if (player.CurrentSystem is { Confidence: >= .75 } system && system.Source != DataSourceKind.Manual &&
            system != _lastAppliedSystem && IsFresh(system, TimeSpan.FromMinutes(30)))
        {
            _lastAppliedSystem = system;
            CurrentSystem = system.Value;
        }
        if (player.CurrentShip is { Confidence: >= .8 } ship && ship.Source != DataSourceKind.Manual &&
            ship != _lastAppliedShip && IsFresh(ship, TimeSpan.FromMinutes(30)) && Ships.Any(x => x.Name.Equals(ship.Value, StringComparison.OrdinalIgnoreCase)))
        {
            _lastAppliedShip = ship;
            SelectDetectedShip(ship.Value);
        }
        if (player.Balance is { Confidence: >= .9 } balance && balance.Value >= 0 &&
            balance.Source is not (DataSourceKind.Manual or DataSourceKind.NexusHistory) &&
            IsFresh(balance, TimeSpan.FromHours(1)) && balance != _lastAppliedBalance)
        {
            _lastAppliedBalance = balance;
            Balance = balance.Value;
        }
        if (player.Environment is { } environment)
            DataCollectionStatus = IsEnglish
                ? $"{environment.Value} • {snapshot.Sources.Count(x => x.IsAvailable)}/{snapshot.Sources.Count} sources"
                : $"{environment.Value} • {snapshot.Sources.Count(x => x.IsAvailable)}/{snapshot.Sources.Count} источников";
        else DataCollectionStatus = IsEnglish
            ? $"{snapshot.Sources.Count(x => x.IsAvailable)}/{snapshot.Sources.Count} sources available"
            : $"{snapshot.Sources.Count(x => x.IsAvailable)}/{snapshot.Sources.Count} источников доступны";
        NeedsManualGamePath = !Directory.Exists(gameLogService.ResolveGameDirectory());
        UpdateVoyageProgress(snapshot);
        NotifyOverlayChanged();
        OnPropertyChanged(nameof(LiveLocationDisplay));
        OnPropertyChanged(nameof(LiveServerDisplay));
        OnPropertyChanged(nameof(LiveMissionDisplay));
    }

    private static bool IsFresh<T>(ObservedValue<T> value, TimeSpan maximumAge) =>
        DateTimeOffset.UtcNow - value.Timestamp.ToUniversalTime() <= maximumAge;

    private static bool IsStarCitizenRunning()
    {
        var processes = Process.GetProcessesByName("StarCitizen");
        try { return processes.Length > 0; }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    private void SelectDetectedShip(string name)
    {
        var match = Ships.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (match is null) return;
        SelectedShip = match;
        CurrentShip = match.Name;
        CargoScu = match.Ship.CargoScu;
    }

    private async Task SyncDetectedFleetAsync(IEnumerable<DetectedShip> detectedShips)
    {
        foreach (var detected in detectedShips.Where(x => x.Name.Confidence >= .8))
        {
            var raw = detected.Name.Value.Trim();
            if (Ships.Any(x => x.Name.Equals(raw, StringComparison.OrdinalIgnoreCase)) || !_fleetSyncPending.Add(raw)) continue;
            try
            {
                var normalized = raw.Replace('_', ' ').Replace('-', ' ');
                var matches = _allVehicles.Where(x => x.IsSpaceship == 1 &&
                    (x.Name.Equals(raw, StringComparison.OrdinalIgnoreCase) ||
                     x.NameFull.Equals(raw, StringComparison.OrdinalIgnoreCase) ||
                     x.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
                     normalized.EndsWith(x.Name, StringComparison.OrdinalIgnoreCase))).ToArray();
                if (matches.Length != 1) continue;
                var vehicle = matches[0];
                if (Ships.Any(x => x.Name.Equals(vehicle.Name, StringComparison.OrdinalIgnoreCase))) continue;
                await flightLogService.AddShipAsync(vehicle.Name, (int)Math.Max(0, vehicle.Scu), VehicleCatalog.InferRole(vehicle),
                    (IsEnglish ? "Detected automatically: " : "Обнаружен автоматически: ") + SourceName(detected.Name.Source));
                await ReloadFlightLogAsync();
                if (detected.IsCurrent) SelectDetectedShip(vehicle.Name);
                WorkspaceStatus = IsEnglish ? $"Added detected ship: {vehicle.Name}" : $"Обнаруженный корабль добавлен во флот: {vehicle.Name}";
                RaiseNotification($"ship:{vehicle.Name}", IsEnglish ? "Ship detected" : "Обнаружен корабль",
                    vehicle.Name, NexusNotificationKind.Success, TimeSpan.FromHours(1));
            }
            catch (Exception ex) { WorkspaceStatus = ex.Message; }
            finally { _fleetSyncPending.Remove(raw); }
        }
    }

    [RelayCommand]
    private async Task RefreshAutomaticDataAsync()
    {
        if (dataCollectionService is null) return;
        DataCollectionStatus = IsEnglish ? "Refreshing all data sources…" : "Обновляю все источники…";
        await dataCollectionService.RefreshAsync(OcrEnabled);
    }

    [ObservableProperty] private bool isShipDetectionCapturing;

    [RelayCommand]
    private async Task DetectShipFromOverlayAsync()
    {
        if (dataCollectionService is null)
        {
            ShipDetectionStatus = IsEnglish
                ? "Automatic data collection is not available."
                : "Автоматический сбор данных недоступен.";
            return;
        }

        ShipDetectionStatus = IsEnglish ? "Switch to Star Citizen. Scanning in 3 seconds…" : "Переключись в Star Citizen. Сканирование через 3 секунды…";
        DataProviderResult? result;
        try
        {
            await Task.Delay(3000);
            IsShipDetectionCapturing = true;
            await Task.Delay(150);
            result = await dataCollectionService.RefreshOcrAsync();
        }
        catch (Exception ex)
        {
            AppLogService.Write("Manual ship detection failed", ex);
            ShipDetectionStatus = IsEnglish ? "Scan failed. Check the data source status." : "Не удалось выполнить проверку. Проверь состояние источников данных.";
            return;
        }
        finally { IsShipDetectionCapturing = false; }
        var snapshot = dataCollectionService.Current;
        ApplyDataSnapshot(snapshot);
        var ocrStatus = result?.Status ?? "Waiting";
        var ship = result?.Values.FirstOrDefault(x => x.Key == "player.ship");
        if (ship is not null)
        {
            ShipDetectionStatus = IsEnglish ? $"Ship detected: {ship.Value}." : $"Корабль определён: {ship.Value}.";
            return;
        }

        var fleet = result?.Records.Select(x => x.Value).OfType<DetectedShip>().Take(3).Select(x => x.Name.Value).ToArray() ?? [];
        if (fleet.Length > 0)
        {
            var names = string.Join(", ", fleet);
            ShipDetectionStatus = IsEnglish ? $"Detected ship entries: {names}." : $"Найдены корабли: {names}.";
            return;
        }

        ShipDetectionStatus = result is null
            ? (IsEnglish ? "Scan unavailable. Check OCR source status and try again." : "Проверка недоступна. Проверь состояние OCR и повтори.")
            : ocrStatus switch
            {
                "Waiting for Star Citizen foreground window" => IsEnglish
                    ? "Switch to Star Citizen before scanning. It must be the foreground window."
                    : "Перед сканированием переключись в Star Citizen. Окно игры должно быть активным.",
                "No text recognized" => IsEnglish
                    ? "No text was recognized. Open a ship screen with the model name and try again."
                    : "Текст не распознан. Открой экран корабля с названием модели и повтори.",
                _ => IsEnglish
                    ? "A ship name was not found. Open Vehicle Loadout, ASOP, Fleet Manager, or a HUD panel and try again."
                    : "Название корабля не найдено. Открой Vehicle Loadout, ASOP, «Мой флот» или HUD и повтори."
            };
    }

    partial void OnOcrEnabledChanged(bool value) => QueueSave();
    partial void OnShipDetectionStatusChanged(string value) => OnPropertyChanged(nameof(OverlayShipDetectionStatus));
    partial void OnLanguageChanged(string value)
    {
        LocalizationService.SetLanguage(value);
        OnPropertyChanged(nameof(IsEnglish));
        OnPropertyChanged(nameof(SelectedLanguage));
        OnPropertyChanged(string.Empty);
        QueueSave();
    }

    private static string SourceName(DataSourceKind source) => new ObservedValue<string>("", source, DateTimeOffset.UtcNow, 1).SourceDisplay;
    private static string Age(DateTimeOffset timestamp)
        => new ObservedValue<string>("", DataSourceKind.Manual, timestamp, 1).AgeDisplay;
}
