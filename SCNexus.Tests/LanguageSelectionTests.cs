using SCNexus.Services;

namespace SCNexus.Tests;

public class LanguageSelectionTests
{
    [Theory]
    [InlineData("english", "ru-RU", "en")]
    [InlineData("russian", "en-US", "ru")]
    [InlineData(null, "ru-RU", "ru")]
    [InlineData(null, "de-DE", "en")]
    [InlineData(null, "en-US", "en")]
    public void InitialChoiceUsesInstallerThenWindows(string? installer, string windows, string expected)
        => Assert.Equal(expected, LanguageSelectionWindow.PreferredLanguage(installer, windows));

    [Fact]
    public async Task LanguageChoiceIsOnlyRequiredUntilSettingsAreSaved()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SCNexusTests", Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(directory, "nexus.db");
            var service = new SettingsService(path);
            var settings = await service.LoadAsync();
            Assert.True(service.NeedsLanguageSelection);
            settings.Language = "en";
            await service.SaveAsync(settings);
            var restarted = new SettingsService(path);
            Assert.Equal("en", (await restarted.LoadAsync()).Language);
            Assert.False(restarted.NeedsLanguageSelection);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
