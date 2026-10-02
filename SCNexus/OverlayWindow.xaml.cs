using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using SCNexus.ViewModels;

namespace SCNexus;

public partial class OverlayWindow : Window
{
    private const double BaseWidth = 460;
    private const int GwlExStyle = -20;
    private const int WmNcHitTest = 0x0084;
    private const int HtClient = 1;
    private const int HtTransparent = -1;
    private const long WsExTransparent = 0x00000020L;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExNoActivate = 0x08000000L;
    private bool _dragging;
    private HwndSource? _source;

    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            _source = PresentationSource.FromVisual(this) as HwndSource;
            _source?.AddHook(WindowMessageHook);
            MakePassive();
        };
        Closed += (_, _) => _source?.RemoveHook(WindowMessageHook);
        Loaded += (_, _) => PositionAtWorkAreaEdge();
        SizeChanged += (_, _) => PositionAtWorkAreaEdge();
        MouseLeftButtonDown += OnMouseLeftButtonDown;
    }

    public void ApplySettings(MainViewModel viewModel)
    {
        var scale = Math.Clamp(viewModel.OverlayScale, .75, 1.5);
        OverlayScaleHost.Width = BaseWidth * scale;
        Width = BaseWidth * scale;
        OverlayChrome.Background = new SolidColorBrush(Color.FromArgb(
            (byte)Math.Round(Math.Clamp(viewModel.OverlayOpacity, .65, 1) * 255), 10, 18, 28));
        OverlayContent.Opacity = Math.Clamp(viewModel.OverlayTextOpacity, .65, 1);
        ApplyInteractionMode(viewModel.OverlayEditMode);
        PositionAtWorkAreaEdge();
    }

    public void PositionAtWorkAreaEdge()
    {
        if (_dragging) return;
        var area = SystemParameters.WorkArea;
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : 260;
        if (DataContext is MainViewModel { OverlayAnchor: "Custom", OverlayCustomLeft: >= 0, OverlayCustomTop: >= 0 } custom)
        {
            Left = Math.Clamp(custom.OverlayCustomLeft, SystemParameters.VirtualScreenLeft + 8,
                SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - width - 8);
            Top = Math.Clamp(custom.OverlayCustomTop, SystemParameters.VirtualScreenTop + 8,
                SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - height - 8);
            return;
        }
        var anchor = (DataContext as MainViewModel)?.OverlayAnchor ?? "BottomRight";
        Left = anchor is "TopLeft" or "BottomLeft"
            ? area.Left + 18 : Math.Max(area.Left + 18, area.Right - width - 18);
        Top = anchor is "TopLeft" or "TopRight"
            ? area.Top + 18 : Math.Max(area.Top + 18, area.Bottom - height - 18);
    }

    private void MakePassive()
    {
        ApplyInteractionMode((DataContext as MainViewModel)?.OverlayEditMode == true);
    }

    private void ApplyInteractionMode(bool editable)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        style |= WsExToolWindow;
        if (editable) style &= ~(WsExTransparent | WsExNoActivate);
        else
        {
            // Let WM_NCHITTEST keep only the OCR button interactive; every other pixel passes through to the game.
            style &= ~WsExTransparent;
            style |= WsExNoActivate;
        }
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style));
        Focusable = editable;
        Cursor = editable ? Cursors.SizeAll : Cursors.Arrow;
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || DataContext is not MainViewModel { OverlayEditMode: true } vm)
            return;
        _dragging = true;
        try { DragMove(); }
        catch (InvalidOperationException) { }
        finally
        {
            _dragging = false;
            vm.SetOverlayCustomPosition(Left, Top);
        }
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmNcHitTest || DataContext is not MainViewModel { OverlayEditMode: false }) return IntPtr.Zero;
        if (!DetectShipButton.IsVisible || !DetectShipButton.IsEnabled)
        {
            handled = true;
            return new IntPtr(HtTransparent);
        }
        var raw = lParam.ToInt64();
        var screenPoint = new Point((short)(raw & 0xffff), (short)((raw >> 16) & 0xffff));
        var point = PointFromScreen(screenPoint);
        var buttonBounds = DetectShipButton.TransformToAncestor(this).TransformBounds(
            new Rect(0, 0, DetectShipButton.ActualWidth, DetectShipButton.ActualHeight));
        handled = true;
        return buttonBounds.Contains(point) ? new IntPtr(HtClient) : new IntPtr(HtTransparent);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr newStyle);
}
