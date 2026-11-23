using System.Collections.Generic;
using System.Linq;
using ScreenQuill.Native;

namespace ScreenQuill.Services;

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;
}

/// <summary>A display in physical pixels (process is Per-Monitor-V2 DPI aware).</summary>
public sealed record MonitorInfo(string DeviceName, PixelRect Bounds, PixelRect WorkArea, double Scale, bool Primary);

public static class Monitors
{
    public static List<MonitorInfo> GetAll() =>
        System.Windows.Forms.Screen.AllScreens.Select(s =>
        {
            var b = s.Bounds; var w = s.WorkingArea;
            double scale = Win32.DpiScaleAt(b.X + b.Width / 2, b.Y + b.Height / 2);
            return new MonitorInfo(s.DeviceName, new PixelRect(b.X, b.Y, b.Width, b.Height),
                new PixelRect(w.X, w.Y, w.Width, w.Height), scale, s.Primary);
        }).ToList();

    /// <summary>Stable signature used to detect monitor reconfiguration.</summary>
    public static string Signature(IEnumerable<MonitorInfo> ms) =>
        string.Join(";", ms.Select(m => $"{m.DeviceName}:{m.Bounds}:{m.Scale}"));

    public static MonitorInfo FromPoint(IReadOnlyList<MonitorInfo> ms, int x, int y) =>
        ms.FirstOrDefault(m => m.Bounds.Contains(x, y)) ?? ms.FirstOrDefault(m => m.Primary) ?? ms[0];
}
