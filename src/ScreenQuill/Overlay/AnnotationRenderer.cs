using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ScreenQuill.Core;
using ScreenQuill.Core.Model;

namespace ScreenQuill.Overlay;

/// <summary>
/// Draws annotations in global physical-pixel space. Colours are drawn opaque and the element's alpha
/// (and highlighter translucency, and fade) is applied as visual opacity, so self-overlapping strokes
/// never double-darken and highlighter remains uniformly see-through.
/// </summary>
public static class AnnotationRenderer
{
    private static readonly Dictionary<uint, SolidColorBrush> Brushes = new();

    public static Color ToColor(uint argb) => Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    public static SolidColorBrush OpaqueBrush(uint argb)
    {
        uint key = argb | 0xFF000000;
        if (!Brushes.TryGetValue(key, out var b))
        {
            b = new SolidColorBrush(ToColor(key));
            b.Freeze();
            Brushes[key] = b;
        }
        return b;
    }

    /// <summary>Base opacity of an element before fading.</summary>
    public static double BaseOpacity(Annotation a, double highlighterOpacity)
    {
        double alpha = (a.Color >> 24) / 255.0;
        return a.Kind == AnnotationKind.Highlighter ? alpha * highlighterOpacity : alpha;
    }

    private static Pen MakePen(Brush b, double w, bool flatCaps = false)
    {
        var p = new Pen(b, Math.Max(0.5, w))
        {
            StartLineCap = flatCaps ? PenLineCap.Flat : PenLineCap.Round,
            EndLineCap = flatCaps ? PenLineCap.Flat : PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        p.Freeze();
        return p;
    }

    public static void Draw(DrawingContext dc, Annotation a, bool smooth)
    {
        var brush = OpaqueBrush(a.Color);
        switch (a)
        {
            case InkStroke s: DrawStroke(dc, s, brush, smooth); break;
            case ShapeAnnotation sh: DrawShape(dc, sh, brush); break;
            case TextAnnotation t: DrawText(dc, t, brush); break;
        }
    }

    private static void DrawStroke(DrawingContext dc, InkStroke s, Brush brush, bool smooth)
    {
        var pts = s.Points;
        if (pts.Count == 0) return;
        if (pts.Count == 1 || IsDot(s))
        {
            double r = s.Width * (s.HasPressure ? InkStroke.PressureScale(pts[0].Pressure) : 1) / 2;
            dc.DrawEllipse(brush, null, new Point(pts[0].X, pts[0].Y), r, r);
            return;
        }

        if (!s.HasPressure)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(pts[0].X, pts[0].Y), false, false);
                if (smooth && pts.Count > 2)
                {
                    // Quadratic Bézier through segment midpoints: smooth, passes near every sample, no overshoot.
                    for (int i = 1; i < pts.Count - 1; i++)
                    {
                        var c = new Point(pts[i].X, pts[i].Y);
                        var m = new Point((pts[i].X + pts[i + 1].X) / 2, (pts[i].Y + pts[i + 1].Y) / 2);
                        ctx.QuadraticBezierTo(c, m, true, true);
                    }
                    ctx.LineTo(new Point(pts[^1].X, pts[^1].Y), true, true);
                }
                else
                {
                    for (int i = 1; i < pts.Count; i++) ctx.LineTo(new Point(pts[i].X, pts[i].Y), true, true);
                }
            }
            g.Freeze();
            dc.DrawGeometry(null, MakePen(brush, s.Width), g);
            return;
        }

        // Variable width: union of round-capped segments. Drawn opaque so overlaps are invisible.
        var cache = new Dictionary<int, Pen>();
        for (int i = 1; i < pts.Count; i++)
        {
            float p = (pts[i - 1].Pressure + pts[i].Pressure) / 2;
            double w = s.Width * InkStroke.PressureScale(p);
            int key = (int)Math.Round(w * 4);
            if (!cache.TryGetValue(key, out var pen)) cache[key] = pen = MakePen(brush, key / 4.0);
            dc.DrawLine(pen, new Point(pts[i - 1].X, pts[i - 1].Y), new Point(pts[i].X, pts[i].Y));
        }
    }

    private static bool IsDot(InkStroke s)
    {
        var first = s.Points[0];
        foreach (var p in s.Points)
            if (Math.Abs(p.X - first.X) > 0.75 || Math.Abs(p.Y - first.Y) > 0.75) return false;
        return true;
    }

    private static void DrawShape(DrawingContext dc, ShapeAnnotation s, Brush brush)
    {
        var pen = MakePen(brush, s.Width);
        switch (s.Kind)
        {
            case AnnotationKind.Line:
                dc.DrawLine(pen, P(s.Start), P(s.End));
                break;
            case AnnotationKind.Arrow:
            {
                var (l, r) = GeometryMath.ArrowHead(s.Start, s.End, s.Width);
                // Shaft stops short of the tip so the filled head stays crisp at large widths.
                var d = s.End - s.Start; double len = d.Length;
                var mid = new Pt((l.X + r.X) / 2, (l.Y + r.Y) / 2);
                var shaftEnd = len > 1 ? mid : s.End;
                dc.DrawLine(pen, P(s.Start), P(shaftEnd));
                var head = new StreamGeometry();
                using (var ctx = head.Open())
                {
                    ctx.BeginFigure(P(s.End), true, true);
                    ctx.LineTo(P(l), true, true);
                    ctx.LineTo(P(r), true, true);
                }
                head.Freeze();
                dc.DrawGeometry(brush, MakePen(brush, Math.Max(1, s.Width * 0.5)), head);
                break;
            }
            case AnnotationKind.Rectangle:
            {
                var r = s.Rect;
                var rp = new Pen(brush, Math.Max(0.5, s.Width)) { LineJoin = PenLineJoin.Round };
                rp.Freeze();
                dc.DrawRectangle(null, rp, new Rect(r.X, r.Y, r.Width, r.Height));
                break;
            }
            case AnnotationKind.Ellipse:
            {
                var r = s.Rect;
                dc.DrawEllipse(null, pen, new Point(r.X + r.Width / 2, r.Y + r.Height / 2), r.Width / 2, r.Height / 2);
                break;
            }
        }
    }

    public static FormattedText Format(TextAnnotation t, Brush brush) =>
        new(string.IsNullOrEmpty(t.Text) ? " " : t.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily(t.FontFamily), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            Math.Max(4, t.Width), brush, 1.0);

    private static void DrawText(DrawingContext dc, TextAnnotation t, Brush brush)
    {
        var ft = Format(t, brush);
        t.MeasuredWidth = ft.WidthIncludingTrailingWhitespace;
        t.MeasuredHeight = ft.Height;
        dc.DrawText(ft, new Point(t.Position.X, t.Position.Y));
    }

    /// <summary>Measures text without drawing (used for hit-testing before the first render).</summary>
    public static void Measure(TextAnnotation t)
    {
        var ft = Format(t, OpaqueBrush(t.Color));
        t.MeasuredWidth = ft.WidthIncludingTrailingWhitespace;
        t.MeasuredHeight = ft.Height;
    }

    private static Point P(Pt p) => new(p.X, p.Y);
}
