using System.Windows;
using System.ComponentModel;
using SCNexus.ViewModels;

namespace SCNexus;

public partial class MainWindow : Window
{
    private bool _readyToClose;
    public MainWindow() => InitializeComponent();

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (!_readyToClose && DataContext is MainViewModel vm)
        {
            e.Cancel = true;
            try { await vm.SaveNowAsync(); }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось сохранить настройки:\n{ex.Message}", "SC NEXUS", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            _readyToClose = true;
            Close();
            return;
        }
        base.OnClosing(e);
    }
}
