using System.ComponentModel;
using System.Windows;
using System.Windows.Markup;
using SCNexus.Services;
using SCNexus.ViewModels;

namespace SCNexus;

public partial class FlightEditorWindow : Window
{
    public FlightEditorWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        ApplyLanguage();
        Loaded += (_, _) => UiLocalization.Apply(this);
        PropertyChangedEventHandler onChanged = (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Language))
            {
                ApplyLanguage();
            }
        };
        vm.PropertyChanged += onChanged;
        Closed += (_, _) => vm.PropertyChanged -= onChanged;
    }

    private void ApplyLanguage()
    {
        Language = XmlLanguage.GetLanguage(LocalizationService.IsEnglish ? "en-US" : "ru-RU");
        Title = LocalizationService.IsEnglish ? "Trip record" : "Учёт рейса";
        UiLocalization.Apply(this);
    }
}
