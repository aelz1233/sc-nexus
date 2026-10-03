using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SCNexus;

internal sealed class LanguageSelectionWindow : Window
{
    private readonly ComboBox _languages;
    public string SelectedLanguage => _languages.SelectedIndex == 1 ? "ru" : "en";

    internal static string PreferredLanguage(string? installerLanguage, string windowsLanguage) =>
        installerLanguage?.ToLowerInvariant() switch
        {
            "russian" => "ru",
            "english" => "en",
            _ => windowsLanguage.StartsWith("ru", StringComparison.OrdinalIgnoreCase) ? "ru" : "en"
        };

    public LanguageSelectionWindow(string preferredLanguage)
    {
        Title = "SC NEXUS · Language / Язык";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(13, 23, 34));
        Foreground = Brushes.White;
        var panel = new StackPanel { Margin = new Thickness(28) };
        panel.Children.Add(new TextBlock { Text = "Welcome / Добро пожаловать", FontSize = 21, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Choose your language / Выберите язык", Margin = new Thickness(0, 18, 0, 8) });
        _languages = new ComboBox { ItemsSource = new[] { "English", "Русский" }, SelectedIndex = preferredLanguage == "ru" ? 1 : 0, MinHeight = 40 };
        panel.Children.Add(_languages);
        panel.Children.Add(new TextBlock { Text = "You can change this in Settings.\nПозже можно изменить в настройках.", Margin = new Thickness(0, 12, 0, 16), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightSlateGray });
        var continueButton = new Button { Content = "Continue / Продолжить", IsDefault = true, Padding = new Thickness(16, 9, 16, 9) };
        continueButton.Click += (_, _) => DialogResult = true;
        panel.Children.Add(continueButton);
        Content = panel;
    }
}
