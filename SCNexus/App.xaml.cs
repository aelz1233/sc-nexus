using System.Windows;
using System.Net.Http;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using SCNexus.Services;
using SCNexus.ViewModels;
using System.Globalization;
using System.Windows.Markup;

namespace SCNexus;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _services;
    private readonly CancellationTokenSource _gameLogCancellation = new();
    private Mutex? _instanceMutex;
    private EventWaitHandle? _activationEvent;
    private RegisteredWaitHandle? _activationWait;
    private bool _ownsInstance;
    private readonly bool _startServices;

    public App() : this(true) { }
    internal App(bool startServices) => _startServices = startServices;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (!_startServices) return;
        DispatcherUnhandledException += (_, args) => AppLogService.Write("UI", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception) AppLogService.Write("AppDomain", exception);
        };
        TaskScheduler.UnobservedTaskException += (_, args) => AppLogService.Write("Background task", args.Exception);
        var windowsLanguage = CultureInfo.CurrentUICulture.Name;
        try
        {
            // All installations for this Windows user share the same database and hotkey.
            var instanceName = @"Local\SCNexus-" + System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value;
            _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, instanceName + "-Activate");
            _instanceMutex = new Mutex(false, instanceName);
            try { _ownsInstance = _instanceMutex.WaitOne(0); }
            catch (AbandonedMutexException) { _ownsInstance = true; }
            if (!_ownsInstance)
            {
                _activationEvent.Set();
                Shutdown();
                return;
            }
            _activationWait = ThreadPool.RegisterWaitForSingleObject(_activationEvent, (_, _) =>
            {
                if (!Dispatcher.HasShutdownStarted)
                    Dispatcher.BeginInvoke(() => (MainWindow as SCNexus.MainWindow)?.RestoreFromTray());
            }, null, Timeout.Infinite, false);
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
            var settingsService = _services.GetRequiredService<SettingsService>();
            var initialSettings = await settingsService.LoadAsync();
            if (settingsService.NeedsLanguageSelection)
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                string? installerLanguage = null;
                try { installerLanguage = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\SCNexus", "SetupLanguage", null) as string; }
                catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException) { AppLogService.Write("Setup language", ex); }
                var choice = new LanguageSelectionWindow(LanguageSelectionWindow.PreferredLanguage(installerLanguage, windowsLanguage));
                if (choice.ShowDialog() != true) { Shutdown(); return; }
                initialSettings.Language = choice.SelectedLanguage;
                await settingsService.SaveAsync(initialSettings);
            }
            var vm = _services.GetRequiredService<MainViewModel>();
            await vm.InitializeAsync();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            var mainWindow = new MainWindow { DataContext = vm, Language = XmlLanguage.GetLanguage(vm.IsEnglish ? "en-US" : "ru-RU") };
            MainWindow = mainWindow;
            mainWindow.EnableTrayMode();
            mainWindow.Show();
            _services.GetRequiredService<OverlayCoordinator>().Attach(vm, mainWindow);
            _ = vm.LoadLocationsAsync();
            _ = vm.LoadVehiclesAsync();
            _ = vm.WatchGameLogAsync(_gameLogCancellation.Token);
            _services.GetRequiredService<UpdateService>().MarkStartupHealthy();
        }
        catch (Exception ex)
        {
            AppLogService.Write("Startup", ex);
            var rollbackScheduled = false;
            try { rollbackScheduled = _services?.GetService<UpdateService>()?.TryScheduleRollback() == true; }
            catch (Exception rollbackError) { AppLogService.Write("Rollback", rollbackError); }
            var rollbackMessage = rollbackScheduled
                ? "\n\nПредыдущая версия и база будут восстановлены автоматически."
                : "";
            MessageBox.Show($"Не удалось запустить SC NEXUS:\n{ex.Message}{rollbackMessage}", "SC NEXUS", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        if (MainWindow is SCNexus.MainWindow window) window.RequestExit();
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _gameLogCancellation.Cancel();
        _gameLogCancellation.Dispose();
        _services?.Dispose();
        _activationWait?.Unregister(null);
        _activationEvent?.Dispose();
        if (_ownsInstance) _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
