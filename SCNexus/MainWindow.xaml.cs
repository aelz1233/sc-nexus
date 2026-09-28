using System.Windows;
using System.ComponentModel;
using SCNexus.ViewModels;
using System.Windows.Controls;
using System.Windows.Input;

namespace SCNexus;

public partial class MainWindow : Window
{
    private bool _readyToClose;
    private bool _closePending;
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is MainViewModel previous) previous.PropertyChanged -= OnViewModelChanged;
            if (e.NewValue is MainViewModel current) current.PropertyChanged += OnViewModelChanged;
        };
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ActivePage))
            Dispatcher.BeginInvoke(() => PageScroll.ScrollToTop());
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_readyToClose && DataContext is MainViewModel vm)
        {
            if (Keyboard.FocusedElement is TextBox input) input.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            e.Cancel = true;
            if (_closePending) return;
            _closePending = true;
            // Leave WPF's Closing callback before saving and requesting Close again.
            // SaveNowAsync can complete synchronously, including before initialization.
            Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    await vm.SaveNowAsync();
                    _readyToClose = true;
                    Close();
                }
                catch (Exception ex)
                {
                    _closePending = false;
                    _readyToClose = false;
                    MessageBox.Show($"Не удалось сохранить настройки:\n{ex.Message}", "SC NEXUS", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
            return;
        }
        base.OnClosing(e);
    }
}
