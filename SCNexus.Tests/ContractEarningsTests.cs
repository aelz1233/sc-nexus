using System.Net.Http;
using System.Windows;
using SCNexus.Models;
using SCNexus.Services;
using SCNexus.ViewModels;

namespace SCNexus.Tests;

public class ContractEarningsTests
{
    [Fact]
    public async Task EarningsAndDisabledSettingSurviveRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SCNexusTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "test.db");
        try
        {
            var service = new SettingsService(path);
            var settings = await service.LoadAsync();
            settings.ContractEarningsEnabled = false;
            settings.ContractEarningsJson = System.Text.Json.JsonSerializer.Serialize(new[] {
                new ContractEarning("receipt", "Contract", 700000, DateTimeOffset.UtcNow, 60, true, ["mission"], true) });
            await service.SaveAsync(settings);
            var restored = await new SettingsService(path).LoadAsync();
            Assert.False(restored.ContractEarningsEnabled);
            var earning = Assert.Single(System.Text.Json.JsonSerializer.Deserialize<ContractEarning[]>(restored.ContractEarningsJson)!);
            Assert.Equal(700000, earning.Amount);
            Assert.True(earning.Excluded);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task ActualPayoutNotificationIsRecognizedAndReplaysHaveStableIds()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SCNexusTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        const string id = "6fbfdfce-435b-4475-b8ba-4e767c9fc7c7";
        try
        {
            await File.WriteAllLinesAsync(Path.Combine(directory, "Game.log"),
            [
                $"<2026-10-03T01:00:00Z> Creating objective marker: missionId [{id}], generator name [Cargo], contract [Hauling]",
                $"<2026-10-03T02:00:00Z> <MissionEnded> mission_id {id} - mission_state MISSION_STATE_COMPLETED",
                "<2026-10-03T02:00:00.443Z> <SHUDEvent_OnNotification> Added notification \"Начислено 50250 aUEC: \" [115] to queue. MissionId: [00000000-0000-0000-0000-000000000000]",
                "<2026-10-03T02:00:00.443Z> <SHUDEvent_OnNotification> Added notification \"Начислено 50250 aUEC: \" [115] to queue.",
                "<2026-10-03T02:00:01Z> <SHUDEvent_OnNotification> Added notification \"Игрок отправил вам: 700000 aUEC\" [116]",
                "<2026-10-03T02:10:00Z> <SHUDEvent_OnNotification> Added notification \"Начислено 500 aUEC: \" [120]"
            ]);
            var result = await new GameLogProvider(new GameLogService(directory)).CollectAsync(new(directory, new(), DateTimeOffset.UtcNow, false), default);
            var receipts = result.Records.Where(x => x.Kind == "contract-income").ToArray();
            Assert.Equal(2, receipts.Length);
            Assert.Single(receipts.Select(x => x.Key).Distinct());
            var payout = Assert.IsType<ContractEarning>(receipts[0].Value);
            Assert.Equal(50250, payout.Amount);
            Assert.True(payout.AutoDetected);
            Assert.Contains(id, payout.RelatedMissionIds!);
            Assert.InRange(payout.Minutes, 60, 60.1);
            Assert.DoesNotContain(result.Values, x => x.Key == "player.balance");
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void PayoutCorrectionToggleAndRemovalNeverChangeWallet()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SCNexusTests", Guid.NewGuid().ToString("N"));
        using var client = new HttpClient();
        var settings = new SettingsService(Path.Combine(directory, "test.db"));
        var data = new GameDataService(client, Path.Combine(directory, "cache"));
        var vm = new MainViewModel(settings, new TradingService(data, new RouteService()), new FlightLogService(settings),
            data, new GameLogService(directory), new HaulingService(), new UpdateService());
        var now = DateTimeOffset.UtcNow;
        vm.Balance = 35087892;
        vm.SelectedEarningMission = new MissionState { Id = "contract-1", Name = new("Contract", DataSourceKind.GameLog, now, 1),
            Status = new("completed", DataSourceKind.GameLog, now, 1) };
        vm.ContractRewardAmount = 700000;
        vm.ContractDurationMinutes = 60;
        vm.SaveContractEarningCommand.Execute(null);
        vm.SaveContractEarningCommand.Execute(null);
        Assert.Single(vm.ContractEarnings);
        Assert.Equal(35087892, vm.Balance);
        Assert.Contains("700", vm.TodayProfitDisplay);
        Assert.Contains("700", vm.PersonalProfitHourDisplay);
        vm.ContractRewardAmount = 600000;
        vm.SaveContractEarningCommand.Execute(null);
        Assert.Equal(600000, Assert.Single(vm.ContractEarnings).Amount);
        vm.ContractEarningsEnabled = false;
        Assert.Equal("0 aUEC", vm.TodayProfitDisplay);
        Assert.Single(vm.ContractEarnings);
        vm.DeleteContractEarningCommand.Execute(vm.ContractEarnings[0]);
        Assert.Empty(vm.ContractEarnings);
        Assert.Equal(35087892, vm.Balance);
    }

    [Fact]
    public void OverlappingActivitiesOnlyCountTimeOnce()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(2, MainViewModel.EarningHours([(now, now.AddHours(1)),
            (now.AddMinutes(30), now.AddHours(2))]));
    }

    [Fact]
    public void OfferedRewardIsNotAWalletBalanceOrEarnedIncome()
    {
        var lines = new OcrProvider.ScreenLine[] { new("НАГРАДА", new Rect(100,100,60,15)),
            new("¤50,500", new Rect(350,100,70,15)), new("72k", new Rect(100,300,40,15)) };
        Assert.Equal(50500m, OcrProvider.ReadContractReward(lines));
        Assert.Null(OcrProvider.ReadContractReward(lines.Where(x => x.Text != "НАГРАДА").ToArray()));
        Assert.Empty(OcrProvider.ParseText("НАГРАДА 50,500", [], DateTimeOffset.UtcNow).Records);
    }
}
