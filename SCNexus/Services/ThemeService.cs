using System.Windows;

namespace SCNexus.Services;

public static class ThemeService
{
    public static bool IsLight { get; private set; }
    public static void Apply(bool light)
    {
        IsLight = light;
        var app = System.Windows.Application.Current;
        if (app is null) return;
        var dictionaries = app.Resources.MergedDictionaries;
        var previous = dictionaries.FirstOrDefault(x => x.Source?.OriginalString.Contains("Themes/") == true);
        var theme = new ResourceDictionary { Source = new Uri($"/SCNexus;component/Themes/{(light ? "Light" : "Dark")}.xaml", UriKind.Relative) };
        if (previous is null) dictionaries.Add(theme);
        else dictionaries[dictionaries.IndexOf(previous)] = theme;
    }
}
