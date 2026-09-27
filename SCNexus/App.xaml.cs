using System.Windows;
using System.Net.Http;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using SCNexus.Services;
using SCNexus.ViewModels;

namespace SCNexus;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
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
            collection.AddSingleton<TradingService>();
            collection.AddSingleton<MainViewModel>();
            _services = collection.BuildServiceProvider();
            var vm = _services.GetRequiredService<MainViewModel>();
            await vm.InitializeAsync();
            new MainWindow { DataContext = vm }.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось запустить SC NEXUS:\n{ex.Message}", "SC NEXUS", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}
