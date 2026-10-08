using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCNexus.Models;

namespace SCNexus.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private RouteResult? selectedRouteResult;
    [ObservableProperty] private string routeSearch = "";
    [ObservableProperty] private bool excludeDangerousRoutes;
    [ObservableProperty] private bool showRouteInspector = true;
    private IReadOnlyList<RouteResult> _routeResults = [];
    public IReadOnlyList<RouteResult> RouteResults => _routeResults;
    private IReadOnlyList<RouteResult> BuildRouteResults() => (IsDirectVoyage
        ? _allHaulingRoutes.Select(x => new RouteResult(x, null))
        : VoyagePlans.Select(x => new RouteResult(null, x)))
        .Where(x => !ExcludeDangerousRoutes || !x.IsDangerous)
        .Where(x => string.IsNullOrWhiteSpace(RouteSearch) || $"{x.Commodity} {x.Origin} {x.Destination} {x.Systems}".Contains(RouteSearch.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
    public bool HasRouteSelection => SelectedRouteResult is not null && ShowRouteInspector;
    partial void OnShowRouteInspectorChanged(bool value) => OnPropertyChanged(nameof(HasRouteSelection));
    public bool NoRouteResults => RouteResults.Count == 0;
    partial void OnRouteSearchChanged(string value) => RefreshRouteResults();
    partial void OnExcludeDangerousRoutesChanged(bool value)
    {
        RefreshRouteResults();
        // Plans were capped to the top 12; changing risk must run the planner again,
        // not merely hide those 12 rows (which may all be in Pyro).
        if (IsMultiVoyage && SelectedShip is not null && _haulingData is not null)
            _ = BuildVoyagesAsync();
    }
    partial void OnSelectedRouteResultChanged(RouteResult? value) => OnPropertyChanged(nameof(HasRouteSelection));
    private void RefreshRouteResults()
    {
        var rows = BuildRouteResults();
        var previous = SelectedRouteResult;
        _routeResults = rows;
        OnPropertyChanged(nameof(RouteResults));
        SelectedRouteResult = previous?.Direct is { } direct
            ? rows.FirstOrDefault(x => ReferenceEquals(x.Direct, direct)) ?? rows.FirstOrDefault()
            : previous?.Plan is { } plan
                ? rows.FirstOrDefault(x => ReferenceEquals(x.Plan, plan)) ?? rows.FirstOrDefault()
                : rows.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedRouteResult));
        OnPropertyChanged(nameof(NoRouteResults));
    }
    [RelayCommand]
    private async Task FindRoutesAsync()
    {
        if (IsMultiVoyage) await BuildVoyagesAsync();
        else
        {
            var previous = _haulingData;
            await EnsureMarketFreshAsync();
            if (ReferenceEquals(previous, _haulingData)) RecalculateHauling();
        }
    }
    [RelayCommand]
    private void UseSelectedRoute()
    {
        if (SelectedRouteResult?.Direct is { } direct) PrepareHaulingFlight(direct);
        else if (SelectedRouteResult?.Plan is { } plan) PrepareVoyage(plan);
    }
}
