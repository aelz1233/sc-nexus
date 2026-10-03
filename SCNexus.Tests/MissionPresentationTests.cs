using System.Net.Http;
using System.Reflection;
using SCNexus.Models;
using SCNexus.Services;
using SCNexus.ViewModels;

namespace SCNexus.Tests;

public class MissionPresentationTests
{
    [Fact]
    public void DashboardAndOverlayExcludeOldEndedAndFutureMissions()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SCNexusTests", Guid.NewGuid().ToString("N"));
        using var client = new HttpClient();
        try
        {
            var settings = new SettingsService(Path.Combine(directory, "test.db"));
            var data = new GameDataService(client, Path.Combine(directory, "cache"));
            var vm = new MainViewModel(settings, new TradingService(data, new RouteService()), new FlightLogService(settings),
                data, new GameLogService(directory), new HaulingService(), new UpdateService()) { Language = "en" };
            var now = DateTimeOffset.UtcNow;
            MissionState Mission(string name, DateTimeOffset at, string status = "active") => new()
            {
                Id = name, Name = new(name, DataSourceKind.GameLog, at, .9), Status = new(status, DataSourceKind.GameLog, at, .9)
            };
            var snapshot = new DataCollectionSnapshot
            {
                Values = new Dictionary<string, ValueObservation>
                {
                    ["game.running"] = new("game.running", "true", DataSourceKind.LocalGameData, now, .99),
                    ["game.process.started"] = new("game.process.started", now.AddMinutes(-20).ToString("O"), DataSourceKind.LocalGameData, now, .99)
                },
                Missions = [Mission("Old Mission", now.AddDays(-8)), Mission("Ended Mission", now, "completed"),
                    Mission("Future Mission", now.AddDays(1)), Mission("Vaughn_Stanton1_Assassination_Intro", now.AddMinutes(-2)),
                    Mission("Second contract", now.AddMinutes(-1))]
            };
            var apply = typeof(MainViewModel).GetMethod("ApplyDataSnapshot", BindingFlags.NonPublic | BindingFlags.Instance)!;
            apply.Invoke(vm, [snapshot]);
            Assert.Equal("Current missions: 2", vm.LiveMissionTitle);
            Assert.Contains("Target elimination", vm.LiveMissionDisplay);
            Assert.DoesNotContain("Old Mission", vm.LiveMissionDisplay);
            Assert.DoesNotContain("Ended Mission", vm.LiveMissionDisplay);
            Assert.DoesNotContain("Future Mission", vm.LiveMissionDisplay);
            Assert.True(vm.OverlayHasMission);
            apply.Invoke(vm, [new DataCollectionSnapshot { Missions = snapshot.Missions, Values = new Dictionary<string, ValueObservation>
                { ["game.running"] = new("game.running", "false", DataSourceKind.LocalGameData, now, .99) } }]);
            Assert.False(vm.OverlayHasMission);
            Assert.Equal("No confirmed active missions", vm.LiveMissionDisplay);
        }
        finally
        {
            LocalizationService.SetLanguage("ru");
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("Vaughn_Stanton1_Assassination_Intro", "Vaughn · Устранение цели · Stanton")]
    [InlineData("Unknown_Contract_Name", "Unknown Contract Name")]
    [InlineData("RED WIND: ВОЗВРАЩЕНИЕ ПАКЕТА", "Red Wind: Возвращение пакета")]
    public void MissionNamesRemainReadableWithoutInventingAnOfficialTitle(string raw, string expected)
        => Assert.Equal(expected, MissionState.FormatName(raw, false));

    [Fact]
    public async Task ObjectiveCompletionDoesNotCompleteContractAndLaterObjectivesDoNotReopenIt()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SCNexusTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        const string id = "f3fd326d-1ac1-4cda-837c-a7c1541b0aba";
        try
        {
            await File.WriteAllLinesAsync(Path.Combine(directory, "Game.log"),
            [
                $"<2026-10-03T01:00:00Z> Creating objective marker: missionId [{id}], generator name [KillShip], contract [TestContract]",
                $"<2026-10-03T01:01:00Z> <ObjectiveUpserted> mission_id {id} - objective_id A - state MISSION_OBJECTIVE_STATE_COMPLETED",
                $"<2026-10-03T01:02:00Z> <MissionEnded> mission_id {id} - mission_state MISSION_STATE_COMPLETED",
                $"<2026-10-03T01:03:00Z> <ObjectiveUpserted> mission_id {id} - objective_id B - state MISSION_OBJECTIVE_STATE_ACTIVE"
            ]);
            var provider = new GameLogProvider(new GameLogService(directory));
            var result = await provider.CollectAsync(new(directory, new(), DateTimeOffset.UtcNow, false), default);
            Assert.Equal(new[] { "active", "active", "completed", "completed" },
                result.Records.Where(x => x.Kind == "mission").Select(x => ((MissionState)x.Value).Status.Value));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("Принят контракт", "RED WIND: ВОЗВРАЩЕНИЕ ПАКЕТА")]
    [InlineData("Contract accepted", "RED WIND: PACKAGE RECOVERY")]
    public async Task AcceptedNotificationProvidesOfficialTitleAndLaterMarkersKeepIt(string prefix, string title)
    {
        var directory = Path.Combine(Path.GetTempPath(), "SCNexusTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        const string id = "e0d51f47-0bc6-4ed6-9c6b-7e5ff16397c6";
        try
        {
            await File.WriteAllLinesAsync(Path.Combine(directory, "Game.log"),
            [
                $"<2026-10-03T03:56:16.033Z> <SHUDEvent_OnNotification> Added notification \"{prefix}: {title} <EM4>[+100 реп]</EM4>: \" [29] to queue. MissionId: [{id}], ObjectiveId: []",
                $"<2026-10-03T03:56:17.000Z> Creating objective marker: missionId [{id}], generator name [RedWind_RecoverItem], contract [RedWind_RecoverPackage_Stanton_1Box]"
            ]);
            var result = await new GameLogProvider(new GameLogService(directory)).CollectAsync(new(directory, new(), DateTimeOffset.UtcNow, false), default);
            var mission = (MissionState)result.Records.Last(x => x.Kind == "mission").Value;
            Assert.Equal(title, mission.Name.Value);
            Assert.Equal("active", mission.Status.Value);
        }
        finally { Directory.Delete(directory, true); }
    }
}
