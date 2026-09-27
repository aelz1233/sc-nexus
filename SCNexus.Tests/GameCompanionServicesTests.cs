using SCNexus.Services;

namespace SCNexus.Tests;

public class GameCompanionServicesTests
{
    [Fact]
    public async Task MonitorReadsLatestShardAndHealthChecksLocalFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SCNexusTests", Guid.NewGuid().ToString("N"));
        var localisation = Path.Combine(directory, "data", "Localization", "russian");
        Directory.CreateDirectory(localisation);
        try
        {
            await File.WriteAllLinesAsync(Path.Combine(directory, "Game.log"),
                ["Connected to pub_use1a_123", "Connected to pub_euw1b_456"]);
            await File.WriteAllTextAsync(Path.Combine(directory, "user.cfg"), "");
            await File.WriteAllTextAsync(Path.Combine(localisation, "global.ini"), "");
            var monitor = GameMonitorService.Inspect(directory);
            Assert.Equal("pub_euw1b_456", monitor.Shard);
            Assert.Equal("Европа · запад", monitor.Region);
            var health = GameHealthService.Scan(directory);
            Assert.Contains(health, x => x.Title == "Личный конфиг" && x.Detail.Contains("найден"));
            Assert.Contains(health, x => x.Title == "Файлы локализации" && x.Detail.Contains("russian"));
            var backupDirectory = Path.Combine(directory, "logbackups");
            Directory.CreateDirectory(backupDirectory);
            await File.WriteAllTextAsync(Path.Combine(backupDirectory, "previous.log"), "Joined pub_use1a_123");
            var sessions = await GameSessionService.LoadAsync(directory);
            Assert.Equal(2, sessions.Count);
            Assert.Contains(sessions, x => x.Shard == "pub_euw1b_456");
            Assert.Contains(sessions, x => x.Shard == "pub_use1a_123");
        }
        finally { Directory.Delete(directory, true); }
    }
}
