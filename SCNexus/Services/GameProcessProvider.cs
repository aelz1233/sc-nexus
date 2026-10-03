using System.Diagnostics;
using SCNexus.Models;

namespace SCNexus.Services;

/// <summary>Reads Windows process metadata only, never game memory or gameplay state.</summary>
public sealed class GameProcessProvider : IDataProvider
{
    private bool? _lastRunning;
    private DateTimeOffset? _lastStartedAt;

    public string Name => "Star Citizen process";
    public DataSourceKind Source => DataSourceKind.LocalGameData;
    public int Priority => 2;
    public TimeSpan RefreshInterval => TimeSpan.FromSeconds(3);

    public Task<DataProviderResult> CollectAsync(DataProviderContext context, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var (running, startedAt) = ReadProcess();
        var changed = _lastRunning != running || _lastStartedAt != startedAt;
        _lastRunning = running;
        _lastStartedAt = startedAt;
        var values = new List<ValueObservation>();
        if (changed)
        {
            values.Add(new("game.running", running ? "true" : "false", Source, context.Now, .99));
            values.Add(new("game.process.started", startedAt?.ToString("O") ?? "", Source, context.Now, .99));
        }
        return Task.FromResult(new DataProviderResult
        {
            Values = values,
            Status = running ? "Game is running" : "Game is not running"
        });
    }

    private static (bool Running, DateTimeOffset? StartedAt) ReadProcess()
    {
        var processes = Process.GetProcessesByName("StarCitizen");
        try
        {
            DateTimeOffset? start = null;
            foreach (var process in processes)
            {
                try
                {
                    var candidate = new DateTimeOffset(process.StartTime.ToUniversalTime());
                    if (start is null || candidate > start) start = candidate;
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }
            }
            return (processes.Length > 0, start);
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }
}
