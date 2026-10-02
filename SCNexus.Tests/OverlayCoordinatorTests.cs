using SCNexus.Services;

namespace SCNexus.Tests;

public class OverlayCoordinatorTests
{
    [Theory]
    [InlineData("F9", 0x4000u, 0x78u)]
    [InlineData("Ctrl+Shift+O", 0x4006u, 0x4Fu)]
    [InlineData("Ctrl+Alt+O", 0x4003u, 0x4Fu)]
    public void ParsesSupportedOverlayHotkeys(string value, uint modifiers, uint virtualKey)
    {
        Assert.True(OverlayCoordinator.TryParseHotkey(value, out var actualModifiers, out var actualKey));
        Assert.Equal(modifiers, actualModifiers);
        Assert.Equal(virtualKey, actualKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("O")]
    [InlineData("Ctrl+Banana+O")]
    public void RejectsUnsafeOrInvalidOverlayHotkeys(string value)
    {
        Assert.False(OverlayCoordinator.TryParseHotkey(value, out _, out _));
    }
}
