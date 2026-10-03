using System.Windows;
using System.ComponentModel;
using SCNexus.ViewModels;
using System.Windows.Controls;
using System.Windows.Input;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Diagnostics;
using System.Windows.Threading;
using SCNexus.Services;
using System.Windows.Markup;

namespace SCNexus;

public partial class MainWindow : Window
{
    private bool _readyToClose;
    private bool _closePending;
    private bool _trayModeEnabled;
    private bool _exitRequested;
    private bool _trayHintShown;
    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    private System.Windows.Forms.ContextMenuStrip? _trayMenu;
    private System.Windows.Forms.ToolStripMenuItem? _trayOpenItem;
    private System.Windows.Forms.ToolStripMenuItem? _trayExitItem;
    private System.Windows.Forms.ToolStripMenuItem? _trayGameStatusItem;
    private System.Windows.Forms.ToolStripMenuItem? _trayLocationItem;
    private System.Windows.Forms.ToolStripMenuItem? _trayRouteItem;
    private System.Windows.Forms.ToolStripMenuItem? _trayOverlayItem;
    private System.Windows.Forms.ToolStripMenuItem? _trayMonitorItem;
    private System.Windows.Forms.ToolStripMenuItem? _trayUpdateItem;
    private System.Drawing.Icon? _trayIcon;
    private readonly DispatcherTimer _translationTimer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    public MainWindow()
    {
        InitializeComponent();
        _translationTimer.Tick += (_, _) =>
        {
            _translationTimer.Stop();
            if (LocalizationService.IsEnglish) UiLocalization.Apply(this);
        };
        Loaded += (_, _) =>
        {
            Language = XmlLanguage.GetLanguage(LocalizationService.IsEnglish ? "en-US" : "ru-RU");
            UiLocalization.Apply(this);
        };
        SourceInitialized += (_, _) => ApplyDarkTitleBar();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is MainViewModel previous)
            {
                previous.PropertyChanged -= OnViewModelChanged;
                previous.NotificationRaised -= OnNotificationRaised;
            }
            if (e.NewValue is MainViewModel current)
            {
                current.PropertyChanged += OnViewModelChanged;
                current.NotificationRaised += OnNotificationRaised;
            }
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
        if (e.PropertyName == nameof(MainViewModel.Language)) Dispatcher.BeginInvoke(() =>
        {
            Language = XmlLanguage.GetLanguage(LocalizationService.IsEnglish ? "en-US" : "ru-RU");
            UiLocalization.Apply(this);
            UpdateTrayText();
        });
        else if (LocalizationService.IsEnglish)
        {
            _translationTimer.Stop();
            _translationTimer.Start();
        }
    }

    private void SaveGithubToken_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (vm.SaveGithubToken(GithubTokenBox.Password)) GithubTokenBox.Clear();
    }

    private void BeginOverlayHotkeyCapture_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        vm.IsOverlayHotkeyCapturing = true;
        OverlayHotkeyCaptureButton.Tag = "Capturing";
        OverlayHotkeyCaptureButton.Focus();
        Keyboard.Focus(OverlayHotkeyCaptureButton);
    }

    private void OverlayHotkeyCapture_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (DataContext is not MainViewModel { IsOverlayHotkeyCapturing: true } vm) return;
        e.Handled = true;

        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            Key.DeadCharProcessed => e.DeadCharProcessedKey,
            _ => e.Key
        };
        if (key == Key.Escape)
        {
            EndOverlayHotkeyCapture();
            return;
        }
        if (key is Key.Delete or Key.Back && Keyboard.Modifiers == ModifierKeys.None)
        {
            vm.OverlayHotkey = "";
            EndOverlayHotkeyCapture();
            return;
        }
        if (!OverlayCoordinator.TryFormatHotkey(key, Keyboard.Modifiers, out var shortcut)) return;
        vm.OverlayHotkey = shortcut;
        EndOverlayHotkeyCapture();
    }

    private void OverlayHotkeyCapture_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (DataContext is MainViewModel { IsOverlayHotkeyCapturing: true }) EndOverlayHotkeyCapture();
    }

    private void ClearOverlayHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.OverlayHotkey = "";
        EndOverlayHotkeyCapture();
    }

    private void EndOverlayHotkeyCapture()
    {
        if (DataContext is MainViewModel vm) vm.IsOverlayHotkeyCapturing = false;
        OverlayHotkeyCaptureButton.Tag = null;
    }

    internal void EnableTrayMode()
    {
        if (_trayModeEnabled) return;
        _trayModeEnabled = true;

        if (Environment.ProcessPath is { Length: > 0 } executable)
            _trayIcon = System.Drawing.Icon.ExtractAssociatedIcon(executable);
        _trayOpenItem = new System.Windows.Forms.ToolStripMenuItem();
        _trayExitItem = new System.Windows.Forms.ToolStripMenuItem();
        _trayGameStatusItem = new System.Windows.Forms.ToolStripMenuItem { Enabled = false };
        _trayLocationItem = new System.Windows.Forms.ToolStripMenuItem { Enabled = false };
        _trayRouteItem = new System.Windows.Forms.ToolStripMenuItem { Enabled = false };
        _trayOverlayItem = new System.Windows.Forms.ToolStripMenuItem();
        _trayMonitorItem = new System.Windows.Forms.ToolStripMenuItem();
        _trayUpdateItem = new System.Windows.Forms.ToolStripMenuItem();
        _trayOpenItem.Click += (_, _) => Dispatcher.BeginInvoke(RestoreFromTray);
        _trayExitItem.Click += (_, _) => Dispatcher.BeginInvoke(RequestExit);
        _trayOverlayItem.Click += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (DataContext is MainViewModel vm) vm.ToggleOverlayFromHotkey();
        });
        _trayMonitorItem.Click += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (DataContext is not MainViewModel vm) return;
            vm.MonitorEnabled = !vm.MonitorEnabled;
            vm.RaiseNotification($"monitor:{vm.MonitorEnabled}",
                vm.IsEnglish ? "Monitoring changed" : "Мониторинг изменён",
                vm.MonitorEnabled ? (vm.IsEnglish ? "Automatic monitoring resumed." : "Автоматический мониторинг продолжен.")
                    : (vm.IsEnglish ? "Automatic monitoring paused." : "Автоматический мониторинг приостановлен."));
            UpdateTrayText();
        });
        _trayUpdateItem.Click += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            RestoreFromTray();
            if (DataContext is MainViewModel vm && vm.CheckForUpdatesCommand.CanExecute(null))
                vm.CheckForUpdatesCommand.Execute(null);
        });
        _trayMenu = new System.Windows.Forms.ContextMenuStrip { ShowImageMargin = false };
        _trayMenu.Items.AddRange([
            _trayGameStatusItem, _trayLocationItem, _trayRouteItem,
            new System.Windows.Forms.ToolStripSeparator(), _trayOverlayItem, _trayMonitorItem, _trayUpdateItem,
            new System.Windows.Forms.ToolStripSeparator(), _trayOpenItem, _trayExitItem
        ]);
        _trayMenu.Opening += (_, _) => UpdateTrayText();
        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "SC NEXUS",
            Icon = _trayIcon ?? System.Drawing.SystemIcons.Application,
            ContextMenuStrip = _trayMenu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => Dispatcher.BeginInvoke(RestoreFromTray);
        UpdateTrayText();
    }

    internal void RequestExit()
    {
        _exitRequested = true;
        Close();
    }

    private void UpdateTrayText()
    {
        if (_trayOpenItem is not null) _trayOpenItem.Text = LocalizationService.IsEnglish ? "Open SC NEXUS" : "Открыть SC NEXUS";
        if (_trayExitItem is not null) _trayExitItem.Text = LocalizationService.IsEnglish ? "Exit" : "Выйти";
        if (DataContext is not MainViewModel vm) return;
        if (_trayGameStatusItem is not null) _trayGameStatusItem.Text = vm.IsGameRunning
            ? (vm.IsEnglish ? "● Star Citizen is running" : "● Star Citizen запущен")
            : (vm.IsEnglish ? "○ Star Citizen is not running" : "○ Star Citizen не запущен");
        if (_trayLocationItem is not null) _trayLocationItem.Text = (vm.IsEnglish ? "Location: " : "Локация: ") + vm.LiveLocationDisplay;
        if (_trayRouteItem is not null) _trayRouteItem.Text = vm.HasActiveVoyage
            ? vm.ActiveVoyageStopDisplay : (vm.IsEnglish ? "No active route" : "Нет активного маршрута");
        if (_trayOverlayItem is not null) _trayOverlayItem.Text = vm.IsEnglish ? "Show / hide overlay" : "Показать / скрыть оверлей";
        if (_trayMonitorItem is not null) _trayMonitorItem.Text = vm.MonitorEnabled
            ? (vm.IsEnglish ? "Pause monitoring" : "Приостановить мониторинг")
            : (vm.IsEnglish ? "Resume monitoring" : "Продолжить мониторинг");
        if (_trayUpdateItem is not null) _trayUpdateItem.Text = vm.IsEnglish ? "Check for updates" : "Проверить обновление";
    }

    private void OnNotificationRaised(object? sender, SCNexus.Models.NexusNotification notification)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnNotificationRaised(sender, notification));
            return;
        }
        if (_notifyIcon is null || IsVisible) return;
        _notifyIcon.BalloonTipTitle = notification.Title;
        _notifyIcon.BalloonTipText = notification.Message.Length > 240
            ? notification.Message[..237] + "…" : notification.Message;
        _notifyIcon.BalloonTipIcon = notification.Kind switch
        {
            SCNexus.Models.NexusNotificationKind.Warning => System.Windows.Forms.ToolTipIcon.Warning,
            SCNexus.Models.NexusNotificationKind.Error => System.Windows.Forms.ToolTipIcon.Error,
            _ => System.Windows.Forms.ToolTipIcon.Info
        };
        _notifyIcon.ShowBalloonTip(4000);
    }

    private void HideToTray()
    {
        EndOverlayHotkeyCapture();
        ShowInTaskbar = false;
        Hide();
        if (_notifyIcon is null || _trayHintShown) return;
        _trayHintShown = true;
        _notifyIcon.BalloonTipTitle = "SC NEXUS";
        _notifyIcon.BalloonTipText = LocalizationService.IsEnglish
            ? "The app is still running in the notification area."
            : "Приложение продолжает работать в области уведомлений.";
        _notifyIcon.BalloonTipIcon = System.Windows.Forms.ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(2500);
    }

    internal void RestoreFromTray()
    {
        ShowInTaskbar = true;
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    protected override void OnClosed(EventArgs e)
    {
        _translationTimer.Stop();
        if (DataContext is MainViewModel vm)
        {
            vm.PropertyChanged -= OnViewModelChanged;
            vm.NotificationRaised -= OnNotificationRaised;
        }
        if (DataContext is MainViewModel { PendingInstallerPath: { } installer })
        {
            try
            {
                var startInfo = new ProcessStartInfo(installer) { UseShellExecute = true };
                startInfo.ArgumentList.Add("/SP-");
                startInfo.ArgumentList.Add("/VERYSILENT");
                startInfo.ArgumentList.Add("/SUPPRESSMSGBOXES");
                startInfo.ArgumentList.Add("/NORESTART");
                startInfo.ArgumentList.Add("/CLOSEAPPLICATIONS");
                startInfo.ArgumentList.Add("/TASKS=restartafterupdate");
                Process.Start(startInfo);
            }
            catch (Exception ex) { MessageBox.Show($"Не удалось запустить установщик:\n{ex.Message}\n\nФайл: {installer}", "SC NEXUS", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
        _trayMenu?.Dispose();
        _trayMenu = null;
        _trayIcon?.Dispose();
        _trayIcon = null;
        base.OnClosed(e);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_readyToClose && DataContext is MainViewModel vm)
        {
            var hideInTray = _trayModeEnabled && !_exitRequested && vm.PendingInstallerPath is null;
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
                    if (hideInTray)
                    {
                        _closePending = false;
                        HideToTray();
                        return;
                    }
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
