namespace SCNexus.Models;

public sealed record GameTradeCandidate(DateTimeOffset TimeUtc, bool IsPurchase, decimal Amount,
    string ShopName, string QuantityText)
{
    public string KindDisplay => IsPurchase ? "Запрос покупки" : "Запрос продажи";
    public string TimeDisplay => TimeUtc.LocalDateTime.ToString("dd.MM HH:mm:ss");
    public string AmountDisplay => $"{Amount:N0} aUEC";
    public string DetailsDisplay => string.IsNullOrWhiteSpace(QuantityText)
        ? ShopName : $"{ShopName} · {QuantityText}";
}
