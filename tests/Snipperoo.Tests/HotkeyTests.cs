using System.Windows.Forms;
using Snipperoo.Native;

namespace Snipperoo.Tests;

public class HotkeyTests
{
    [Theory]
    [InlineData("Alt+Shift+G", 0x1 | 0x4, Keys.G)]
    [InlineData("shift + alt + g", 0x1 | 0x4, Keys.G)]
    [InlineData("Win+Shift+G", 0x8 | 0x4, Keys.G)]
    [InlineData("Ctrl+F9", 0x2, Keys.F9)]
    public void Parses(string text, uint modifiers, Keys key)
    {
        Assert.Equal(new Hotkey(modifiers, key), Hotkey.Parse(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Alt+Shift")]
    [InlineData("Alt+G+H")]
    [InlineData("Alt+Banana")]
    public void Rejects_invalid(string text)
    {
        Assert.Throws<FormatException>(() => Hotkey.Parse(text));
    }

    [Fact]
    public void Round_trips_to_canonical_text()
    {
        Assert.Equal("Alt+Shift+G", Hotkey.Parse("shift+ALT+g").ToString());
    }
}
