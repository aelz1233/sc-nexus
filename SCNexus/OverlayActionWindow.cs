using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using SCNexus.ViewModels;

namespace SCNexus;

// Only this small surface accepts clicks. The rest of the overlay remains transparent to input.
internal sealed class OverlayActionWindow : Window
{
    private HwndSource? _source;

    public OverlayActionWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Topmost = true;
        var button = new Button
        {
            Background = new SolidColorBrush(Color.FromRgb(27, 67, 82)),
            Foreground = new SolidColorBrush(Color.FromRgb(234, 245, 244)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(105, 207, 192)),
            Padding = new Thickness(10, 6, 10, 6), Focusable = false,
            Margin = new Thickness(0)
        };
        button.SetBinding(ContentControl.ContentProperty, new Binding(nameof(MainViewModel.OverlayDetectShipButtonText)));
        button.SetBinding(Button.CommandProperty, new Binding(nameof(MainViewModel.DetectShipFromOverlayCommand)));
        Content = new Viewbox { Child = button, Stretch = Stretch.Fill };
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            SetWindowLongPtr(handle, -20, new IntPtr(GetWindowLongPtr(handle, -20).ToInt64() | 0x08000080L));
            _source = HwndSource.FromHwnd(handle);
            _source?.AddHook(WindowMessage);
        };
        Closed += (_, _) => _source?.RemoveHook(WindowMessage);
    }

    private static IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != 0x0021) return IntPtr.Zero; // WM_MOUSEACTIVATE
        handled = true;
        return new IntPtr(3); // MA_NOACTIVATE: click without taking focus from Star Citizen.
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
}
