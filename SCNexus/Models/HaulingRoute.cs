namespace SCNexus.Models;

public sealed record HaulingRoute(
    string Commodity, string BuyAt, string SellAt, string BuySystem, string SellSystem,
    int Scu, int CargoScu, decimal BuyPrice, decimal SellPrice, decimal Stock, decimal Demand,
    decimal Investment, decimal Revenue, bool Risky, string Category, DateTimeOffset UpdatedAt)
{
    public decimal Profit => Revenue - Investment;
    public decimal ProfitPerScu => Scu == 0 ? 0 : Profit / Scu;
    public decimal RoiPercent => Investment == 0 ? 0 : Profit / Investment * 100;
    public decimal FillPercent => CargoScu == 0 ? 0 : (decimal)Scu / CargoScu * 100;
    public bool CrossSystem => !BuySystem.Equals(SellSystem, StringComparison.OrdinalIgnoreCase);
    public bool IsPyroRoute => BuySystem.Equals("Pyro", StringComparison.OrdinalIgnoreCase) ||
        SellSystem.Equals("Pyro", StringComparison.OrdinalIgnoreCase);
    public bool IsDangerous => Risky || IsPyroRoute;
    public string ProfitDisplay => $"+{Profit:N0} aUEC";
    public string ProfitPerScuDisplay => $"+{ProfitPerScu:N0} / SCU";
    public string RoiDisplay => $"{RoiPercent:N0}% маржа";
    public string CargoDisplay => $"{Scu:N0} / {CargoScu:N0} SCU · {FillPercent:N0}%";
    public string InvestmentDisplay => $"{Investment:N0} aUEC";
    public string RouteDisplay => $"{BuyAt} ({BuySystem}) → {SellAt} ({SellSystem})";
    public string AvailabilityDisplay => $"Запас {Stock:N0} · спрос {Demand:N0} SCU";
    public string RiskDisplay => IsPyroRoute
        ? Risky ? "⚠ ОПАСНО: Pyro · NQA" : "⚠ ОПАСНО: Pyro"
        : Risky ? "⚠ Терминал NQA" : CrossSystem ? "Межсистемный рейс" : "В пределах системы";
    public string RiskColor => IsDangerous ? "#FF9A8F" : "#8D9AB5";
    public string CategoryDisplay => Category;
    public string UpdatedDisplay => $"Котировка: {UpdatedAt.LocalDateTime:dd.MM HH:mm}";
}
