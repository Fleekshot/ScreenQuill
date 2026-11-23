using System.Text.Json.Serialization;

namespace ScreenQuill.Core;

[Flags]
public enum HotkeyModifiers
{
    None = 0, Alt = 1, Control = 2, Shift = 4, Win = 8 // same values as Win32 MOD_*
}

public enum HotkeyAction
{
    Cursor, Pen, Highlighter, Eraser, Text, Line, Arrow, Rectangle, Ellipse,
    Screenshot, ScreenshotToClipboard, Clear, Undo, Redo,
    Whiteboard, Blackboard, CycleFadingInk, CursorHighlight, ToggleInkVisibility,
    GhostMode, IncreaseSize, DecreaseSize, NextColor, OpenSettings
}

/// <summary>A key chord: Win32 virtual-key code plus modifiers.</summary>
public readonly record struct Hotkey(HotkeyModifiers Modifiers, int VirtualKey)
{
    [JsonIgnore] public bool IsEmpty => VirtualKey == 0;
    public static readonly Hotkey None = new(HotkeyModifiers.None, 0);

    public override string ToString()
    {
        if (IsEmpty) return "None";
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(VirtualKeys.Name(VirtualKey));
        return string.Join("+", parts);
    }

    public static bool TryParse(string? text, out Hotkey hk)
    {
        hk = None;
        if (string.IsNullOrWhiteSpace(text) || text.Trim().Equals("None", StringComparison.OrdinalIgnoreCase)) return text != null;
        var mods = HotkeyModifiers.None;
        int vk = 0;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= HotkeyModifiers.Control; break;
                case "alt": mods |= HotkeyModifiers.Alt; break;
                case "shift": mods |= HotkeyModifiers.Shift; break;
                case "win": case "windows": mods |= HotkeyModifiers.Win; break;
                default:
                    if (vk != 0) return false;
                    vk = VirtualKeys.Parse(raw);
                    if (vk == 0) return false;
                    break;
            }
        }
        if (vk == 0) return false;
        hk = new Hotkey(mods, vk);
        return true;
    }
}

public static class VirtualKeys
{
    private static readonly Dictionary<int, string> Names = new()
    {
        [0x08] = "Backspace", [0x09] = "Tab", [0x0D] = "Enter", [0x13] = "Pause", [0x1B] = "Esc", [0x20] = "Space",
        [0x21] = "PageUp", [0x22] = "PageDown", [0x23] = "End", [0x24] = "Home",
        [0x25] = "Left", [0x26] = "Up", [0x27] = "Right", [0x28] = "Down",
        [0x2C] = "PrintScreen", [0x2D] = "Insert", [0x2E] = "Delete",
        [0xBA] = ";", [0xBB] = "=", [0xBC] = ",", [0xBD] = "-", [0xBE] = ".", [0xBF] = "/", [0xC0] = "`",
        [0xDB] = "[", [0xDC] = "\\", [0xDD] = "]", [0xDE] = "'",
        [0x6A] = "Num*", [0x6B] = "Num+", [0x6D] = "Num-", [0x6E] = "Num.", [0x6F] = "Num/",
    };

    public static string Name(int vk)
    {
        if (vk is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A) return ((char)vk).ToString();
        if (vk is >= 0x70 and <= 0x87) return "F" + (vk - 0x6F);
        if (vk is >= 0x60 and <= 0x69) return "Num" + (vk - 0x60);
        return Names.TryGetValue(vk, out var n) ? n : $"VK{vk:X2}";
    }

    public static int Parse(string s)
    {
        if (s.Length == 1 && char.IsLetterOrDigit(s[0])) return char.ToUpperInvariant(s[0]);
        if (s.Length >= 2 && (s[0] == 'F' || s[0] == 'f') && int.TryParse(s[1..], out var f) && f is >= 1 and <= 24) return 0x6F + f;
        if (s.StartsWith("Num", StringComparison.OrdinalIgnoreCase) && s.Length == 4 && char.IsDigit(s[3])) return 0x60 + (s[3] - '0');
        foreach (var kv in Names) if (string.Equals(kv.Value, s, StringComparison.OrdinalIgnoreCase)) return kv.Key;
        if (s.StartsWith("VK", StringComparison.OrdinalIgnoreCase) && int.TryParse(s[2..], System.Globalization.NumberStyles.HexNumber, null, out var v)) return v;
        return 0;
    }

    public static bool IsModifierKey(int vk) => vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or >= 0xA0 and <= 0xA5;
}

public enum HotkeyProblem { None, Duplicate, Reserved, NoModifier }

