using System.Collections.ObjectModel;
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
    [ObservableProperty] private bool ocrEnabled;
    [ObservableProperty] private string dataCollectionStatus = "Источники данных запускаются…";
    [ObservableProperty] private bool needsManualGamePath = true;
    [ObservableProperty] private string language = "ru";

    public bool IsEnglish => Language == "en";
    public string[] Languages { get; } = ["Русский", "English"];
    public string SelectedLanguage
    {
        get => IsEnglish ? "English" : "Русский";
        set => Language = value == "English" ? "en" : "ru";
    }

    public ObservableCollection<DataFieldDisplay> AutomaticDataFields { get; } = [];
    public ObservableCollection<DataSourceInfo> DataSources { get; } = [];
    public ObservableCollection<MissionState> DetectedMissions { get; } = [];
    public ObservableCollection<DetectedShip> DetectedShips { get; } = [];
    public int DetectedDeaths { get; private set; }
    public int DetectedMovements { get; private set; }

    private void OnDataSnapshotUpdated(object? sender, DataCollectionSnapshot snapshot)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => ApplyDataSnapshot(snapshot));
            return;
        }
        ApplyDataSnapshot(snapshot);
    }

    private void ApplyDataSnapshot(DataCollectionSnapshot snapshot)
    {
        AutomaticDataFields.Clear();
        foreach (var field in snapshot.ToDisplayFields(IsEnglish)) AutomaticDataFields.Add(field);
        if (snapshot.Values.TryGetValue("market.prices", out var market))
            AutomaticDataFields.Add(new DataFieldDisplay(IsEnglish ? "Market prices" : "Рыночные цены", market.Value,
                SourceName(market.Source), Age(market.Timestamp), market.Confidence));

        DataSources.Clear();
        foreach (var source in snapshot.Sources.OrderBy(x => x.Priority)) DataSources.Add(source);
        DetectedMissions.Clear();
        foreach (var mission in snapshot.Missions.Take(30)) DetectedMissions.Add(mission);
        DetectedShips.Clear();
        foreach (var detected in snapshot.Ships.GroupBy(x => x.Name.Value, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).Take(50))
            DetectedShips.Add(detected);
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

        var player = snapshot.Player;
        if (player.CurrentLocation is { Confidence: >= .75 } location)
        {
            CurrentLocation = location.Value;
            LocationQuery = location.Value;
        }
        if (player.CurrentSystem is { Confidence: >= .75 } system) CurrentSystem = system.Value;
        if (player.CurrentShip is { Confidence: >= .8 } ship) SelectDetectedShip(ship.Value);
        if (player.Balance is { Confidence: >= .9 } balance && balance.Value >= 0) Balance = balance.Value;
        if (player.Environment is { } environment)
            DataCollectionStatus = $"{environment.Value} • {snapshot.Sources.Count(x => x.IsAvailable)}/{snapshot.Sources.Count} источников";
        else DataCollectionStatus = $"{snapshot.Sources.Count(x => x.IsAvailable)}/{snapshot.Sources.Count} источников доступны";
        NeedsManualGamePath = !Directory.Exists(gameLogService.ResolveGameDirectory());
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
                    IsEnglish ? "Detected automatically from Game.log" : "Обнаружен автоматически из Game.log");
                await ReloadFlightLogAsync();
                WorkspaceStatus = IsEnglish ? $"Added detected ship: {vehicle.Name}" : $"Обнаруженный корабль добавлен во флот: {vehicle.Name}";
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

    partial void OnOcrEnabledChanged(bool value) => QueueSave();
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
