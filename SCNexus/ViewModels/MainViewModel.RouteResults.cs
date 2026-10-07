using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCNexus.Models;

namespace SCNexus.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private RouteResult? selectedRouteResult;
    [ObservableProperty] private string routeSearch = "";
    [ObservableProperty] private bool showRouteInspector = true;
    private IReadOnlyList<RouteResult> _routeResults = [];
    public IReadOnlyList<RouteResult> RouteResults => _routeResults;
    private IReadOnlyList<RouteResult> BuildRouteResults() => (IsDirectVoyage
        ? _allHaulingRoutes.Select(x => new RouteResult(x, null))
        : VoyagePlans.Select(x => new RouteResult(null, x)))
        .Where(x => string.IsNullOrWhiteSpace(RouteSearch) || $"{x.Commodity} {x.Origin} {x.Destination} {x.Systems}".Contains(RouteSearch.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
    public bool HasRouteSelection => SelectedRouteResult is not null && ShowRouteInspector;
    partial void OnShowRouteInspectorChanged(bool value) => OnPropertyChanged(nameof(HasRouteSelection));
    public bool NoRouteResults => RouteResults.Count == 0;
    partial void OnRouteSearchChanged(string value) => RefreshRouteResults();
    partial void OnSelectedRouteResultChanged(RouteResult? value) => OnPropertyChanged(nameof(HasRouteSelection));
    private void RefreshRouteResults()
    {
        var rows = BuildRouteResults();
        var previous = SelectedRouteResult;
        _routeResults = rows;
        OnPropertyChanged(nameof(RouteResults));
        SelectedRouteResult = rows.FirstOrDefault(x => x == previous) ?? rows.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedRouteResult));
        OnPropertyChanged(nameof(NoRouteResults));
    }
    [RelayCommand]
    private async Task FindRoutesAsync()
    {
        if (IsMultiVoyage) await BuildVoyagesAsync();
        else if (_haulingData is null) await LoadMarketAsync(false);
        else RecalculateHauling();
    }
    [RelayCommand]
    private void UseSelectedRoute()
    {
        if (SelectedRouteResult?.Direct is { } direct) PrepareHaulingFlight(direct);
        else if (SelectedRouteResult?.Plan is { } plan) PrepareVoyage(plan);
    }
}
