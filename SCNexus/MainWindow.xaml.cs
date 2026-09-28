using System.Windows;
using System.ComponentModel;
using SCNexus.ViewModels;
using System.Windows.Controls;
using System.Windows.Input;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Diagnostics;

namespace SCNexus;

public partial class MainWindow : Window
{
    private bool _readyToClose;
    private bool _closePending;
    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyDarkTitleBar();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is MainViewModel previous) previous.PropertyChanged -= OnViewModelChanged;
            if (e.NewValue is MainViewModel current) current.PropertyChanged += OnViewModelChanged;
        };
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private void ApplyDarkTitleBar()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var enabled = 1;
        // Windows 10 20H1+ uses attribute 20; older Windows 10 builds use 19.
        if (DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) != 0)
            DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
        var background = 0x0019100B; // COLORREF: #0B1019
        var foreground = 0x00F6F0EA; // COLORREF: #EAF0F6
        DwmSetWindowAttribute(handle, 35, ref background, sizeof(int));
        DwmSetWindowAttribute(handle, 36, ref foreground, sizeof(int));
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ActivePage))
            Dispatcher.BeginInvoke(() => PageScroll.ScrollToTop());
    }

    private void SaveGithubToken_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        vm.SaveGithubToken(GithubTokenBox.Password);
        GithubTokenBox.Clear();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is MainViewModel { PendingInstallerPath: { } installer })
        {
            try
            {
                var startInfo = new ProcessStartInfo(installer) { UseShellExecute = true };
                startInfo.ArgumentList.Add("/VERYSILENT");
                startInfo.ArgumentList.Add("/SUPPRESSMSGBOXES");
                startInfo.ArgumentList.Add("/NORESTART");
                startInfo.ArgumentList.Add("/CLOSEAPPLICATIONS");
                Process.Start(startInfo);
            }
            catch (Exception ex) { MessageBox.Show($"Не удалось запустить установщик:\n{ex.Message}\n\nФайл: {installer}", "SC NEXUS", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
        base.OnClosed(e);
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
