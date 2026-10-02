using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SCNexus;

public partial class OverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020L;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExNoActivate = 0x08000000L;

    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => MakePassive();
        Loaded += (_, _) => PositionAtWorkAreaEdge();
        SizeChanged += (_, _) => PositionAtWorkAreaEdge();
    }

    public void PositionAtWorkAreaEdge()
    {
        var area = SystemParameters.WorkArea;
        Left = Math.Max(area.Left + 12, area.Right - ActualWidth - 24);
        Top = Math.Max(area.Top + 12, area.Bottom - ActualHeight - 24);
    }

    private void MakePassive()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        SetWindowLongPtr(handle, GwlExStyle,
            new IntPtr(style | WsExTransparent | WsExToolWindow | WsExNoActivate));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr newStyle);
}
