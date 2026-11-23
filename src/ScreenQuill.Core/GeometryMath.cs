namespace ScreenQuill.Core;

/// <summary>A point in global physical screen pixels (virtual-desktop coordinates, may be negative).</summary>
public readonly record struct Pt(double X, double Y)
{
    public static Pt operator +(Pt a, Pt b) => new(a.X + b.X, a.Y + b.Y);
    public static Pt operator -(Pt a, Pt b) => new(a.X - b.X, a.Y - b.Y);
    public static Pt operator *(Pt a, double s) => new(a.X * s, a.Y * s);
    public double Length => Math.Sqrt(X * X + Y * Y);
}

/// <summary>Axis-aligned rectangle in global physical pixels.</summary>
public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public bool Contains(Pt p) => p.X >= X && p.X <= Right && p.Y >= Y && p.Y <= Bottom;
    public RectD Inflate(double d) => new(X - d, Y - d, Width + 2 * d, Height + 2 * d);
    public bool IntersectsWith(RectD o) => o.X <= Right && o.Right >= X && o.Y <= Bottom && o.Bottom >= Y;
}

public static class GeometryMath
{
    public static double Distance(Pt a, Pt b) => (a - b).Length;

    public static double DistancePointToSegment(Pt p, Pt a, Pt b)
    {
        var ab = b - a;
        double len2 = ab.X * ab.X + ab.Y * ab.Y;
        if (len2 < 1e-12) return Distance(p, a);
        double t = ((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / len2;
        t = Math.Clamp(t, 0, 1);
        return Distance(p, new Pt(a.X + ab.X * t, a.Y + ab.Y * t));
    }

    /// <summary>Normalises a drag from any corner to any corner into a positive-size rectangle.</summary>
    public static RectD NormalizeRect(Pt a, Pt b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));

    /// <summary>Returns the two barb end points of an arrowhead at <paramref name="tip"/> pointing away from <paramref name="tail"/>.</summary>
    public static (Pt Left, Pt Right) ArrowHead(Pt tail, Pt tip, double strokeWidth)
    {
        var d = tip - tail;
        double len = d.Length;
        double headLen = Math.Max(12, strokeWidth * 4.0);
        if (len < 1e-6) return (tip, tip);
        headLen = Math.Min(headLen, len * 0.6 + strokeWidth * 2);
        var u = d * (1 / len);
        const double angle = 28 * Math.PI / 180; // half-angle of the head
        double c = Math.Cos(angle), s = Math.Sin(angle);
        var back = new Pt(-u.X, -u.Y);
        var l = new Pt(back.X * c - back.Y * s, back.X * s + back.Y * c) * headLen;
        var r = new Pt(back.X * c + back.Y * s, -back.X * s + back.Y * c) * headLen;
        return (tip + l, tip + r);
    }

    /// <summary>Approximates an ellipse inscribed in <paramref name="r"/> as a closed polyline.</summary>
    public static List<Pt> EllipsePolyline(RectD r, int segments = 72)
    {
        var pts = new List<Pt>(segments + 1);
        double cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, rx = r.Width / 2, ry = r.Height / 2;
        for (int i = 0; i <= segments; i++)
        {
            double t = 2 * Math.PI * i / segments;
            pts.Add(new Pt(cx + rx * Math.Cos(t), cy + ry * Math.Sin(t)));
        }
        return pts;
    }

    public static bool PolylineHit(IReadOnlyList<Pt> pts, Pt p, double tolerance)
    {
        if (pts.Count == 0) return false;
        if (pts.Count == 1) return Distance(pts[0], p) <= tolerance;
        for (int i = 1; i < pts.Count; i++)
            if (DistancePointToSegment(p, pts[i - 1], pts[i]) <= tolerance) return true;
        return false;
    }

    public static RectD Bounds(IEnumerable<Pt> pts)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        bool any = false;
        foreach (var p in pts)
        {
            any = true;
            minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y);
        }
        return any ? new RectD(minX, minY, maxX - minX, maxY - minY) : default;
    }

    /// <summary>Interpolates points between a and b so no two consecutive samples are further apart than <paramref name="step"/>.</summary>
    public static IEnumerable<Pt> Densify(Pt a, Pt b, double step)
    {
        double len = Distance(a, b);
        int n = Math.Max(1, (int)Math.Ceiling(len / Math.Max(0.5, step)));
        for (int i = 1; i <= n; i++) yield return a + (b - a) * ((double)i / n);
    }

    /// <summary>Constrains a shape drag: snaps lines to 45° increments, rectangles/ellipses to squares/circles.</summary>
    public static Pt Constrain(Pt start, Pt end, bool lineLike)
    {
        var d = end - start;
        if (lineLike)
        {
            double ang = Math.Atan2(d.Y, d.X);
            double snap = Math.Round(ang / (Math.PI / 4)) * (Math.PI / 4);
            double len = d.Length;
            return new Pt(start.X + Math.Cos(snap) * len, start.Y + Math.Sin(snap) * len);
        }
        double m = Math.Max(Math.Abs(d.X), Math.Abs(d.Y));
        return new Pt(start.X + Math.Sign(d.X == 0 ? 1 : d.X) * m, start.Y + Math.Sign(d.Y == 0 ? 1 : d.Y) * m);
    }
}
