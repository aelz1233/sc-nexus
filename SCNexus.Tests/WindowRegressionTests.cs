using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using SCNexus.Models;
using SCNexus.Services;
using SCNexus.ViewModels;

namespace SCNexus.Tests;

public class WindowRegressionTests
{
    [Fact]
    public async Task ActualRouteTemplateRendersAndWindowClosesAfterSynchronousSave()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            // Use production resources without providers, the user database or the instance mutex.
            var app = new App(startServices: false) { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.InitializeComponent();
            dispatcher.UnhandledException += (_, e) => { e.Handled = true; completion.TrySetException(e.Exception); dispatcher.BeginInvokeShutdown(DispatcherPriority.Send); };
            dispatcher.BeginInvoke(async () =>
            {
                var dir = Path.Combine(Path.GetTempPath(), "SCNexusTests", Guid.NewGuid().ToString("N"));
                try
                {
                    var languageChoice = new LanguageSelectionWindow("en");
                    var languageContent = (FrameworkElement)languageChoice.Content;
                    languageContent.Measure(new Size(440, double.PositiveInfinity));
                    languageContent.Arrange(new Rect(new Point(), languageContent.DesiredSize));
                    languageContent.UpdateLayout();
                    Assert.Equal("en", languageChoice.SelectedLanguage);
                    var languageList = LogicalDescendants(languageContent).OfType<ComboBox>().Single();
                    languageList.SelectedIndex = 1;
                    Assert.Equal("ru", languageChoice.SelectedLanguage);
                    Assert.True(languageContent.DesiredSize.Height < 400);
                    languageChoice.Close();
                    using var client = new HttpClient();
                    var settings = new SettingsService(Path.Combine(dir, "test.db"));
                    var data = new GameDataService(client, Path.Combine(dir, "cache"));
                    var vm = new MainViewModel(settings, new TradingService(data, new RouteService()),
                        new FlightLogService(settings), data, new GameLogService(), new HaulingService(), new UpdateService());
                    var window = new MainWindow { DataContext = vm };
                    var list = LogicalDescendants(window).OfType<ItemsControl>().Single(x =>
                        BindingOperations.GetBinding(x, ItemsControl.ItemsSourceProperty)?.Path.Path == "HaulingRoutes");
                    var route = new HaulingRoute("Cargo", "A", "B", "Stanton", "Pyro", 75, 100,
                        10, 20, 100, 100, 750, 1500, false, "Межзвёздный", DateTimeOffset.UtcNow);
                    var card = (FrameworkElement)list.ItemTemplate.LoadContent();
                    card.DataContext = route;
                    card.Measure(new Size(900, double.PositiveInfinity));
                    card.Arrange(new Rect(new Point(), card.DesiredSize));
                    card.UpdateLayout();
                    var progress = VisualDescendants(card).OfType<ProgressBar>().Single();
                    Assert.Equal(75d, progress.Value);
                    Assert.Equal(BindingMode.OneWay, BindingOperations.GetBinding(progress, ProgressBar.ValueProperty)!.Mode);

                    var voyages = LogicalDescendants(window).OfType<ItemsControl>().Single(x =>
                        BindingOperations.GetBinding(x, ItemsControl.ItemsSourceProperty)?.Path.Path == "VoyagePlans");
                    var voyageCard = (FrameworkElement)voyages.ItemTemplate.LoadContent();
                    voyageCard.DataContext = new VoyagePlan("Сбор груза", [route],
                        [new VoyageStop(1, "A", "Pyro", "Купить", 75, 100, 250)], 1000, 100);
                    voyageCard.Measure(new Size(900, double.PositiveInfinity));
                    voyageCard.Arrange(new Rect(new Point(), voyageCard.DesiredSize));
                    voyageCard.UpdateLayout();
                    Assert.Equal(75d, VisualDescendants(voyageCard).OfType<ProgressBar>().Single().Value);
                    var stopsExpander = VisualDescendants(voyageCard).OfType<Expander>().Single();
                    Assert.False(stopsExpander.IsExpanded);
                    Assert.Equal("Остановки и груз", stopsExpander.Header);
                    stopsExpander.IsExpanded = true;
                    voyageCard.UpdateLayout();
                    Assert.Contains(VisualDescendants(voyageCard).OfType<TextBlock>(), x => x.Text.Contains("ОПАСНО: Pyro"));

                    LocalizationService.SetLanguage("en");
                    UiLocalization.Apply(window);
                    Assert.Contains(LogicalDescendants(window).OfType<TextBlock>(), x => x.Text == "Dashboard");
                    var pageTitle = LogicalDescendants(window).OfType<TextBlock>().Single(x =>
                        BindingOperations.GetBinding(x, TextBlock.TextProperty)?.Path.Path == "PageTitle");
                    Assert.NotNull(BindingOperations.GetBinding(pageTitle, TextBlock.TextProperty));
                    LocalizationService.SetLanguage("ru");
                    UiLocalization.Apply(window);

                    // Exercise real templates at the supported minimum and normal window sizes.
                    window.ShowActivated = false;
                    window.Show();
                    await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    foreach (var language in new[] { "ru", "en" })
                    foreach (var size in new[] { new Size(1100, 720), new Size(1440, 900) })
                    foreach (var page in new[] { "Обзор", "Маршруты", "Флот", "Рейсы", "Инструменты", "Настройки" })
                    {
                        vm.Language = language;
                        typeof(MainViewModel).GetMethod("Navigate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                            .Invoke(vm, [page]);
                        window.Width = size.Width;
                        window.Height = size.Height;
                        // A shown Window is sized by its HWND; measuring it manually races display-mode changes.
                        window.UpdateLayout();
                        await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                        UiLocalization.Apply(window);
                        Assert.Contains(VisualDescendants(window).OfType<TextBlock>(), x => x.IsVisible);
                        Assert.All(VisualDescendants(window).OfType<System.Windows.Controls.Primitives.RangeBase>(), control =>
                            Assert.False(double.IsNaN(control.Value)));
                        if (size.Width == 1440)
                        {
                            var output = Path.Combine(AppContext.BaseDirectory, "audit-screenshots");
                            Directory.CreateDirectory(output);
                            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1440, 900, 96, 96, PixelFormats.Pbgra32);
                            bitmap.Render((Visual)window.Content);
                            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                            using var image = File.Create(Path.Combine(output, $"{language}-{page}.png"));
                            encoder.Save(image);
                        }
                    }

                    var shopping = new ComponentShoppingPlan("Balanced",
                        [new(1, "Stanton", "Area18", "CenterMass", [new("FR-76", 2, 42000), new("XL-1", 1, 90000)], 174000),
                         new(2, "Pyro", "Ruin Station", "Dumpers Depot", [new("Glacier", 2, 12000)], 24000)], 198000, 190000, 1);
                    vm.TrackComponentShopping(shopping, "C2 Hercules Starlifter");
                    var configurator = new SCNexus.Controls.ShipConfiguratorView();
                    var tabs = (TabControl)configurator.FindName("ResultsTabs");
                    var build = new ShipBuildResult("Build", [], 198000, 41.4, 0, 0, "Ready") { ShoppingPlans = [shopping] };
                    ((TabItem)tabs.Items[0]).DataContext = build;
                    ((TabItem)tabs.Items[1]).DataContext = build with { KnownCost = 250000 };
                    tabs.Visibility = Visibility.Visible;
                    configurator.Measure(new Size(1000, double.PositiveInfinity));
                    configurator.Arrange(new Rect(0, 0, 1000, configurator.DesiredSize.Height));
                    configurator.UpdateLayout();
                    UiLocalization.Apply(configurator);
                    Assert.Contains(VisualDescendants(tabs).OfType<TextBlock>(), x => x.Text == build.CostDisplay);
                    Assert.Equal(FontWeights.Normal, ((TabItem)tabs.Items[0]).FontWeight);
                    tabs.SelectedIndex = 1;
                    configurator.UpdateLayout();
                    Assert.Contains(VisualDescendants(tabs).OfType<TextBlock>(), x => x.Text == (build with { KnownCost = 250000 }).CostDisplay);
                    var tabBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1000, (int)Math.Ceiling(configurator.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    tabBitmap.Render(configurator);
                    var tabEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    tabEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(tabBitmap));
                    using (var output = File.Create(Path.Combine(AppContext.BaseDirectory, "audit-screenshots", "configurator.png"))) tabEncoder.Save(output);
                    var overlay = new OverlayWindow { DataContext = vm };
                    vm.IsGameRunning = true;
                    vm.OverlayEditMode = true;
                    vm.OverlayPreview = true;
                    vm.ToggleOverlayFromHotkey();
                    Assert.False(vm.OverlayEditMode);
                    Assert.False(vm.OverlayPreview);
                    Assert.True(vm.OverlaySuppressed);
                    Assert.False(vm.OverlayHotkeyVisible);
                    vm.ToggleOverlayFromHotkey();
                    Assert.True(vm.OverlayHotkeyVisible);
                    Assert.False(vm.OverlaySuppressed);
                    vm.HideOverlayCommand.Execute(null);
                    Assert.True(vm.OverlaySuppressed);
                    Assert.False(vm.OverlayHotkeyVisible);
                    vm.OverlayExpanded = true;
                    overlay.ApplySettings(vm);
                    overlay.Show();
                    overlay.UpdateLayout();
                    await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    Assert.Single(app.Windows.OfType<OverlayActionWindow>());
                    var toolbar = app.Windows.OfType<OverlayActionWindow>().Single();
                    Assert.Contains(VisualDescendants(toolbar).OfType<Button>(), x => x.Command == vm.HideOverlayCommand);
                    var mode = VisualDescendants(toolbar).OfType<Button>().Single(x => x.Command == vm.ToggleOverlayModeCommand);
                    mode.Command.Execute(null);
                    overlay.UpdateLayout();
                    await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    Assert.False(vm.OverlayExpanded);
                    Assert.True(toolbar.IsVisible);
                    Assert.DoesNotContain(VisualDescendants(toolbar).OfType<Button>(), x => x.IsVisible && x.Command == vm.DetectShipFromOverlayCommand);
                    mode.Command.Execute(null);
                    overlay.UpdateLayout();
                    await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    var overlayBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(overlay.ActualWidth), (int)Math.Ceiling(overlay.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    vm.OverlayEditMode = true;
                    overlay.ApplySettings(vm);
                    overlay.UpdateLayout();
                    overlayBitmap.Render(overlay);
                    var overlayEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    overlayEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(overlayBitmap));
                    using (var output = File.Create(Path.Combine(AppContext.BaseDirectory, "audit-screenshots", "overlay.png"))) overlayEncoder.Save(output);
                    vm.OverlayEditMode = false;
                    overlay.ApplySettings(vm);
                    overlay.Hide();
                    Assert.False(app.Windows.OfType<OverlayActionWindow>().Single().IsVisible);
                    overlay.Close();
                    Assert.Empty(app.Windows.OfType<OverlayActionWindow>());

                    var closed = new TaskCompletionSource();
                    window.Closed += (_, _) => closed.TrySetResult();
                    // Uninitialized VM's save finishes synchronously: this previously re-entered Closing.
                    window.Close();
                    await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    completion.TrySetResult();
                }
                catch (Exception ex) { completion.TrySetException(ex); }
                finally
                {
                    LocalizationService.SetLanguage("ru");
                    if (Directory.Exists(dir)) Directory.Delete(dir, true);
                    app.Shutdown();
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(60));
    }

    private static IEnumerable<DependencyObject> LogicalDescendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in LogicalDescendants(child)) yield return descendant;
        }
    }


    private static IEnumerable<DependencyObject> VisualDescendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in VisualDescendants(child)) yield return descendant;
        }
    }
}
