using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using SCNexus.Models;
using SCNexus.Services;
using SCNexus.ViewModels;

// WPF resources and the application language are process-wide.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

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
                    vm.Balance = 20000000;
                    var ship = new ShipSummary(new PersonalShip { Id = 1, Name = "C2 Hercules Starlifter", CargoScu = 696, Role = "Cargo" }, 0);
                    vm.Ships.Add(ship);
                    vm.SortedShips.Add(ship);
                    vm.CurrentShip = ship.Name;
                    vm.CargoScu = ship.Ship.CargoScu;
                    vm.SelectedShip = ship;
                    var window = new MainWindow { DataContext = vm };
                    var route = new HaulingRoute("Iodine", "ArcCorp Mining Area 056", "Seraphim Station", "Stanton", "Stanton", 600, 696,
                        8000, 8700, 1000, 1000, 4800000, 5220000, false, "Внутрисистемный", DateTimeOffset.UtcNow);
                    typeof(MainViewModel).GetField("_allHaulingRoutes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                        .SetValue(vm, new[] { route, route with { Commodity = "Laranite", BuySystem = "Pyro", BuyAt = "Ruin Station", BuyPrice = 12000, SellPrice = 13500, Investment = 7200000, Revenue = 8100000 }, route with { Commodity = "Gold", Scu = 100, BuyPrice = 2000, SellPrice = 2100, Investment = 200000, Revenue = 210000 } });
                    vm.RouteSearch = "Iodine";
                    Assert.Single(vm.RouteResults);
                    Assert.Equal("Iodine", vm.SelectedRouteResult!.Commodity);
                    vm.RouteSearch = "";
                    Assert.Equal(3, vm.RouteResults.Count);
                    vm.VoyageMode = "Сбор груза";
                    vm.VoyagePlans.Add(new VoyagePlan("Сбор груза", [route], [new VoyageStop(1, "A", "Stanton", "Купить", 600, 696, 1000000), new VoyageStop(2, "B", "Stanton", "Продать", 0, 696, 2000000)], 10000000, 696));
                    vm.RouteSearch = "Iodine";
                    Assert.Single(vm.RouteResults);
                    Assert.NotNull(vm.SelectedRouteResult!.Plan);
                    vm.VoyageMode = "Прямой рейс";
                    vm.RouteSearch = "";

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
                    var resultsGrid = LogicalDescendants(window).OfType<SCNexus.Controls.RouteResultsView>().Single().FindName("Results") as DataGrid;
                    Assert.NotNull(resultsGrid);
                    Assert.NotNull(resultsGrid.SelectedItem);
                    Assert.Equal(vm.SelectedRouteResult, resultsGrid.SelectedItem);
                    Assert.Equal(900000, ((RouteResult)resultsGrid.Items[0]).Profit);
                    resultsGrid.SelectedIndex = 0;
                    Assert.True(vm.SelectedRouteResult!.IsDangerous);
                    var sorting = new DataGridSortingEventArgs(resultsGrid.Columns.Last());
                    var resultsControl = LogicalDescendants(window).OfType<SCNexus.Controls.RouteResultsView>().Single();
                    typeof(SCNexus.Controls.RouteResultsView).GetMethod("OnSorting", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(resultsControl, [resultsGrid, sorting]);
                    Assert.Equal(10000, ((RouteResult)resultsGrid.Items[0]).Profit);
                    typeof(SCNexus.Controls.RouteResultsView).GetMethod("OnSorting", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(resultsControl, [resultsGrid, sorting]);
                    Assert.Equal(900000, ((RouteResult)resultsGrid.Items[0]).Profit);
                    foreach (var light in new[] { false, true })
                    foreach (var language in new[] { "ru", "en" })
                    foreach (var size in new[] { new Size(1100, 720), new Size(1440, 900) })
                    foreach (var page in new[] { "Обзор", "Маршруты", "Флот", "Оснащение", "Рейсы", "Инструменты", "Настройки" })
                    {
                        vm.LightTheme = light;
                        ThemeService.Apply(light);
                        Assert.Equal(light ? "#FFF3F4F6" : "#FF181A1D", ((SolidColorBrush)window.Background).Color.ToString());
                        vm.Language = language;
                        typeof(MainViewModel).GetMethod("Navigate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                            .Invoke(vm, [page]);
                        Assert.Equal(page == "Оснащение", vm.IsEquipmentOpen);
                        Assert.Equal(page == "Обзор", vm.IsDashboardOpen);
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
                            var content = (FrameworkElement)window.Content;
                            var width = (int)Math.Ceiling(content.ActualWidth);
                            var height = (int)Math.Ceiling(content.ActualHeight);
                            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height + 28, 96, 96, PixelFormats.Pbgra32);
                            var presentation = new DrawingVisual();
                            using (var drawing = presentation.RenderOpen())
                            {
                                drawing.DrawRectangle((Brush)app.Resources["Bg"], null, new Rect(0, 0, width, height + 28));
                                drawing.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, width, height));
                                drawing.DrawText(new FormattedText("SC NEXUS · Interface preview · Illustrative data", System.Globalization.CultureInfo.GetCultureInfo("en-US"), FlowDirection.LeftToRight,
                                    new Typeface("Segoe UI"), 11, (Brush)app.Resources["Muted"], 1), new Point(20, height + 6));
                            }
                            bitmap.Render(presentation);
                            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                            using var image = File.Create(Path.Combine(output, $"{(light ? "light" : "dark")}-{language}-{page}.png"));
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
                    Assert.Contains(VisualDescendants(overlay).OfType<TextBlock>(), x => x.IsVisible && x.Text == vm.BalanceDisplay);
                    Assert.DoesNotContain(VisualDescendants(overlay).OfType<TextBlock>(), x => x.IsVisible && x.Text == vm.OverlayShipSourceDisplay);
                    Assert.True(toolbar.IsVisible);
                    Assert.DoesNotContain(VisualDescendants(toolbar).OfType<Button>(), x => x.IsVisible && x.Command == vm.DetectShipFromOverlayCommand);
                    mode.Command.Execute(null);
                    overlay.UpdateLayout();
                    await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    var overlayBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(overlay.ActualWidth), (int)Math.Ceiling(overlay.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    vm.OverlayEditMode = true;
                    overlay.ApplySettings(vm);
                    overlay.UpdateLayout();
                    var chrome = (Border)overlay.FindName("OverlayChrome");
                    var expectedBackground = ((SolidColorBrush)app.FindResource("Bg")).Color;
                    var actualBackground = ((SolidColorBrush)chrome.Background).Color;
                    Assert.Equal(expectedBackground.R, actualBackground.R);
                    Assert.Equal(expectedBackground.G, actualBackground.G);
                    Assert.Equal(expectedBackground.B, actualBackground.B);
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
