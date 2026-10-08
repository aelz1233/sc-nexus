using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using SCNexus.ViewModels;

namespace SCNexus.Controls;

public partial class RouteResultsView : UserControl
{
    public RouteResultsView()
    {
        InitializeComponent();
        Results.TargetUpdated += (_, e) => { if (e.Property == ItemsControl.ItemsSourceProperty) ApplyDefaultOrder(); };
        Loaded += (_, _) => ApplyDefaultOrder();
    }
    private void ApplyDefaultOrder()
    {
        if (Results.ItemsSource is null) return;
        var view = CollectionViewSource.GetDefaultView(Results.ItemsSource);
        using (view.DeferRefresh())
        {
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription("Profit", ListSortDirection.Descending));
        }
    }
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        => ArrangeInspector(e.NewSize.Width);
    private void OnInspectorChanged(object sender, RoutedEventArgs e)
        => Dispatcher.BeginInvoke(() => ArrangeInspector(ActualWidth), DispatcherPriority.Loaded);
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        => Dispatcher.BeginInvoke(() => ArrangeInspector(ActualWidth), DispatcherPriority.Loaded);
    private void ArrangeInspector(double width)
    {
        if (DetailsColumn is null || InspectorToggle is null) return;
        var compact = width < 1030;
        Results.MaxHeight = compact ? 280 : 540;
        var showDetails = InspectorToggle.IsChecked == true && DataContext is MainViewModel { HasRouteSelection: true };
        DetailsColumn.Width = new GridLength(compact || !showDetails ? 0 : Math.Clamp(width * .32, 320, 380));
        Grid.SetColumn(Details, compact ? 0 : 1);
        Grid.SetRow(Details, compact ? 2 : 1);
        Details.Margin = compact ? new Thickness(0, 16, 12, 0) : new Thickness(0);
        Details.Padding = compact ? new Thickness(0, 16, 0, 0) : new Thickness(20, 0, 0, 0);
        Details.BorderThickness = compact ? new Thickness(0, 1, 0, 0) : new Thickness(1, 0, 0, 0);
    }
}
