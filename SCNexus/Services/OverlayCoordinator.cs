using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using SCNexus.ViewModels;

namespace SCNexus.Services;

public sealed class OverlayCoordinator : IDisposable
{
    private const int HotkeyId = 0x534E;
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;
    private MainViewModel? _viewModel;
    private OverlayWindow? _window;
    private Window? _owner;
    private HwndSource? _source;
    private IntPtr _ownerHandle;
    private bool _hotkeyRegistered;
    private readonly DispatcherTimer _keyTimer = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(25) };
    private uint _modifiers;
    private uint _virtualKey;
    private bool _keyWasDown;
    private IntPtr _lastForeground;
    private bool _gameForeground;
    private readonly HotkeyActivationGate _activationGate = new();

    public OverlayCoordinator() => _keyTimer.Tick += PollGameHotkey;

    public void Attach(MainViewModel viewModel, Window owner)
    {
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelChanged;
        DetachWindowSource();
        _viewModel = viewModel;
        _owner = owner;
        _viewModel.PropertyChanged += OnViewModelChanged;

        _ownerHandle = new WindowInteropHelper(owner).Handle;
        if (_ownerHandle == IntPtr.Zero) owner.SourceInitialized += OnOwnerSourceInitialized;
        else AttachWindowSource();
        Reevaluate();
    }

    private void OnOwnerSourceInitialized(object? sender, EventArgs e)
    {
        if (_owner is not null) _owner.SourceInitialized -= OnOwnerSourceInitialized;
        if (_owner is null) return;
        _ownerHandle = new WindowInteropHelper(_owner).Handle;
        AttachWindowSource();
    }

    private void AttachWindowSource()
    {
        if (_ownerHandle == IntPtr.Zero) return;
        _source = HwndSource.FromHwnd(_ownerHandle);
        _source?.AddHook(WindowMessageHook);
        RegisterSelectedHotkey();
        _keyTimer.Start();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.OverlayEnabled) or nameof(MainViewModel.OverlayPreview)
            or nameof(MainViewModel.IsGameRunning) or nameof(MainViewModel.OverlayHotkeyVisible)
            or nameof(MainViewModel.OverlaySuppressed) or nameof(MainViewModel.IsShipDetectionCapturing)) Reevaluate();
        else if (e.PropertyName == nameof(MainViewModel.OverlayHotkey)) RegisterSelectedHotkey();
        else if (e.PropertyName == nameof(MainViewModel.IsOverlayHotkeyCapturing)) RegisterSelectedHotkey();
        else if (e.PropertyName is nameof(MainViewModel.OverlayExpanded) or nameof(MainViewModel.OverlayOpacity)
                 or nameof(MainViewModel.OverlayTextOpacity) or nameof(MainViewModel.OverlayScale)
                 or nameof(MainViewModel.OverlayAnchor) or nameof(MainViewModel.OverlayCustomLeft)
                 or nameof(MainViewModel.OverlayCustomTop) or nameof(MainViewModel.OverlayEditMode)
                 or nameof(MainViewModel.OverlayShowShip) or nameof(MainViewModel.OverlayShowLocation)
                 or nameof(MainViewModel.OverlayShowRoute) or nameof(MainViewModel.OverlayShowMission)
                 or nameof(MainViewModel.OverlayShowFreshness))
        {
            if (_window is not null && _viewModel is not null)
                _window.Dispatcher.BeginInvoke(() => _window.ApplySettings(_viewModel));
            Reevaluate();
        }
    }

    private void RegisterSelectedHotkey()
    {
        if (_viewModel is null || _owner is null) return;
        if (!_owner.Dispatcher.CheckAccess())
        {
            _owner.Dispatcher.BeginInvoke(RegisterSelectedHotkey);
            return;
        }
        UnregisterSelectedHotkey();
        _keyWasDown = true; // Wait for release after assigning a key.
        if (_viewModel.IsOverlayHotkeyCapturing) return;
        if (string.IsNullOrWhiteSpace(_viewModel.OverlayHotkey))
        {
            _viewModel.SetOverlayHotkeyRegistrationState("none");
            return;
        }
        if (_ownerHandle == IntPtr.Zero ||
            !TryParseHotkey(_viewModel.OverlayHotkey, out var modifiers, out var virtualKey))
        {
            _viewModel.SetOverlayHotkeyRegistrationState("invalid");
            return;
        }
        _hotkeyRegistered = RegisterHotKey(_ownerHandle, HotkeyId, modifiers, virtualKey);
        _modifiers = modifiers;
        _virtualKey = virtualKey;
        _viewModel.SetOverlayHotkeyRegistrationState(_hotkeyRegistered
            ? "active"
            : Marshal.GetLastWin32Error() == 1409 ? "busy" : "invalid");
    }

    private void UnregisterSelectedHotkey()
    {
        if (!_hotkeyRegistered || _ownerHandle == IntPtr.Zero) return;
        UnregisterHotKey(_ownerHandle, HotkeyId);
        _hotkeyRegistered = false;
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam,
        ref bool handled)
    {
        if (message == WmHotkey && wParam.ToInt32() == HotkeyId && _viewModel is not null)
        {
            ActivateHotkey();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void PollGameHotkey(object? sender, EventArgs e)
    {
        var inGame = _viewModel?.IsGameRunning == true;
        _keyTimer.Interval = TimeSpan.FromMilliseconds(inGame ? 25 : 250);
        if (!_hotkeyRegistered || _viewModel is null || _viewModel.IsOverlayHotkeyCapturing) return;
        // Read only the assigned chord. Some games consume WM_HOTKEY; no keyboard hook is needed.
        var down = IsKeyDown((int)_virtualKey) && ModifiersMatch(_modifiers,
            IsKeyDown(0x11), IsKeyDown(0x12), IsKeyDown(0x10), IsKeyDown(0x5B) || IsKeyDown(0x5C));
        if (inGame && down && !_keyWasDown && IsGameForeground()) ActivateHotkey();
        _keyWasDown = down;
    }

    private bool IsGameForeground()
    {
        var foreground = GetForegroundWindow();
        if (foreground == _lastForeground) return _gameForeground;
        _lastForeground = foreground;
        _gameForeground = false;
        GetWindowThreadProcessId(foreground, out var processId);
        try
        {
            using var process = Process.GetProcessById((int)processId);
            _gameForeground = process.ProcessName.Equals("StarCitizen", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception) { }
        return _gameForeground;
    }

    private void ActivateHotkey()
    {
        if (_viewModel is { IsOverlayHotkeyCapturing: false } && _activationGate.TryActivate(Environment.TickCount64))
            _viewModel.ToggleOverlayFromHotkey();
    }

    internal static bool ModifiersMatch(uint modifiers, bool ctrl, bool alt, bool shift, bool win) =>
        ((modifiers & ModControl) != 0) == ctrl && ((modifiers & ModAlt) != 0) == alt &&
        ((modifiers & ModShift) != 0) == shift && ((modifiers & ModWin) != 0) == win;

    internal sealed class HotkeyActivationGate
    {
        private long? _lastActivation;
        public bool TryActivate(long timestamp)
        {
            if (_lastActivation is { } previous && timestamp - previous < 180) return false;
            _lastActivation = timestamp;
            return true;
        }
    }

    private static bool IsKeyDown(int key) => GetAsyncKeyState(key) < 0;

    internal static bool TryParseHotkey(string? value, out uint modifiers, out uint virtualKey)
    {
        modifiers = ModNoRepeat;
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || !Enum.TryParse<Key>(parts[^1], true, out var key) || key == Key.None)
            return false;
        foreach (var modifier in parts[..^1])
        {
            if (modifier.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) modifiers |= ModControl;
            else if (modifier.Equals("Alt", StringComparison.OrdinalIgnoreCase)) modifiers |= ModAlt;
            else if (modifier.Equals("Shift", StringComparison.OrdinalIgnoreCase)) modifiers |= ModShift;
            else if (modifier.Equals("Win", StringComparison.OrdinalIgnoreCase) ||
                     modifier.Equals("Windows", StringComparison.OrdinalIgnoreCase)) modifiers |= ModWin;
            else return false;
        }
        if (IsModifierKey(key)) return false;
        virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        return virtualKey != 0;
    }

    internal static bool TryFormatHotkey(Key key, ModifierKeys modifiers, out string value)
    {
        value = "";
        if (key is Key.None or Key.System or Key.ImeProcessed or Key.DeadCharProcessed || IsModifierKey(key))
            return false;

        var parts = new List<string>(5);
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        value = string.Join('+', parts);
        return TryParseHotkey(value, out _, out _);
    }

    private static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    private void Reevaluate()
    {
        if (_viewModel is null || System.Windows.Application.Current is null) return;
        if (!System.Windows.Application.Current.Dispatcher.CheckAccess())
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(Reevaluate);
            return;
        }
        var visible = _viewModel.OverlayPreview || _viewModel.OverlayEditMode || _viewModel.OverlayHotkeyVisible ||
            (_viewModel.OverlayEnabled && _viewModel.IsGameRunning && !_viewModel.OverlaySuppressed);
        if (!visible || _viewModel.IsShipDetectionCapturing)
        {
            _window?.Hide();
            return;
        }
        _window ??= new OverlayWindow { DataContext = _viewModel };
        _window.ApplySettings(_viewModel);
        if (!_window.IsVisible) _window.Show();
        _window.PositionAtWorkAreaEdge();
    }

    private void DetachWindowSource()
    {
        _keyTimer.Stop();
        if (_owner is not null) _owner.SourceInitialized -= OnOwnerSourceInitialized;
        UnregisterSelectedHotkey();
        _source?.RemoveHook(WindowMessageHook);
        _source = null;
        _owner = null;
        _ownerHandle = IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelChanged;
        DetachWindowSource();
        if (_window is not null)
        {
            _window.Close();
            _window = null;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
