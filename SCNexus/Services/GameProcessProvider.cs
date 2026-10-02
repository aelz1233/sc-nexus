using System.Diagnostics;
using SCNexus.Models;

namespace SCNexus.Services;

/// <summary>Tracks only the public Windows process list. It never opens the game process.</summary>
public sealed class GameProcessProvider : IDataProvider
{
    private bool? _lastRunning;

    public string Name => "Star Citizen process";
    public DataSourceKind Source => DataSourceKind.LocalGameData;
    public int Priority => 2;
    public TimeSpan RefreshInterval => TimeSpan.FromSeconds(3);

    public Task<DataProviderResult> CollectAsync(DataProviderContext context, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var running = IsRunning();
        var changed = _lastRunning != running;
        _lastRunning = running;
        return Task.FromResult(new DataProviderResult
        {
            Values = changed
                ? [new ValueObservation("game.running", running ? "true" : "false", Source, context.Now, .99)]
                : [],
            Status = running ? "Game is running" : "Game is not running"
        });
    }

    private static bool IsRunning()
    {
        var processes = Process.GetProcessesByName("StarCitizen");
        try { return processes.Length > 0; }
        finally { foreach (var process in processes) process.Dispose(); }
    }
}
