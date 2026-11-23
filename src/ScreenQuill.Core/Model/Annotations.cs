namespace ScreenQuill.Core.Model;

public enum AnnotationKind { Pen, Highlighter, Line, Arrow, Rectangle, Ellipse, Text }

/// <summary>A pressure-aware input sample in global physical pixels. Pressure is 0..1 (0.5 when unknown).</summary>
public readonly record struct InkPoint(double X, double Y, float Pressure = 0.5f)
{
    public Pt P => new(X, Y);
}

/// <summary>Base of every retained annotation element. Elements are never burned into a bitmap.</summary>
public abstract class Annotation
{
    private static long _nextId;
    public long Id { get; } = Interlocked.Increment(ref _nextId);
    public abstract AnnotationKind Kind { get; }
    /// <summary>Colour as 0xAARRGGBB. Alpha is applied to the whole element (so self-overlap never darkens).</summary>
    public uint Color { get; set; }
    /// <summary>Stroke width (or font size for text) in global physical pixels.</summary>
    public double Width { get; set; }
    /// <summary>Monotonic creation time in seconds (see <see cref="AnnotationDocument.Clock"/>).</summary>
    public double CreatedAt { get; set; }
    /// <summary>When non-null the element fades: fully visible for Hold seconds, then fades over FadeOut seconds and is removed.</summary>
    public FadeSpec? Fade { get; set; }
    /// <summary>Bumped whenever the element's geometry/appearance changes so renderers can rebuild.</summary>
    public int Version { get; private set; }
    public void Touch() => Version++;

    public abstract RectD Bounds { get; }
    public abstract bool HitTest(Pt p, double tolerance);
    public abstract Annotation Clone();

    protected T CopyBase<T>(T a) where T : Annotation
    {
        a.Color = Color; a.Width = Width; a.CreatedAt = CreatedAt; a.Fade = Fade;
        return a;
    }
}

public sealed record FadeSpec(double Hold, double FadeOut)
{
    public double Total => Hold + FadeOut;

    /// <summary>Opacity multiplier at the given age; 0 when expired.</summary>
    public double OpacityAt(double age)
    {
        if (age <= Hold) return 1;
        if (FadeOut <= 0) return 0;
        double t = (age - Hold) / FadeOut;
        return t >= 1 ? 0 : 1 - t;
    }
}

public sealed class InkStroke : Annotation
{
    private readonly AnnotationKind _kind;
    public InkStroke(bool highlighter) => _kind = highlighter ? AnnotationKind.Highlighter : AnnotationKind.Pen;
    public override AnnotationKind Kind => _kind;
    public bool IsHighlighter => _kind == AnnotationKind.Highlighter;
    public List<InkPoint> Points { get; } = new();
    /// <summary>True when samples carried real pressure; otherwise the stroke has uniform width.</summary>
    public bool HasPressure { get; set; }

    public override RectD Bounds => GeometryMath.Bounds(Points.Select(p => p.P)).Inflate(Width / 2 + 1);

    public override bool HitTest(Pt p, double tolerance)
    {
        double tol = tolerance + Width / 2;
        if (!Bounds.Inflate(tolerance).Contains(p)) return false;
        return GeometryMath.PolylineHit(Points.Select(x => x.P).ToList(), p, tol);
    }

    public override Annotation Clone()
    {
        var s = CopyBase(new InkStroke(IsHighlighter));
        s.HasPressure = HasPressure;
        s.Points.AddRange(Points);
        return s;
    }

    /// <summary>Width multiplier for a pressure sample (0.25..1.5 × base width).</summary>
    public static double PressureScale(float pressure) => 0.25 + 1.25 * Math.Clamp(pressure, 0f, 1f);
}

public sealed class ShapeAnnotation : Annotation
{
    private readonly AnnotationKind _kind;
    public ShapeAnnotation(AnnotationKind kind)
    {
        if (kind is not (AnnotationKind.Line or AnnotationKind.Arrow or AnnotationKind.Rectangle or AnnotationKind.Ellipse))
            throw new ArgumentException("Not a shape kind", nameof(kind));
        _kind = kind;
    }
    public override AnnotationKind Kind => _kind;
    public Pt Start { get; set; }
    public Pt End { get; set; }

    public RectD Rect => GeometryMath.NormalizeRect(Start, End);

    /// <summary>The outline of the shape as polylines (used for hit testing).</summary>
    public IEnumerable<IReadOnlyList<Pt>> Outline()
    {
        switch (_kind)
        {
            case AnnotationKind.Line:
                yield return new[] { Start, End };
                break;
            case AnnotationKind.Arrow:
                yield return new[] { Start, End };
                var (l, r) = GeometryMath.ArrowHead(Start, End, Width);
                yield return new[] { l, End, r };
                break;
            case AnnotationKind.Rectangle:
                var rc = Rect;
                yield return new[] { new Pt(rc.X, rc.Y), new Pt(rc.Right, rc.Y), new Pt(rc.Right, rc.Bottom), new Pt(rc.X, rc.Bottom), new Pt(rc.X, rc.Y) };
                break;
            case AnnotationKind.Ellipse:
                yield return GeometryMath.EllipsePolyline(Rect);
                break;
        }
    }

    public override RectD Bounds => GeometryMath.Bounds(Outline().SelectMany(o => o)).Inflate(Width / 2 + 1);

    public override bool HitTest(Pt p, double tolerance) =>
        Outline().Any(o => GeometryMath.PolylineHit(o, p, tolerance + Width / 2));

    public override Annotation Clone()
    {
        var s = CopyBase(new ShapeAnnotation(_kind));
        s.Start = Start; s.End = End;
        return s;
    }
}

public sealed class TextAnnotation : Annotation
{
    public override AnnotationKind Kind => AnnotationKind.Text;
    /// <summary>Top-left of the text block in global physical pixels.</summary>
    public Pt Position { get; set; }
    public string Text { get; set; } = "";
    public string FontFamily { get; set; } = "Segoe UI";
    /// <summary>Layout size in physical pixels, filled in by the renderer after measuring.</summary>
    public double MeasuredWidth { get; set; }
    public double MeasuredHeight { get; set; }

    public override RectD Bounds => new(Position.X, Position.Y, Math.Max(MeasuredWidth, Width * 0.6), Math.Max(MeasuredHeight, Width * 1.2));
    public override bool HitTest(Pt p, double tolerance) => Bounds.Inflate(tolerance).Contains(p);

    public override Annotation Clone()
    {
        var t = CopyBase(new TextAnnotation());
        t.Position = Position; t.Text = Text; t.FontFamily = FontFamily;
        t.MeasuredWidth = MeasuredWidth; t.MeasuredHeight = MeasuredHeight;
        return t;
    }
}
