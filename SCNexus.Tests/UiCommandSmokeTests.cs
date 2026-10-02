using System.Reflection;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SCNexus.Controls;
using SCNexus.Services;
using SCNexus.ViewModels;

namespace SCNexus.Tests;

public class UiCommandSmokeTests
{
    [Fact]
    public async Task ExistingFleetShipBecomesTheCurrentShipOnStartup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SCNexusTests", Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new SettingsService(Path.Combine(directory, "test.db"));
            await settings.LoadAsync();
            await new FlightLogService(settings).AddShipAsync("C2 Hercules Starlifter", 696, "Торговля", "");
            using var client = new HttpClient();
            var data = new GameDataService(client, Path.Combine(directory, "cache"));
            var vm = new MainViewModel(settings, new TradingService(data, new RouteService()),
                new FlightLogService(settings), data, new GameLogService(), new HaulingService(), new UpdateService());

            await vm.InitializeAsync();

            Assert.Equal("C2 Hercules Starlifter", vm.SelectedShip?.Name);
            Assert.Equal(vm.SelectedShip?.Name, vm.CurrentShip);
            Assert.Equal(696, vm.CargoScu);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void VisibleButtonsReferenceExistingCommandsAndClickHandlers()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var views = new[]
        {
            (Path.Combine(root, "SCNexus", "MainWindow.xaml"), typeof(MainWindow)),
            (Path.Combine(root, "SCNexus", "Controls", "ShipConfiguratorView.xaml"), typeof(ShipConfiguratorView))
        };
        var checkedActions = 0;
        foreach (var (path, viewType) in views)
        {
            var xaml = XDocument.Load(path);
            foreach (var element in xaml.Descendants().Where(x => x.Name.LocalName is "Button" or "ToggleButton"))
            {
                var command = element.Attributes().FirstOrDefault(x => x.Name.LocalName == "Command")?.Value;
                if (command is not null)
                {
                    var match = Regex.Match(command, @"\b(\w+Command)\b");
                    Assert.True(match.Success, $"Не удалось разобрать команду: {path}: {command}");
                    Assert.NotNull(typeof(MainViewModel).GetProperty(match.Groups[1].Value));
                    checkedActions++;
                }
                var click = element.Attributes().FirstOrDefault(x => x.Name.LocalName == "Click")?.Value;
                if (click is not null)
                {
                    Assert.NotNull(viewType.GetMethod(click, BindingFlags.Instance | BindingFlags.NonPublic));
                    checkedActions++;
                }
            }
        }
        Assert.True(checkedActions >= 30, $"Проверено только {checkedActions} действий — список кнопок мог измениться.");
    }
}
