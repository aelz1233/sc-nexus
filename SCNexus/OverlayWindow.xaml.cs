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
    private const long WsExTransparent = 0x00000020L;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExNoActivate = 0x08000000L;
    private bool _dragging;
    private OverlayActionWindow? _actionWindow;

    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            MakePassive();
        };
        Closed += (_, _) => { _actionWindow?.Close(); _actionWindow = null; };
        IsVisibleChanged += (_, _) => UpdateActionWindow();
        LocationChanged += (_, _) => UpdateActionWindow();
        LayoutUpdated += (_, _) => UpdateActionWindow();
        Loaded += (_, _) => PositionAtWorkAreaEdge();
        SizeChanged += (_, _) => PositionAtWorkAreaEdge();
        MouseLeftButtonDown += OnMouseLeftButtonDown;
    }

    public void ApplySettings(MainViewModel viewModel)
    {
        var scale = Math.Clamp(viewModel.OverlayScale, .75, 1.5);
        MaxHeight = Math.Max(200, SystemParameters.WorkArea.Height - 36);
        OverlayScaleHost.MaxHeight = MaxHeight;
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
            // HTTRANSPARENT only passes to windows on the same thread. This style also works with the game.
            style |= WsExTransparent | WsExNoActivate;
        }
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style));
        Focusable = editable;
        Cursor = editable ? Cursors.SizeAll : Cursors.Arrow;
        ActionsHost.Opacity = editable ? 1 : 0;
        UpdateActionWindow();
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled) return;
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

    private void UpdateActionWindow()
    {
        if (!IsVisible || DataContext is not MainViewModel { OverlayEditMode: false } vm ||
            !ActionsHost.IsVisible || ActionsHost.ActualWidth <= 0)
        {
            _actionWindow?.Hide();
            return;
        }
        _actionWindow ??= new OverlayActionWindow(ActionsHost.ContentTemplate) { Owner = this, DataContext = vm };
        var bounds = ActionsHost.TransformToAncestor(this).TransformBounds(
            new Rect(ActionsHost.RenderSize));
        _actionWindow.Left = Left + bounds.Left;
        _actionWindow.Top = Top + bounds.Top;
        _actionWindow.Width = bounds.Width;
        _actionWindow.Height = bounds.Height;
        _actionWindow.Opacity = Math.Clamp(vm.OverlayTextOpacity, .65, 1);
        if (!_actionWindow.IsVisible) _actionWindow.Show();
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr newStyle);
}
