using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SCNexus.Controls;

public sealed class CommodityImageConverter : IValueConverter
{
    private static readonly Dictionary<string, DrawingImage> Images = new(StringComparer.OrdinalIgnoreCase);
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var name = value?.ToString() ?? "";
        if (Images.TryGetValue(name, out var cached)) return cached;
        var color = name.Contains("Gold", StringComparison.OrdinalIgnoreCase) ? "#BC8A36" :
            name.Contains("Iodine", StringComparison.OrdinalIgnoreCase) ? "#7864AE" :
            name.Contains("Laranite", StringComparison.OrdinalIgnoreCase) ? "#B9603C" : "#638694";
        var brush = (Brush)new BrushConverter().ConvertFromString(color)!;
        var group = new DrawingGroup();
        void Shape(string path, Brush fill) => group.Children.Add(new GeometryDrawing(fill, new Pen(new SolidColorBrush(Color.FromRgb(49, 62, 73)), .7), Geometry.Parse(path)));
        if (name.Contains("Supplies", StringComparison.OrdinalIgnoreCase) || name.Contains("Materials", StringComparison.OrdinalIgnoreCase) || name.Contains(','))
        {
            Shape("M4,12 L21,5 38,12 21,20 Z", Brushes.LightSlateGray);
            Shape("M4,12 L21,20 21,37 4,29 Z", brush);
            Shape("M21,20 L38,12 38,29 21,37 Z", Brushes.SlateGray);
            Shape("M11,9 L28,16 28,33 25,34 25,18 8,11 Z", Brushes.Silver);
        }
        else
        {
            Shape("M8,33 L5,18 12,9 19,19 18,36 Z", brush);
            Shape("M16,34 L17,12 26,2 32,16 29,37 Z", brush);
            Shape("M17,12 L26,2 24,23 16,34 Z", Brushes.LightSteelBlue);
            Shape("M27,35 L29,21 37,14 39,27 33,38 Z", brush);
            Shape("M29,21 L37,14 34,29 27,35 Z", Brushes.SlateGray);
        }
        var image = new DrawingImage(group);
        image.Freeze();
        Images[name] = image;
        return image;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
