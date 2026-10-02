using SCNexus.Services;
using System.Windows.Input;

namespace SCNexus.Tests;

public class OverlayCoordinatorTests
{
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
