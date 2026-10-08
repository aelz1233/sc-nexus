using System.Net.Http;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
                    var otherShip = new ShipSummary(new PersonalShip { Id = 2, Name = "Constellation Taurus", CargoScu = 174, Role = "Cargo" }, 0);
                    vm.Ships.Add(otherShip);
                    vm.SortedShips.Add(otherShip);
                    vm.SelectedShip = otherShip;
                    vm.CurrentShip = ship.Name;
                    vm.OpenEquipmentCommand.Execute(null);
                    Assert.Same(ship, vm.SelectedShip);
                    Assert.True(vm.IsEquipmentOpen);
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
                    var overlayPlan = new VoyagePlan("Сбор груза", [route], [new VoyageStop(1, "A", "Stanton", "Купить", 600, 696, 1000000), new VoyageStop(2, "B", "Stanton", "Продать", 0, 696, 2000000)], 10000000, 696);
                    vm.VoyagePlans.Add(overlayPlan);
                    vm.RouteSearch = "Iodine";
                    Assert.Single(vm.RouteResults);
                    Assert.NotNull(vm.SelectedRouteResult!.Plan);
                    vm.VoyageMode = "Прямой рейс";
                    vm.RouteSearch = "";

                    vm.Language = "en";
                    UiLocalization.Apply(window);
                    Assert.Contains(LogicalDescendants(window).OfType<TextBlock>(), x => x.Text == "Dashboard");
                    Assert.Equal("Danger", vm.RouteResults.Single(x => x.Commodity == "Laranite").RiskTitle);
                    Assert.Equal("NEXT ACTION", vm.OverlayActionHeading);
                    var localizedHint = new TextBox { ToolTip = "Товар или точка" };
                    AutomationProperties.SetName(localizedHint, "Поиск: товар или точка");
                    UiLocalization.Apply(localizedHint);
                    Assert.Equal("Commodity or location", localizedHint.ToolTip);
                    Assert.Equal("Search: commodity or location", AutomationProperties.GetName(localizedHint));
                    var sources = new TextBlock();
                    var sourcesLabel = new System.Windows.Documents.Run("Источники: ");
                    sources.Inlines.Add(sourcesLabel);
                    UiLocalization.Apply(sources);
                    Assert.Equal("Sources: ", sourcesLabel.Text);
                    var pageTitle = LogicalDescendants(window).OfType<TextBlock>().Single(x =>
                        BindingOperations.GetBinding(x, TextBlock.TextProperty)?.Path.Path == "PageTitle");
                    Assert.NotNull(BindingOperations.GetBinding(pageTitle, TextBlock.TextProperty));
                    vm.Language = "ru";
                    UiLocalization.Apply(window);
                    UiLocalization.Apply(localizedHint);
                    UiLocalization.Apply(sources);
                    Assert.Equal("Товар или точка", localizedHint.ToolTip);
                    Assert.Equal("Поиск: товар или точка", AutomationProperties.GetName(localizedHint));
                    Assert.Equal("Источники: ", sourcesLabel.Text);

                    // Exercise real templates at the supported minimum and normal window sizes.
                    window.ShowActivated = false;
                    window.Show();
                    typeof(MainViewModel).GetMethod("Navigate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                        .Invoke(vm, ["Маршруты"]);
                    window.UpdateLayout();
                    await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    var routeFilters = (Expander)window.FindName("RouteFilters");
                    Assert.False(routeFilters.IsExpanded);
                    routeFilters.IsExpanded = true;
                    window.UpdateLayout();
                    var dangerousFilter = LogicalDescendants(routeFilters).OfType<CheckBox>().Single(x =>
                        Equals(x.Content, "Исключить опасные"));
                    dangerousFilter.IsChecked = true;
                    Assert.True(vm.ExcludeDangerousRoutes);
                    Assert.Equal(2, vm.RouteResults.Count);
                    dangerousFilter.IsChecked = false;
                    Assert.Equal(3, vm.RouteResults.Count);
                    Assert.Contains(LogicalDescendants(routeFilters).OfType<Button>(), x =>
                        Equals(x.Content, "Сбросить фильтры"));
                    vm.Language = "en";
                    await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    UiLocalization.Apply(window);
                    Assert.Equal("Filters", routeFilters.Header);
                    Assert.Contains(LogicalDescendants(routeFilters).OfType<CheckBox>(), x =>
                        Equals(x.Content, "Exclude dangerous routes"));
                    Assert.Equal("Systems will appear after market prices load.", LocalizationService.T(vm.RouteSystemsSummary));
                    Assert.DoesNotContain(VisualDescendants(routeFilters).OfType<TextBlock>(), x =>
                        x.IsVisible && System.Text.RegularExpressions.Regex.IsMatch(x.Text ?? "", "[А-Яа-яЁё]"));
                    vm.Language = "ru";
                    UiLocalization.Apply(window);
                    routeFilters.IsExpanded = false;
                    Assert.Equal("Все системы", ((ComboBox)window.FindName("RouteStartSelector")).SelectedItem);
                    var resultsGrid = LogicalDescendants(window).OfType<SCNexus.Controls.RouteResultsView>().Single().FindName("Results") as DataGrid;
                    Assert.NotNull(resultsGrid);
                    Assert.NotNull(resultsGrid.SelectedItem);
                    Assert.Equal(vm.SelectedRouteResult, resultsGrid.SelectedItem);
                    Assert.Equal(900000, ((RouteResult)resultsGrid.Items[0]).Profit);
                    resultsGrid.SelectedIndex = 0;
                    Assert.True(vm.SelectedRouteResult!.IsDangerous);
                    Assert.False(resultsGrid.CanUserReorderColumns);
                    Assert.True(resultsGrid.CanUserSortColumns);
                    Assert.Equal(900000, ((RouteResult)resultsGrid.Items[0]).Profit);
                    void ClickSortHeader(string headerText)
                    {
                        var header = VisualDescendants(resultsGrid).OfType<DataGridColumnHeader>()
                            .Single(x => Equals(x.Content, headerText));
                        typeof(DataGridColumnHeader).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                            .Invoke(header, null);
                    }
                    foreach (var (headerText, ascending, descending) in new[]
                    {
                        ("Рейс", "Gold", "Laranite"),
                        ("Инвестиции", "Gold", "Laranite"),
                        ("Прибыль", "Gold", "Laranite")
                    })
                    {
                        ClickSortHeader(headerText);
                        Assert.Equal(ascending, ((RouteResult)resultsGrid.Items[0]).Commodity);
                        ClickSortHeader(headerText);
                        Assert.Equal(descending, ((RouteResult)resultsGrid.Items[0]).Commodity);
                    }
                    ClickSortHeader("Риск");
                    Assert.Equal(0, ((RouteResult)resultsGrid.Items[0]).RiskRank);
                    ClickSortHeader("Риск");
                    Assert.Equal(2, ((RouteResult)resultsGrid.Items[0]).RiskRank);
                    vm.RouteSearch = "Iodine";
                    vm.RouteSearch = "";
                    Assert.Equal(2, ((RouteResult)resultsGrid.Items[0]).RiskRank);
                    var untranslatedEnglish = new HashSet<string>();
                    foreach (var light in new[] { false, true })
                    foreach (var language in new[] { "ru", "en" })
                    foreach (var size in new[] { new Size(1100, 720), new Size(1440, 900) })
                    foreach (var page in new[] { "Обзор", "Маршруты", "Флот", "Оснащение", "Рейсы", "Инструменты", "Настройки" })
                    {
                        vm.LightTheme = light;
                        ThemeService.Apply(light);
                        Assert.Equal(light ? "#FFF4F2EE" : "#FF101719", ((SolidColorBrush)window.Background).Color.ToString());
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
                        if (page == "Маршруты")
                            Assert.Contains(VisualDescendants(window).OfType<TextBlock>(), x => x.IsVisible && x.Text == "Laranite");
                        if (language == "en")
                        {
                            untranslatedEnglish.UnionWith(VisualDescendants(window).OfType<TextBlock>()
                                .Where(x => x.IsVisible && System.Text.RegularExpressions.Regex.IsMatch(x.Text ?? "", "[А-Яа-яЁё]"))
                                .Select(x => x.Text));
                            untranslatedEnglish.UnionWith(VisualDescendants(window).OfType<ContentControl>()
                                .Where(x => x.IsVisible && x.Content is string text && System.Text.RegularExpressions.Regex.IsMatch(text, "[А-Яа-яЁё]"))
                                .Select(x => (string)x.Content));
                            untranslatedEnglish.UnionWith(VisualDescendants(window).OfType<HeaderedContentControl>()
                                .Where(x => x.IsVisible && x.Header is string text && System.Text.RegularExpressions.Regex.IsMatch(text, "[А-Яа-яЁё]"))
                                .Select(x => (string)x.Header));
                        }
                        Assert.All(VisualDescendants(window).OfType<System.Windows.Controls.Primitives.RangeBase>(), control =>
                            Assert.False(double.IsNaN(control.Value)));
                        if (!light && language == "ru" && size.Width == 1440 && page == "Маршруты")
                        {
                            var routeView = LogicalDescendants(window).OfType<SCNexus.Controls.RouteResultsView>().Single();
                            var detailsColumn = (ColumnDefinition)routeView.FindName("DetailsColumn");
                            var detailsToggle = (CheckBox)routeView.FindName("InspectorToggle");
                            var wideLayout = routeView.ActualWidth >= 1030;
                            Assert.True(resultsGrid.ActualHeight < 350);
                            Assert.Equal(wideLayout, detailsColumn.Width.Value > 0);
                            detailsToggle.IsChecked = false;
                            await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                            Assert.Equal(0, detailsColumn.Width.Value);
                            detailsToggle.IsChecked = true;
                            await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                            Assert.Equal(wideLayout, detailsColumn.Width.Value > 0);
                            vm.RouteSearch = "not-a-route";
                            await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                            Assert.Equal(0, detailsColumn.Width.Value);
                            vm.RouteSearch = "";
                            await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                            Assert.Equal(wideLayout, detailsColumn.Width.Value > 0);
                        }
                        if (size.Width == 1440 || page is "Обзор" or "Маршруты" or "Настройки")
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
                            var prefix = size.Width == 1440 ? "" : "min-";
                            using var image = File.Create(Path.Combine(output, $"{prefix}{(light ? "light" : "dark")}-{language}-{page}.png"));
                            encoder.Save(image);
                        }
                    }
                    Assert.Empty(untranslatedEnglish);

                    vm.Language = "ru";
                    vm.PrepareVoyageCommand.Execute(overlayPlan);
                    vm.NextVoyageStopCommand.Execute(null);
                    vm.NextVoyageStopCommand.Execute(null);
                    typeof(MainViewModel).GetMethod("Navigate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                        .Invoke(vm, ["Маршруты"]);
                    window.UpdateLayout();
                    UiLocalization.Apply(window);
                    var dismissRoute = VisualDescendants(window).OfType<Button>()
                        .Single(x => x.IsVisible && Equals(x.Content, "Убрать"));
                    Assert.False(vm.CanAdvanceVoyage);
                    typeof(Button).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                        .Invoke(dismissRoute, null);
                    Assert.False(vm.HasActiveVoyage);

                    vm.Language = "en";
                    vm.OpenSettingsCommand.Execute(null);
                    var settingsTabs = LogicalDescendants(window).OfType<TabControl>().Single(x => x.Items.Count == 4);
                    settingsTabs.SelectedIndex = 2;
                    window.UpdateLayout();
                    await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    UiLocalization.Apply(window);
                    Assert.DoesNotContain(VisualDescendants(window).OfType<TextBlock>(), x =>
                        x.IsVisible && System.Text.RegularExpressions.Regex.IsMatch(x.Text ?? "", "[А-Яа-яЁё]"));
                    settingsTabs.SelectedIndex = 0;

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
                    typeof(MainViewModel).GetMethod("ActivateVoyageGuidance", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                        .Invoke(vm, [overlayPlan]);
                    vm.LightTheme = false;
                    ThemeService.Apply(false);
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
                    // The overlay now has opt-in interactive controls; verify both modes.
                    Assert.Contains(VisualDescendants(overlay).OfType<Button>(),
                        x => x.IsVisible && Equals(x.Content, "Scan terminal"));
                    Assert.Contains(VisualDescendants(overlay).OfType<Button>(),
                        x => x.IsVisible && Equals(x.Content, "Step complete →"));
                    vm.OverlayControlsEnabled = false;
                    overlay.ApplySettings(vm);
                    overlay.UpdateLayout();
                    Assert.DoesNotContain(VisualDescendants(overlay).OfType<Button>(), x => x.IsVisible);
                    vm.OverlayControlsEnabled = true;
                    overlay.ApplySettings(vm);
                    overlay.UpdateLayout();
                    Assert.DoesNotContain(VisualDescendants(overlay).OfType<TextBlock>(), x => x.IsVisible && x.Text == vm.BalanceDisplay);
                    Assert.DoesNotContain(VisualDescendants(overlay).OfType<TextBlock>(), x => x.IsVisible && x.Text == vm.OverlayShipSourceDisplay);
                    Assert.DoesNotContain(VisualDescendants(overlay).OfType<TextBlock>(), x =>
                        x.IsVisible && System.Text.RegularExpressions.Regex.IsMatch(x.Text ?? "", "[А-Яа-яЁё]"));
                    Assert.True(overlay.ActualWidth <= 360);
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
                    overlay.Close();

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
