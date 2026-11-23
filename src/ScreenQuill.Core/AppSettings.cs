using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScreenQuill.Core;

/// <summary>All persisted preferences. Serialised as JSON in %APPDATA%\ScreenQuill\settings.json.</summary>
public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;

    // Tools
    public double PenSize { get; set; } = 4;
    public double HighlighterSize { get; set; } = 22;
    public double EraserSize { get; set; } = 20;
    public double TextSize { get; set; } = 28;
    public double ShapeSize { get; set; } = 4;
    public uint Color { get; set; } = Palette.DefaultColor;
    public List<uint> RecentColors { get; set; } = Palette.DefaultRecent.ToList();
    public List<uint> CustomColors { get; set; } = new();
    public double HighlighterOpacity { get; set; } = 0.45;
    public bool UsePenPressure { get; set; } = true;
    public bool SmoothInk { get; set; } = true;

    // Fading ink — project defaults (Epic Pen documents "short, medium or long" without numbers)
    public FadeMode FadeMode { get; set; } = FadeMode.Off;
    public double FadeShortSeconds { get; set; } = 2;
    public double FadeMediumSeconds { get; set; } = 5;
    public double FadeLongSeconds { get; set; } = 10;
    public double FadeOutSeconds { get; set; } = 1;

    // Highlighted cursor
    public uint CursorHighlightColor { get; set; } = 0xFFFFEB3B;
    public double CursorHighlightRadius { get; set; } = 32;
    public double CursorHighlightOpacity { get; set; } = 0.45;
    public bool CursorHighlightRing { get; set; } = true;

    // Screenshots
    public string ScreenshotFolder { get; set; } = DefaultScreenshotFolder();
    public bool ScreenshotAlsoCopyToClipboard { get; set; } = true;
    public bool ScreenshotOpenFolderAfterSave { get; set; } = false;

    // Toolbar
    public double? ToolbarX { get; set; }
    public double? ToolbarY { get; set; }
    public bool StartInGhostMode { get; set; }
    public bool ShowTooltips { get; set; } = true;
    public bool HideInkWhenCursor { get; set; } = false;

    // Boards
    public bool BoardOnAllMonitors { get; set; } = true;

    // Startup
    public bool LaunchAtStartup { get; set; }
    public bool ShowTrayIcon { get; set; } = true;

    // Hotkeys stored as text ("Ctrl+Alt+P") so the file is human editable.
    public Dictionary<HotkeyAction, string> Hotkeys { get; set; } = HotkeyRules.Defaults().ToDictionary(k => k.Key, k => k.Value.ToString());

    public double FadeSeconds(FadeMode m) => m switch
    {
        FadeMode.Short => FadeShortSeconds,
        FadeMode.Medium => FadeMediumSeconds,
        FadeMode.Long => FadeLongSeconds,
        _ => 0
    };

    public Dictionary<HotkeyAction, Hotkey> GetHotkeys()
    {
        var result = HotkeyRules.Defaults();
        foreach (var kv in Hotkeys)
            if (Hotkey.TryParse(kv.Value, out var hk)) result[kv.Key] = hk;
        return result;
    }

    public void SetHotkeys(IReadOnlyDictionary<HotkeyAction, Hotkey> map) =>
        Hotkeys = map.ToDictionary(k => k.Key, k => k.Value.ToString());

    public static string DefaultScreenshotFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "ScreenQuill");

    /// <summary>Clamps values loaded from a hand-edited or older file into valid ranges.</summary>
    public void Sanitize()
    {
        PenSize = Math.Clamp(PenSize, ToolState.MinSize, ToolState.MaxSize);
        HighlighterSize = Math.Clamp(HighlighterSize, ToolState.MinSize, ToolState.MaxSize);
        EraserSize = Math.Clamp(EraserSize, ToolState.MinSize, ToolState.MaxSize);
        TextSize = Math.Clamp(TextSize, 8, ToolState.MaxSize);
        ShapeSize = Math.Clamp(ShapeSize, ToolState.MinSize, ToolState.MaxSize);
        HighlighterOpacity = Math.Clamp(HighlighterOpacity, 0.1, 0.9);
        FadeShortSeconds = Math.Clamp(FadeShortSeconds, 0.5, 600);
        FadeMediumSeconds = Math.Clamp(FadeMediumSeconds, 0.5, 600);
        FadeLongSeconds = Math.Clamp(FadeLongSeconds, 0.5, 600);
        FadeOutSeconds = Math.Clamp(FadeOutSeconds, 0, 10);
        CursorHighlightRadius = Math.Clamp(CursorHighlightRadius, 8, 200);
        CursorHighlightOpacity = Math.Clamp(CursorHighlightOpacity, 0.1, 1);
        if (!Enum.IsDefined(FadeMode)) FadeMode = FadeMode.Off;
        RecentColors ??= Palette.DefaultRecent.ToList();
        RecentColors = Palette.PushRecent(RecentColors.Skip(1), RecentColors.FirstOrDefault(Palette.DefaultRecent[0]));
        CustomColors ??= new();
        if (string.IsNullOrWhiteSpace(ScreenshotFolder)) ScreenshotFolder = DefaultScreenshotFolder();
        Hotkeys ??= new();
    }
}

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // Keep "+" in hotkey strings literal instead of the default "+" escape.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    public SettingsStore(string? path = null)
    {
        Path = path ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenQuill", "settings.json");
    }

    public string Path { get; }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path), Options);
                if (s != null) { s.Sanitize(); return s; }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Corrupt or unreadable: keep a copy for diagnosis and fall back to defaults.
            try { File.Copy(Path, Path + ".bad", true); } catch { /* ignore */ }
        }
        var d = new AppSettings();
        d.Sanitize();
        return d;
    }

    /// <summary>Writes atomically (temp file + replace) so a crash never leaves a truncated file.</summary>
    public void Save(AppSettings s)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var tmp = Path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(s, Options));
        if (File.Exists(Path)) File.Replace(tmp, Path, null);
        else File.Move(tmp, Path);
    }
}
