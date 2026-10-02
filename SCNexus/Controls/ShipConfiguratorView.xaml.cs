using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using SCNexus.Models;
using SCNexus.Services;
using SCNexus.ViewModels;

namespace SCNexus.Controls;

public partial class ShipConfiguratorView : UserControl
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(90) };
    private readonly ShipComponentCatalogService _catalogService = new(Client);
    private MainViewModel? _main;

    public ShipConfiguratorView() => InitializeComponent();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var main = Window.GetWindow(this)?.DataContext as MainViewModel;
        if (main is null || ReferenceEquals(_main, main)) return;
        _main = main;
        ShipPicker.ItemsSource = main.Ships;
        ShipPicker.SelectedItem = main.SelectedShip ?? main.Ships.FirstOrDefault();
        StatusText.Text = ShipPicker.SelectedItem is ShipSummary
            ? LocalizationService.T("Укажи бюджет и нажми «Подобрать конфигурации».")
            : LocalizationService.T("Добавь корабль во флот, чтобы подобрать оснащение.");
        OnUseBalanceClick(sender, e);
    }

    private void OnShipSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SummaryCard.Visibility = Visibility.Collapsed;
        BudgetCard.Visibility = Visibility.Collapsed;
        BestCard.Visibility = Visibility.Collapsed;
    }

    private void OnUseBalanceClick(object sender, RoutedEventArgs e)
    {
        if (_main is not null)
            BudgetBox.Text = Math.Max(0, _main.Balance - _main.Reserve).ToString("N0", CultureInfo.CurrentCulture);
    }

    private async void OnBuildClick(object sender, RoutedEventArgs e)
    {
        if (ShipPicker.SelectedItem is not ShipSummary ship)
        {
            StatusText.Text = LocalizationService.T("Добавь корабль во флот и выбери его здесь.");
            return;
        }
        if (!TryReadBudget(BudgetBox.Text, out var budget))
        {
            StatusText.Text = LocalizationService.T("Укажи бюджет в aUEC: целое число не меньше нуля.");
            BudgetBox.Focus();
            return;
        }

        BuildButton.IsEnabled = false;
        ShipPicker.IsEnabled = false;
        ProfilePicker.IsEnabled = false;
        BudgetBox.IsEnabled = false;
        UseBalanceButton.IsEnabled = false;
        SummaryCard.Visibility = Visibility.Collapsed;
        BudgetCard.Visibility = Visibility.Collapsed;
        BestCard.Visibility = Visibility.Collapsed;
        StatusText.Text = LocalizationService.T("Загружаю порты корабля, детали и цены…");
        try
        {
            var catalog = await _catalogService.LoadAsync(ship.Name);
            var profile = ProfilePicker.SelectedIndex == 1 ? ShipBuildProfile.Travel : ShipBuildProfile.Combat;
            var builds = await Task.Run(() => ShipBuildOptimizer.Build(catalog, budget, profile,
                _main?.CurrentSystem, _main?.CurrentLocation));
            BudgetCard.DataContext = builds.Budget;
            BestCard.DataContext = builds.Best;
            BudgetCard.Visibility = Visibility.Visible;
            BestCard.Visibility = Visibility.Visible;
            SummaryCard.Visibility = Visibility.Visible;
            DataStatusText.Text = $"{catalog.ShipName} · версия игры {catalog.GameVersion} · данные {catalog.FetchedAt.ToLocalTime():dd.MM.yyyy HH:mm}" +
                (catalog.UsedOldCache ? " · сохранённая копия" : "");
            CoverageText.Text = $"Рассчитано слотов: {catalog.Slots.Count}. Типы: {string.Join(", ", catalog.Slots.Select(x => CategoryName(x.Type)).Distinct())}.";
            MethodText.Text = profile == ShipBuildProfile.Combat
                ? "Приоритет «Бой»: урон орудий и щиты, затем энергия, охлаждение и квантовый привод. Внутри типа сравниваются измеримые показатели."
                : "Приоритет «Путешествие и торговля»: скорость и расход квантового привода, затем щиты, энергия, охлаждение и орудия. Внутри типа сравниваются измеримые показатели.";
            StatusText.Text = LocalizationService.T("Подбор завершён.");
            if (LocalizationService.IsEnglish) UiLocalization.Apply(this);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
        finally
        {
            BuildButton.IsEnabled = true;
            ShipPicker.IsEnabled = true;
            ProfilePicker.IsEnabled = true;
            BudgetBox.IsEnabled = true;
            UseBalanceButton.IsEnabled = true;
        }
    }

    private static bool TryReadBudget(string text, out decimal value)
    {
        var cleaned = text.Replace("aUEC", "", StringComparison.OrdinalIgnoreCase).Trim();
        return (decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.CurrentCulture, out value) ||
                decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out value)) &&
               value >= 0 && value == decimal.Truncate(value);
    }

    private void OnCopyBudgetClick(object sender, RoutedEventArgs e) => CopyBuild(BudgetCard.DataContext as ShipBuildResult);
    private void OnCopyBestClick(object sender, RoutedEventArgs e) => CopyBuild(BestCard.DataContext as ShipBuildResult);

    private void CopyBuild(ShipBuildResult? result)
    {
        if (result is null) return;
        var shipName = (ShipPicker.SelectedItem as ShipSummary)?.Name ?? "Корабль";
        var rows = result.Lines.Select(x =>
            $"{x.SlotDisplay}: {x.Component.Name} (S{x.Component.Size}) — {x.PriceDisplay}" +
            (x.ShopDisplay.Length == 0 ? "" : $", {x.ShopDisplay}"));
        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, new[]
            {
                $"{shipName} — {result.Title}", result.CostDisplay
            }.Concat(rows).Concat(new[] { "", result.EngineeringDisplay, result.QuantumDisplay, "", result.ShoppingRouteDisplay })
                .Concat(result.ShoppingStops.SelectMany(stop => new[] { stop.Heading }.Concat(stop.Items.Select(item => "  " + item.Display))))));
            StatusText.Text = LocalizationService.T("Список деталей скопирован.");
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Не удалось скопировать список: {ex.Message}";
        }
    }

    private static string CategoryName(string type) => type switch
    {
        "Shield" => "щиты",
        "QuantumDrive" => "квантовый привод",
        "PowerPlant" => "генераторы",
        "Cooler" => "охлаждение",
        "WeaponGun" => "орудия",
        _ => type
    };

    private void OnSourceNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Не удалось открыть страницу источника: {ex.Message}";
        }
        e.Handled = true;
    }
}
