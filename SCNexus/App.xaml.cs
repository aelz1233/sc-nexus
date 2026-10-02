using System.Windows;
using System.Net.Http;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using SCNexus.Services;
using SCNexus.ViewModels;
using System.Globalization;
using System.Windows.Markup;

namespace SCNexus;

public partial class App : Application
{
    private ServiceProvider? _services;
    private readonly CancellationTokenSource _gameLogCancellation = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) => AppLogService.Write("UI", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception) AppLogService.Write("AppDomain", exception);
        };
        TaskScheduler.UnobservedTaskException += (_, args) => AppLogService.Write("Background task", args.Exception);
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("ru-RU");
        try
        {
            var collection = new ServiceCollection();
            collection.AddSingleton<SettingsService>();
            collection.AddSingleton(new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
            })
            {
                BaseAddress = new Uri("https://api.uexcorp.space/2.0/"),
                Timeout = TimeSpan.FromSeconds(90)
            });
            collection.AddSingleton<GameDataService>();
            collection.AddSingleton<RouteService>();
            collection.AddSingleton<HaulingService>();
            collection.AddSingleton<TradingService>();
            collection.AddSingleton<FlightLogService>();
            collection.AddSingleton<GameLogService>();
            collection.AddSingleton<DataHistoryService>();
            collection.AddSingleton<ShipComponentCatalogService>();
            collection.AddSingleton<IDataProvider, GameLogProvider>();
            collection.AddSingleton<IDataProvider, GameProcessProvider>();
            collection.AddSingleton<IDataProvider, LocalGameDataProvider>();
            collection.AddSingleton<IDataProvider, UexProvider>();
            collection.AddSingleton<IDataProvider, SCWikiProvider>();
            collection.AddSingleton<IDataProvider, OcrProvider>();
            collection.AddSingleton<IDataProvider, NexusHistoryProvider>();
            collection.AddSingleton<IDataProvider, ManualDataProvider>();
            collection.AddSingleton<DataCollectionService>();
            collection.AddSingleton<UpdateService>();
            collection.AddSingleton<OverlayCoordinator>();
            collection.AddSingleton<MainViewModel>();
            _services = collection.BuildServiceProvider();
            var vm = _services.GetRequiredService<MainViewModel>();
            await vm.InitializeAsync();
            FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
                new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(vm.IsEnglish ? "en-US" : "ru-RU")));
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            var mainWindow = new MainWindow { DataContext = vm };
            MainWindow = mainWindow;
            mainWindow.Show();
            _services.GetRequiredService<OverlayCoordinator>().Attach(vm, mainWindow);
            _ = vm.LoadLocationsAsync();
            _ = vm.LoadVehiclesAsync();
            _ = vm.WatchGameLogAsync(_gameLogCancellation.Token);
        }
        catch (Exception ex)
        {
            AppLogService.Write("Startup", ex);
            MessageBox.Show($"Не удалось запустить SC NEXUS:\n{ex.Message}", "SC NEXUS", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _gameLogCancellation.Cancel();
        _gameLogCancellation.Dispose();
        _services?.Dispose();
        base.OnExit(e);
    }
}
