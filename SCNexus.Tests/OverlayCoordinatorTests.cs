using SCNexus.Services;
using System.Windows.Input;

namespace SCNexus.Tests;

public class OverlayCoordinatorTests
{
    [Fact]
    public void GamePollingAndWindowsMessageDoNotToggleTwiceForOnePress()
    {
        var gate = new OverlayCoordinator.HotkeyActivationGate();
        Assert.True(gate.TryActivate(1000));
        Assert.False(gate.TryActivate(1025));
        Assert.False(gate.TryActivate(1050));
        Assert.True(gate.TryActivate(1300));
    }

    [Theory]
    [InlineData(0x4000u, false, false, false, false, true)]
    [InlineData(0x4000u, true, false, false, false, false)]
    [InlineData(0x4006u, true, false, true, false, true)]
    [InlineData(0x4006u, true, true, true, false, false)]
    [InlineData(0x4006u, true, false, false, false, false)]
    public void PollingRequiresExactlyTheAssignedModifiers(uint flags, bool ctrl, bool alt, bool shift, bool win, bool expected)
        => Assert.Equal(expected, OverlayCoordinator.ModifiersMatch(flags, ctrl, alt, shift, win));

    [Theory]
    [InlineData("F9", 0x4000u, 0x78u)]
    [InlineData("Ctrl+Shift+O", 0x4006u, 0x4Fu)]
    [InlineData("Ctrl+Alt+O", 0x4003u, 0x4Fu)]
    [InlineData("O", 0x4000u, 0x4Fu)]
    [InlineData("Win+Ctrl+K", 0x400Au, 0x4Bu)]
    public void ParsesSupportedOverlayHotkeys(string value, uint modifiers, uint virtualKey)
    {
        Assert.True(OverlayCoordinator.TryParseHotkey(value, out var actualModifiers, out var actualKey));
        Assert.Equal(modifiers, actualModifiers);
        Assert.Equal(virtualKey, actualKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+Banana+O")]
    public void RejectsUnsafeOrInvalidOverlayHotkeys(string value)
    {
        Assert.False(OverlayCoordinator.TryParseHotkey(value, out _, out _));
    }

    [Fact]
    public void FormatsCapturedKeyboardShortcut()
    {
        Assert.True(OverlayCoordinator.TryFormatHotkey(
            Key.OemPlus,
            ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Windows,
            out var value));
        Assert.Equal("Ctrl+Shift+Win+OemPlus", value);
        Assert.True(OverlayCoordinator.TryParseHotkey(value, out _, out _));
    }
}
