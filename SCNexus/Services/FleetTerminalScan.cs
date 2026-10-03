using SCNexus.Models;
using System.Windows;

namespace SCNexus.Services;

internal sealed record FleetScreenRow(VehicleCatalogItem Vehicle, bool Locked, Rect Bounds, bool StatusRecognized = true);
internal sealed record FleetScreen(string Text, IReadOnlyList<FleetScreenRow> Rows, bool IsTerminal)
{
    // Screen positions distinguish partial scrolls without relying on clocks or animated UI text.
    public string Signature => string.Join("|", Rows.Select(x => $"{x.Vehicle.Id}:{x.Locked}:{x.StatusRecognized}:{Math.Round(x.Bounds.Y / 8)}"));
}
public sealed record FleetScanSummary(int Pages, int Models, string StopReason, bool LockedEntriesSkipped);

internal static class FleetTerminalScan
{
    internal sealed record Result(IReadOnlyList<VehicleCatalogItem> Vehicles, FleetScanSummary Summary);

    internal static async Task<Result> RunAsync(FleetScreen initial,
        Func<CancellationToken, Task<FleetScreen>> read,
        Func<FleetScreen, int, CancellationToken, Task<bool>> scroll,
        IProgress<FleetScanSummary>? progress, CancellationToken token, int maxScrolls = 60)
    {
        var found = new Dictionary<int, VehicleCatalogItem>();
        var page = initial;
        var pages = 0;
        var locked = false;
        var direction = 1; // Reach the top first, so starting in the middle does not miss earlier rows.
        var unchanged = 0;
        var reason = "limit";
        void Collect(FleetScreen screen)
        {
            pages++;
            foreach (var row in screen.Rows)
            {
                locked |= row.Locked;
                if (!row.Locked && row.StatusRecognized) found[row.Vehicle.Id] = row.Vehicle;
            }
            progress?.Report(new(pages, found.Count, direction > 0 ? "top" : "down", locked));
        }

        try
        {
            if (!page.IsTerminal || page.Rows.Count == 0) return new([], new(0, 0, "unrecognized", false));
            Collect(page);
            for (var step = 0; step < maxScrolls; step++)
            {
                token.ThrowIfCancellationRequested();
                if (!await scroll(page, direction, token)) { reason = "input-unavailable"; break; }
                var next = await read(token);
                if (!next.IsTerminal || next.Rows.Count == 0) { reason = "screen-changed"; break; }
                Collect(next);
                unchanged = next.Signature == page.Signature ? unchanged + 1 : 0;
                page = next;
                if (unchanged < 2) continue;
                if (direction < 0) { reason = "end-of-list"; break; }
                direction = -1;
                unchanged = 0;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { reason = "cancelled"; }
        return new(found.Values.OrderBy(x => x.Name).ToArray(), new(pages, found.Count, reason, locked));
    }
}
