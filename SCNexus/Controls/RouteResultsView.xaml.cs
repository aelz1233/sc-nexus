using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace SCNexus.Controls;

public partial class RouteResultsView : UserControl
{
    private string _sort = "Profit";
    private ListSortDirection _direction = ListSortDirection.Descending;
    public RouteResultsView()
    {
        InitializeComponent();
        Results.TargetUpdated += (_, e) => { if (e.Property == ItemsControl.ItemsSourceProperty) ApplySort(); };
        Loaded += (_, _) => ApplySort();
    }
    private void OnSorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        _direction = _sort == e.Column.SortMemberPath && _direction == ListSortDirection.Ascending
            ? ListSortDirection.Descending : ListSortDirection.Ascending;
        _sort = e.Column.SortMemberPath;
        ApplySort();
    }
    private void ApplySort()
    {
        if (Results.ItemsSource is null) return;
        var view = CollectionViewSource.GetDefaultView(Results.ItemsSource);
        using (view.DeferRefresh())
        {
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(_sort, _direction));
        }
        foreach (var column in Results.Columns)
            column.SortDirection = column.SortMemberPath == _sort ? _direction : null;
    }
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        => ArrangeInspector(e.NewSize.Width);
    private void OnInspectorChanged(object sender, RoutedEventArgs e) => ArrangeInspector(ActualWidth);
    private void ArrangeInspector(double width)
    {
        if (DetailsColumn is null || InspectorToggle is null) return;
        var compact = width < 1080;
        DetailsColumn.Width = new GridLength(compact || InspectorToggle.IsChecked != true ? 0 : 300);
        Grid.SetColumn(Details, compact ? 0 : 1);
        Grid.SetRow(Details, compact ? 2 : 1);
        Details.Margin = compact ? new Thickness(0, 16, 12, 0) : new Thickness(0);
        Details.BorderThickness = compact ? new Thickness(0, 1, 0, 0) : new Thickness(1, 0, 0, 0);
    }
}
