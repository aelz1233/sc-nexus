using System.Net.Http;
using System.Windows;
using SCNexus.Models;
using SCNexus.Services;

namespace SCNexus.Tests;

public class BalanceDetectionTests
{
    [Theory]
    [InlineData("35,087,892", "35087892")]
    [InlineData("35087,892", "35087892")]
    [InlineData("35.087.892", "35087892")]
    [InlineData("35 087 892", "35087892")]
    [InlineData("12,50", "12.50")]
    [InlineData("1,234.50", "1234.50")]
    public void CurrencyGroupingDoesNotMultiplyFractions(string text, string expected)
    {
        Assert.True(BalanceText.TryParse(text, out var amount));
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), amount);
    }

    [Fact]
    public void MobiGlasBalanceRequiresKnownLayoutAndAUniqueAmount()
    {
        var lines = new List<OcrProvider.ScreenLine>
        {
            new("КРИМСТАТ", new Rect(300, 0, 100, 20)),
            new("ГЛАВНАЯ", new Rect(560, 140, 70, 20)),
            new("35,087,892", new Rect(350, 116, 90, 20)),
            new("35.087.892", new Rect(350, 117, 90, 20)),
            new("1,200,000", new Rect(350, 300, 90, 20))
        };
        Assert.Equal(35087892m, OcrProvider.ReadMobiGlasBalance(lines));
        lines.Add(new("99,999", new Rect(200, 120, 90, 20)));
        Assert.Null(OcrProvider.ReadMobiGlasBalance(lines));
        Assert.Null(OcrProvider.ReadMobiGlasBalance(lines.Where(x => x.Text != "ГЛАВНАЯ").ToArray()));
    }

    [Fact]
    public void OcrBalanceNeedsTwoMatchingRecentFrames()
    {
        using var client = new HttpClient();
        var directory = Path.Combine(Path.GetTempPath(), "SCNexusTests", Guid.NewGuid().ToString("N"));
        try
        {
            var provider = new OcrProvider(new GameDataService(client, directory));
            var now = DateTimeOffset.UtcNow;
            DataProviderResult Read(string amount, int seconds) => provider.ConfirmBalance(
                OcrProvider.ParseText($"Balance: {amount} aUEC", [], now.AddSeconds(seconds)));
            Assert.True(Assert.Single(Read("35,087,892", 0).Values).Confidence < .9);
            Assert.True(Assert.Single(Read("35,087,892", 2).Values).Confidence >= .9);
            Assert.True(Assert.Single(Read("1,200,000", 3).Values).Confidence < .9);
            Assert.True(Assert.Single(Read("1,200,000", 60).Values).Confidence < .9);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task TransferNotificationsAndPricesAreNotWalletBalances()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SCNexusTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllLinesAsync(Path.Combine(directory, "Game.log"),
            [
                "<2026-10-03T03:47:48.841Z> Вы отправили игроку:",
                "<2026-10-03T03:47:48.841Z> 1,200,000 aUEC",
                "<2026-10-03T03:47:48.844Z> Price: 1,200,000 aUEC",
                "<2026-10-03T03:47:49.000Z> Wallet: 35,087,892 aUEC"
            ]);
            var result = await new GameLogProvider(new GameLogService(directory))
                .CollectAsync(new(directory, new PlayerState(), DateTimeOffset.UtcNow, false), default);
            Assert.Equal("35087892", Assert.Single(result.Values, x => x.Key == "player.balance").Value);
        }
        finally { Directory.Delete(directory, true); }
    }
}