public static class HotkeyRules
{
    /// <summary>
    /// Project defaults. Epic Pen does not publish its default bindings (see docs/feature-matrix.md),
    /// so these are ScreenQuill's own choices: Ctrl+Alt+letter avoids common browser/editor Ctrl+Shift shortcuts.
    /// </summary>
    public static Dictionary<HotkeyAction, Hotkey> Defaults() => new()
    {
        [HotkeyAction.Cursor] = K('C'),
        [HotkeyAction.Pen] = K('P'),
        [HotkeyAction.Highlighter] = K('H'),
        [HotkeyAction.Eraser] = K('E'),
        [HotkeyAction.Text] = K('T'),
        [HotkeyAction.Line] = K('L'),
        [HotkeyAction.Arrow] = K('A'),
        [HotkeyAction.Rectangle] = K('R'),
        [HotkeyAction.Ellipse] = K('O'),
        [HotkeyAction.Screenshot] = K('S'),
        [HotkeyAction.ScreenshotToClipboard] = K('X'),
        [HotkeyAction.Clear] = K('D'),
        [HotkeyAction.Undo] = K('Z'),
        [HotkeyAction.Redo] = K('Y'),
        [HotkeyAction.Whiteboard] = K('W'),
        [HotkeyAction.Blackboard] = K('B'),
        [HotkeyAction.CycleFadingInk] = K('F'),
        [HotkeyAction.CursorHighlight] = K('M'),
        [HotkeyAction.ToggleInkVisibility] = K('V'),
        [HotkeyAction.GhostMode] = K('G'),
        [HotkeyAction.IncreaseSize] = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0xBB), // Ctrl+Alt+=
        [HotkeyAction.DecreaseSize] = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0xBD), // Ctrl+Alt+-
        [HotkeyAction.NextColor] = K('N'),
        [HotkeyAction.OpenSettings] = Hotkey.None,
    };

    private static Hotkey K(char c) => new(HotkeyModifiers.Control | HotkeyModifiers.Alt, c);

    /// <summary>Combinations reserved by Windows that RegisterHotKey cannot or should not take.</summary>
    public static bool IsReserved(Hotkey h)
    {
        var m = h.Modifiers; int vk = h.VirtualKey;
        if (m == (HotkeyModifiers.Control | HotkeyModifiers.Alt) && vk == 0x2E) return true;   // Ctrl+Alt+Del
        if (m == (HotkeyModifiers.Control | HotkeyModifiers.Shift) && vk == 0x1B) return true; // Ctrl+Shift+Esc
        if (m == HotkeyModifiers.Control && vk == 0x1B) return true;                          // Ctrl+Esc
        if (m == HotkeyModifiers.Alt && vk is 0x09 or 0x73 or 0x1B or 0x20) return true;       // Alt+Tab/F4/Esc/Space
        if (m.HasFlag(HotkeyModifiers.Win)) return true;                                      // Win+* is shell territory
        if (m == HotkeyModifiers.None && vk == 0x2C) return false;                            // PrintScreen alone allowed
        return false;
    }

    /// <summary>Validates one binding against the others. Empty bindings are always valid.</summary>
    public static HotkeyProblem Validate(HotkeyAction action, Hotkey h, IReadOnlyDictionary<HotkeyAction, Hotkey> all)
    {
        if (h.IsEmpty) return HotkeyProblem.None;
        if (IsReserved(h)) return HotkeyProblem.Reserved;
        bool isFKey = h.VirtualKey is >= 0x70 and <= 0x87 || h.VirtualKey is 0x2C or 0x13;
        if (h.Modifiers == HotkeyModifiers.None && !isFKey) return HotkeyProblem.NoModifier; // would break normal typing
        foreach (var kv in all)
            if (kv.Key != action && kv.Value == h) return HotkeyProblem.Duplicate;
        return HotkeyProblem.None;
    }

    public static IEnumerable<(HotkeyAction A, HotkeyAction B)> FindDuplicates(IReadOnlyDictionary<HotkeyAction, Hotkey> all)
    {
        var list = all.Where(k => !k.Value.IsEmpty).ToList();
        for (int i = 0; i < list.Count; i++)
            for (int j = i + 1; j < list.Count; j++)
                if (list[i].Value == list[j].Value) yield return (list[i].Key, list[j].Key);
    }

    public static string Describe(HotkeyAction a) => a switch
    {
        HotkeyAction.Cursor => "Cursor (click-through)",
        HotkeyAction.ScreenshotToClipboard => "Screenshot to clipboard",
        HotkeyAction.Screenshot => "Screenshot (save to folder)",
        HotkeyAction.Clear => "Clear all",
        HotkeyAction.CycleFadingInk => "Fading ink (cycle Off/Short/Medium/Long)",
        HotkeyAction.CursorHighlight => "Highlighted cursor",
        HotkeyAction.ToggleInkVisibility => "Show / hide ink",
        HotkeyAction.GhostMode => "Ghost Mode (hide / restore toolbar)",
        HotkeyAction.IncreaseSize => "Increase size",
        HotkeyAction.DecreaseSize => "Decrease size",
        HotkeyAction.NextColor => "Next quick colour",
        HotkeyAction.OpenSettings => "Open Settings",
        HotkeyAction.Undo => "Undo (supplemental)",
        HotkeyAction.Redo => "Redo (supplemental)",
        _ => a.ToString()
    };
}
