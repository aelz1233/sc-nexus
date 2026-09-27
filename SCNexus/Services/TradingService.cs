using SCNexus.Models;

namespace SCNexus.Services;

public sealed class TradingService(GameDataService gameData, RouteService routes)
{
    public async Task<(DataSnapshot Data, IReadOnlyList<TradeRoute> Routes)> RecommendAsync(
        PersonalSettings settings, CancellationToken token = default)
    {
        var snapshot = await gameData.GetSnapshotAsync(token);
        return (snapshot, routes.FindRoutes(snapshot, settings));
    }
}
