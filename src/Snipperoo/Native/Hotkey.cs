using System.Windows.Forms;

namespace Snipperoo.Native;

/// <summary>A global hotkey such as "Alt+Shift+G": RegisterHotKey modifier flags plus a virtual key.</summary>
internal readonly record struct Hotkey(uint Modifiers, Keys Key)
{
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8;

    /// <summary>Parses "Ctrl+Alt+Shift+Win+Key" in any order and case. Throws FormatException if invalid.</summary>
    public static Hotkey Parse(string text)
    {
        uint modifiers = 0;
        Keys? key = null;

        foreach (string part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= MOD_CONTROL; break;
                case "alt": modifiers |= MOD_ALT; break;
                case "shift": modifiers |= MOD_SHIFT; break;
                case "win" or "windows": modifiers |= MOD_WIN; break;
                default:
                    if (key is not null || !Enum.TryParse(part, ignoreCase: true, out Keys parsed) || parsed == Keys.None)
                        throw new FormatException($"Invalid hotkey '{text}'.");
                    key = parsed;
                    break;
            }
        }

        if (key is null)
            throw new FormatException($"Hotkey '{text}' has no key.");
        return new Hotkey(modifiers, key.Value);
    }

    /// <summary>Builds the RegisterHotKey modifier flags from individual keys.</summary>
    public static uint ModifierFlags(bool ctrl, bool alt, bool shift, bool win) =>
        (ctrl ? MOD_CONTROL : 0) | (alt ? MOD_ALT : 0) | (shift ? MOD_SHIFT : 0) | (win ? MOD_WIN : 0);

    public bool HasModifier => (Modifiers & (MOD_CONTROL | MOD_ALT | MOD_SHIFT | MOD_WIN)) != 0;

    /// <summary>Modifier names followed by the key, as stored in settings.</summary>
    public override string ToString() => string.Join('+', ModifierNames().Append(Key.ToString()));

    /// <summary>Labels for drawing the shortcut as keycaps, e.g. ["Alt", "Shift", "G"].</summary>
    public IReadOnlyList<string> DisplayParts => [.. ModifierNames(), KeyLabel(Key)];

    private IEnumerable<string> ModifierNames()
    {
        if ((Modifiers & MOD_CONTROL) != 0) yield return "Ctrl";
        if ((Modifiers & MOD_ALT) != 0) yield return "Alt";
        if ((Modifiers & MOD_SHIFT) != 0) yield return "Shift";
        if ((Modifiers & MOD_WIN) != 0) yield return "Win";
    }

    private static string KeyLabel(Keys key) => key switch
    {
        >= Keys.D0 and <= Keys.D9 => ((char)('0' + (key - Keys.D0))).ToString(),
        >= Keys.NumPad0 and <= Keys.NumPad9 => $"Num {key - Keys.NumPad0}",
        Keys.PrintScreen => "PrtSc",
        Keys.Return => "Enter",
        Keys.Back => "Backspace",
        Keys.Next => "PgDn",
        Keys.Prior => "PgUp",
        _ when Win32.KeyToChar(key) is { } c and not ' ' => char.ToUpperInvariant(c).ToString(),
        _ => key.ToString(),
    };
}
