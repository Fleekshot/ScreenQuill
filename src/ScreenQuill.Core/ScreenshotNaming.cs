namespace ScreenQuill.Core;

public static class ScreenshotNaming
{
    /// <summary>"ScreenQuill 2026-11-22 16-05-09.png", with " (2)", " (3)"… appended if the name exists.</summary>
    public static string NextPath(string folder, DateTime now, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        string stem = $"ScreenQuill {now:yyyy-MM-dd HH-mm-ss}";
        string path = Path.Combine(folder, stem + ".png");
        for (int i = 2; exists(path); i++) path = Path.Combine(folder, $"{stem} ({i}).png");
        return path;
    }

    /// <summary>Intersects a selection with the captured virtual-screen bounds; null if empty/too small.</summary>
    public static RectD? ClampSelection(RectD selection, RectD virtualScreen, double minSize = 2)
    {
        double x1 = Math.Max(selection.X, virtualScreen.X), y1 = Math.Max(selection.Y, virtualScreen.Y);
        double x2 = Math.Min(selection.Right, virtualScreen.Right), y2 = Math.Min(selection.Bottom, virtualScreen.Bottom);
        if (x2 - x1 < minSize || y2 - y1 < minSize) return null;
        return new RectD(Math.Floor(x1), Math.Floor(y1), Math.Ceiling(x2 - x1), Math.Ceiling(y2 - y1));
    }
}
