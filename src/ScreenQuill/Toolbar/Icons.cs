using System;
using System.Windows;
using System.Windows.Media;
using ScreenQuill.Core;

namespace ScreenQuill.Toolbar;

/// <summary>
/// Vector toolbar icons drawn from scratch on a 24×24 grid to match the silhouettes in the reference
/// screenshots (dark outline, white/light-grey fills). No bitmap assets are extracted from Epic Pen.
/// </summary>
public static class Icons
{
    private static readonly Brush Ink = Frozen(new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x26)));
    private static readonly Brush Light = Frozen(new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)));
    private static readonly Brush White = Brushes.White;
    private static readonly Pen Outline = FrozenPen(Ink, 1.5);
    private static readonly Pen Thin = FrozenPen(Ink, 1.1);

    private static Brush Frozen(SolidColorBrush b) { b.Freeze(); return b; }
    private static Pen FrozenPen(Brush b, double w)
    {
        var p = new Pen(b, w) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        p.Freeze();
        return p;
    }

    private static GeometryDrawing G(string data, Brush? fill, Pen? pen) => new(fill, pen, Geometry.Parse(data));

    private static DrawingImage Image(params Drawing[] parts)
    {
        var g = new DrawingGroup();
        // Invisible frame keeps every icon on the same 24×24 box so they align identically.
        g.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 24, 24))));
        foreach (var p in parts) g.Children.Add(p);
        var img = new DrawingImage(g);
        img.Freeze();
        return img;
    }

    private static Drawing Rotated(double angle, params Drawing[] parts)
    {
        var g = new DrawingGroup { Transform = new RotateTransform(angle, 12, 12) };
        foreach (var p in parts) g.Children.Add(p);
        return g;
    }

    public static readonly DrawingImage Eye = Image(
        G("M1.8,12 C4.8,6.6 8.3,4.6 12,4.6 C15.7,4.6 19.2,6.6 22.2,12 C19.2,17.4 15.7,19.4 12,19.4 C8.3,19.4 4.8,17.4 1.8,12 Z", White, Outline),
        G("M12,7.4 A4.6,4.6 0 1 1 11.99,7.4 Z", Frozen(new SolidColorBrush(Color.FromRgb(0xC9, 0xA0, 0x8D))), Outline),
        G("M12,9.6 A2.4,2.4 0 1 1 11.99,9.6 Z", Ink, null),
        G("M10.2,9.6 A0.9,0.9 0 1 1 10.19,9.6 Z", White, null));

    public static readonly DrawingImage EyeClosed = Image(
        G("M1.8,12 C4.8,6.6 8.3,4.6 12,4.6 C15.7,4.6 19.2,6.6 22.2,12 C19.2,17.4 15.7,19.4 12,19.4 C8.3,19.4 4.8,17.4 1.8,12 Z", White, Outline),
        G("M12,7.4 A4.6,4.6 0 1 1 11.99,7.4 Z", Light, Outline),
        G("M3.5,20.5 L20.5,3.5", null, FrozenPen(Ink, 2)));

    public static readonly DrawingImage Cursor = Image(
        G("M6,2.8 L6,19.6 L10.1,15.7 L13.1,22 L15.9,20.7 L13,14.5 L18.6,14.5 Z", White, Outline));

    private static Drawing StopwatchBase() => new DrawingGroup
    {
        Children =
        {
            G("M12,6.2 A7.6,7.6 0 1 1 11.99,6.2 Z", White, Outline),
            G("M10.2,2.6 H13.8 M12,2.6 V6.2 M18.2,6.6 L19.6,5.2", null, Outline),
            G("M12,13.8 L12,9.4 M12,13.8 L14.8,15.4", null, Thin),
        }
    };

    public static readonly DrawingImage FadeOff = Image(StopwatchBase(), G("M3.2,22 L21,4.2", null, FrozenPen(Ink, 1.6)));
    public static readonly DrawingImage FadeOn = Image(StopwatchBase(),
        G("M12,13.8 L12,7.6 A6.2,6.2 0 0 1 17.6,11 Z", Frozen(new SolidColorBrush(Color.FromArgb(120, 0x2F, 0xC5, 0xEA))), null));

    /// <summary>Chunky marker (the pen button silhouette in the reference toolbar).</summary>
    public static readonly DrawingImage Pen = Image(Rotated(45,
        G("M8.6,1.8 H15.4 V15.2 H8.6 Z", White, Outline),
        G("M8.6,5 H15.4", null, Thin),
        G("M8.6,15.2 L15.4,15.2 L13.8,19.4 L10.2,19.4 Z", Light, Outline),
        G("M10.2,19.4 L11.2,22.4 L12.8,22.4 L13.8,19.4", Ink, Thin)));

    public static readonly DrawingImage Highlighter = Image(Rotated(45,
        G("M7.6,1.6 H16.4 V13.6 H7.6 Z", White, Outline),
        G("M9.2,3.6 H14.8 V11.6 H9.2 Z", Frozen(new SolidColorBrush(Color.FromRgb(0xFD, 0xF2, 0x03))), Thin),
        G("M7.6,13.6 L16.4,13.6 L15,17.4 L9,17.4 Z", Light, Outline),
        G("M9.6,17.4 L9.6,21.6 L14.4,20.2 L14.4,17.4", Frozen(new SolidColorBrush(Color.FromRgb(0xFD, 0xF2, 0x03))), Outline)));

    public static readonly DrawingImage Text = Image(
        G("M4.5,4 H19.5 V8 H18 L17.3,6.2 H13.6 V18.6 L15.8,19.2 V20.4 H8.2 V19.2 L10.4,18.6 V6.2 H6.7 L6,8 H4.5 Z", White, Outline));

    public static readonly DrawingImage Line = Image(
        G("M3.6,18.6 L18.6,3.6 L20.4,5.4 L5.4,20.4 Z", White, Outline));

    public static readonly DrawingImage Arrow = Image(
        G("M4,20 L17.2,6.8", null, FrozenPen(Ink, 1.8)),
        G("M20.4,3.6 L18.6,12 L12,5.4 Z", Ink, Thin));

    public static readonly DrawingImage Rectangle = Image(G("M3.5,6 H20.5 V18 H3.5 Z", White, FrozenPen(Ink, 1.8)));
    public static readonly DrawingImage Ellipse = Image(G("M12,5.5 A8.8,6.5 0 1 1 11.99,5.5 Z", White, FrozenPen(Ink, 1.8)));

    public static readonly DrawingImage Eraser = Image(Rotated(-45,
        G("M5,7.5 H19 V16.5 H5 Z", White, Outline),
        G("M12.4,7.5 H19 V16.5 H12.4 Z", Light, Outline),
        G("M6.6,16.5 V18.2 H17.4 V16.5", null, Thin)));

    public static readonly DrawingImage Undo = Image(
        G("M9.2,4.2 L3.4,9.6 L9.2,15 V11.6 H13.6 C16.2,11.6 17.4,13 17.4,15 C17.4,17 16.2,18.4 13.6,18.4 H11.4 V21 H13.8 C18,21 20.4,18.6 20.4,15 C20.4,11.4 18,9 13.8,9 H9.2 Z", White, Outline));

    public static readonly DrawingImage Redo = Image(new DrawingGroup
    {
        Transform = new ScaleTransform(-1, 1, 12, 12),
        Children = { G("M9.2,4.2 L3.4,9.6 L9.2,15 V11.6 H13.6 C16.2,11.6 17.4,13 17.4,15 C17.4,17 16.2,18.4 13.6,18.4 H11.4 V21 H13.8 C18,21 20.4,18.6 20.4,15 C20.4,11.4 18,9 13.8,9 H9.2 Z", White, Outline) }
    });

    public static readonly DrawingImage Trash = Image(
        G("M4,6.2 H20 V8.4 H4 Z", White, Outline),
        G("M9.2,6.2 V3.4 H14.8 V6.2", null, Outline),
        G("M5.6,8.4 L6.6,21 H17.4 L18.4,8.4 Z", White, Outline),
        G("M9.6,10.8 V18.4 M14.4,10.8 V18.4", null, Thin));

    public static readonly DrawingImage Board = Image(
        G("M10,3.4 H14 V5 H10 Z", White, Thin),
        G("M3.8,5 H20.2 V15.6 H3.8 Z", White, Outline),
        G("M5.8,7 H18.2 V13.6 H5.8 Z", Light, Thin),
        G("M7,15.6 L5.6,21.4 M17,15.6 L18.4,21.4 M12,15.6 V20", null, Outline));

    public static readonly DrawingImage Whiteboard = Image(
        G("M3.8,5 H20.2 V15.6 H3.8 Z", White, Outline),
        G("M7,15.6 L5.6,21.4 M17,15.6 L18.4,21.4", null, Outline));

    public static readonly DrawingImage Blackboard = Image(
        G("M3.8,5 H20.2 V15.6 H3.8 Z", Ink, Outline),
        G("M6.4,9.6 C8,8 9.6,11.6 11.4,9.6", null, FrozenPen(White, 1.1)),
        G("M7,15.6 L5.6,21.4 M17,15.6 L18.4,21.4", null, Outline));

    public static readonly DrawingImage Desktop = Image(
        G("M3,4.6 H21 V16.4 H3 Z", White, Outline),
        G("M9,20.4 H15 M12,16.4 V20.4", null, Outline));

    public static readonly DrawingImage Camera = Image(
        G("M2.8,7.4 H21.2 V19.6 H2.8 Z", White, Outline),
        G("M7.6,7.4 L9,4.6 H15 L16.4,7.4", White, Outline),
        G("M11.6,8.8 A4.8,4.8 0 1 1 11.59,8.8 Z", White, Outline),
        G("M11.6,10.8 A2.8,2.8 0 1 1 11.59,10.8 Z", Ink, null),
        G("M10.6,11.8 A0.8,0.8 0 1 1 10.59,11.8 Z", White, null),
        G("M17.4,9.4 H19.4 V10.8 H17.4 Z", White, Thin));

    public static readonly DrawingImage Clipboard = Image(
        G("M5,4.4 H19 V21.6 H5 Z", White, Outline),
        G("M8.8,2.6 H15.2 V6.2 H8.8 Z", White, Outline),
        G("M8,10 H16 M8,13.2 H16 M8,16.4 H16", null, Outline));

    public static readonly DrawingImage CursorHighlight = Image(
        G("M12,2 A10,10 0 1 1 11.99,2 Z", Frozen(new SolidColorBrush(Color.FromArgb(170, 0xFF, 0xEB, 0x3B))), Thin),
        G("M9,6 L9,18.4 L12,15.6 L14.2,20.2 L16.2,19.3 L14.1,14.8 L18.2,14.8 Z", White, Thin));

    public static readonly DrawingImage Settings = Image(
        G("M10.4,2.6 H13.6 L14.2,5.2 L16.4,6.2 L18.7,4.8 L21,7.1 L19.6,9.4 L20.4,11.6 L23,12.2 V15.4 L20.4,16 L19.6,18.2 L21,20.5 L18.7,22.8 L16.4,21.4 L14.2,22.4 L13.6,25 H10.4 L9.8,22.4 L7.6,21.4 L5.3,22.8 L3,20.5 L4.4,18.2 L3.6,16 L1,15.4 V12.2 L3.6,11.6 L4.4,9.4 L3,7.1 L5.3,4.8 L7.6,6.2 L9.8,5.2 Z",
            White, Outline) is var gear ? new DrawingGroup { Transform = new ScaleTransform(0.86, 0.86, 12, 13.8), Children = { gear, G("M12,10.4 A3.4,3.4 0 1 1 11.99,10.4 Z", Light, Outline) } } : null!);

    public static readonly DrawingImage Ghost = Image(
        G("M5,20.6 V10.4 C5,6.4 8,3.4 12,3.4 C16,3.4 19,6.4 19,10.4 V20.6 L16.6,18.6 L14.3,20.6 L12,18.6 L9.7,20.6 L7.4,18.6 Z", White, Outline),
        G("M9.6,9.4 A1.2,1.6 0 1 1 9.59,9.4 Z M14.4,9.4 A1.2,1.6 0 1 1 14.39,9.4 Z", Ink, null));

    public static readonly DrawingImage Folder = Image(
        G("M2.8,6 H9.4 L11.2,8 H21.2 V19.4 H2.8 Z", White, Outline));

    public static readonly DrawingImage Exit = Image(
        G("M6.2,6.2 L17.8,17.8 M17.8,6.2 L6.2,17.8", null, FrozenPen(Ink, 2)));

    public static readonly DrawingImage Help = Image(
        G("M12,2.6 A9.4,9.4 0 1 1 11.99,2.6 Z", White, Outline),
        G("M9.2,9.4 C9.2,7.6 10.4,6.6 12,6.6 C13.6,6.6 14.8,7.6 14.8,9.2 C14.8,11.4 12,11.6 12,14", null, FrozenPen(Ink, 1.6)),
        G("M12,16.6 A1,1 0 1 1 11.99,16.6 Z", Ink, null));

    public static readonly DrawingImage Check = Image(G("M5,12.6 L9.8,17.2 L19,7.4", null, FrozenPen(Ink, 2.2)));

    /// <summary>Round black logo with a white pen nib (drawn independently; not the Epic Pen asset).</summary>
    public static readonly DrawingImage Logo = Image(
        // Light rim keeps the black logo visible on the blackboard and dark apps.
        G("M12,0.5 A11.5,11.5 0 1 1 11.99,0.5 Z", Frozen(new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11))), FrozenPen(Frozen(new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xD8))), 0.7)),
        G("M12,4.6 L15.2,8.2 L16.6,12.2 L13.6,18.8 L10.4,18.8 L7.4,12.2 L8.8,8.2 Z", White, null),
        G("M12,10 A1.6,1.6 0 1 1 11.99,10 Z", Frozen(new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11))), null),
        G("M12,11.6 V18.8", null, FrozenPen(Frozen(new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11))), 0.9)));

    public static DrawingImage For(ToolKind t) => t switch
    {
        ToolKind.Cursor => Cursor,
        ToolKind.Pen => Pen,
        ToolKind.Highlighter => Highlighter,
        ToolKind.Eraser => Eraser,
        ToolKind.Text => Text,
        ToolKind.Line => Line,
        ToolKind.Arrow => Arrow,
        ToolKind.Rectangle => Rectangle,
        ToolKind.Ellipse => Ellipse,
        _ => Pen,
    };

    /// <summary>The brush-size indicator: a filled dot whose diameter tracks the current size.</summary>
    public static DrawingImage SizeDot(double size, Color color)
    {
        double d = Math.Clamp(3 + Math.Sqrt(size) * 2.2, 4, 21);
        var b = new SolidColorBrush(color); b.Freeze();
        bool light = color.R * 0.3 + color.G * 0.59 + color.B * 0.11 > 200;
        return Image(new GeometryDrawing(b, light ? Thin : null, new EllipseGeometry(new Point(12, 12), d / 2, d / 2)));
    }
}
