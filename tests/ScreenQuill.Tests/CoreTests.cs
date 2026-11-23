using ScreenQuill.Core;
using ScreenQuill.Core.Model;

namespace ScreenQuill.Tests;

public class GeometryTests
{
    [Theory]
    [InlineData(10, 10, 50, 40)]
    [InlineData(50, 40, 10, 10)]
    [InlineData(50, 10, 10, 40)]
    [InlineData(10, 40, 50, 10)]
    public void NormalizeRect_AnyDragDirection_GivesSameRect(double ax, double ay, double bx, double by)
    {
        var r = GeometryMath.NormalizeRect(new Pt(ax, ay), new Pt(bx, by));
        Assert.Equal(new RectD(10, 10, 40, 30), r);
    }

    [Fact]
    public void NormalizeRect_NegativeCoordinates()
    {
        var r = GeometryMath.NormalizeRect(new Pt(-1920, 100), new Pt(-1800, -50));
        Assert.Equal(new RectD(-1920, -50, 120, 150), r);
    }

    [Fact]
    public void DistancePointToSegment_ClampsToEnds()
    {
        Assert.Equal(5, GeometryMath.DistancePointToSegment(new Pt(3, 4), new Pt(0, 0), new Pt(0, 0)), 6);
        Assert.Equal(2, GeometryMath.DistancePointToSegment(new Pt(5, 2), new Pt(0, 0), new Pt(10, 0)), 6);
        Assert.Equal(5, GeometryMath.DistancePointToSegment(new Pt(13, 4), new Pt(0, 0), new Pt(10, 0)), 6);
    }

    [Fact]
    public void ArrowHead_IsSymmetricAndBehindTip()
    {
        var (l, r) = GeometryMath.ArrowHead(new Pt(0, 0), new Pt(100, 0), 4);
        Assert.True(l.X < 100 && r.X < 100);
        Assert.Equal(l.X, r.X, 6);
        Assert.Equal(-l.Y, r.Y, 6);
    }

    [Fact]
    public void Constrain_SnapsLineTo45Degrees_AndRectToSquare()
    {
        var p = GeometryMath.Constrain(new Pt(0, 0), new Pt(100, 10), lineLike: true);
        Assert.Equal(0, p.Y, 6);
        var q = GeometryMath.Constrain(new Pt(0, 0), new Pt(-30, 80), lineLike: false);
        Assert.Equal(new Pt(-80, 80), q);
    }

    [Fact]
    public void Densify_NoGapLargerThanStep()
    {
        var pts = GeometryMath.Densify(new Pt(0, 0), new Pt(100, 0), 7).ToList();
        Assert.Equal(100, pts[^1].X, 6);
        var prev = new Pt(0, 0);
        foreach (var p in pts) { Assert.True(GeometryMath.Distance(prev, p) <= 7.0001); prev = p; }
    }
}

public class AnnotationTests
{
    private static InkStroke Stroke(params (double x, double y)[] pts)
    {
        var s = new InkStroke(false) { Width = 4, Color = 0xFF000000 };
        foreach (var (x, y) in pts) s.Points.Add(new InkPoint(x, y));
        return s;
    }

    [Fact]
    public void StrokeHitTest_UsesWidthAndTolerance()
    {
        var s = Stroke((0, 0), (100, 0));
        Assert.True(s.HitTest(new Pt(50, 5), 4));
        Assert.False(s.HitTest(new Pt(50, 20), 4));
    }

    [Fact]
    public void RectangleHitTest_OutlineOnly()
    {
        var r = new ShapeAnnotation(AnnotationKind.Rectangle) { Start = new Pt(100, 100), End = new Pt(0, 0), Width = 2 };
        Assert.True(r.HitTest(new Pt(0, 50), 2));
        Assert.False(r.HitTest(new Pt(50, 50), 2)); // interior is not ink
    }

    [Fact]
    public void EllipseHitTest()
    {
        var e = new ShapeAnnotation(AnnotationKind.Ellipse) { Start = new Pt(0, 0), End = new Pt(200, 100), Width = 2 };
        Assert.True(e.HitTest(new Pt(200, 50), 3));
        Assert.True(e.HitTest(new Pt(100, 0), 3));
        Assert.False(e.HitTest(new Pt(100, 50), 3));
    }

    [Fact]
    public void ShapeRejectsNonShapeKind() =>
        Assert.Throws<ArgumentException>(() => new ShapeAnnotation(AnnotationKind.Pen));

    [Fact]
    public void TextHitTest_UsesMeasuredBounds()
    {
        var t = new TextAnnotation { Position = new Pt(10, 10), Width = 20, MeasuredWidth = 100, MeasuredHeight = 30, Text = "Hi" };
        Assert.True(t.HitTest(new Pt(90, 30), 0));
        Assert.False(t.HitTest(new Pt(200, 30), 0));
    }

    [Fact]
    public void Clone_CopiesEverythingButId()
    {
        var s = Stroke((1, 2), (3, 4));
        s.Fade = new FadeSpec(2, 1);
        var c = (InkStroke)s.Clone();
        Assert.NotEqual(s.Id, c.Id);
        Assert.Equal(s.Points, c.Points);
        Assert.Equal(s.Fade, c.Fade);
    }

    [Fact]
    public void PressureScale_IsMonotonic()
    {
        Assert.True(InkStroke.PressureScale(0) < InkStroke.PressureScale(0.5f));
        Assert.True(InkStroke.PressureScale(0.5f) < InkStroke.PressureScale(1));
    }
}

public class DocumentTests
{
    private double _now = 1;
    private AnnotationDocument NewDoc() => new(() => _now);

    private static ShapeAnnotation Line() => new(AnnotationKind.Line) { Start = new Pt(0, 0), End = new Pt(100, 0), Width = 4 };

    [Fact]
    public void AddUndoRedo()
    {
        var d = NewDoc();
        var a = Line();
        d.Add(a);
        Assert.Single(d.Items);
        d.Undo();
        Assert.Empty(d.Items);
        d.Redo();
        Assert.Same(a, d.Items[0]);
    }

    [Fact]
    public void Clear_RemovesAllKinds_AndIsUndoable()
    {
        var d = NewDoc();
        d.Add(Line());
        d.Add(new TextAnnotation { Text = "x" });
        var s = new InkStroke(true); s.Points.Add(new InkPoint(1, 1)); d.Add(s);
        d.Clear();
        Assert.Equal(0, d.Count);
        d.Undo();
        Assert.Equal(3, d.Count);
    }

    [Fact]
    public void Remove_PreservesZOrderOnUndo()
    {
        var d = NewDoc();
        var a = Line(); var b = Line(); var c = Line();
        d.Add(a); d.Add(b); d.Add(c);
        d.Remove(new[] { b });
        d.Undo();
        Assert.Equal(new Annotation[] { a, b, c }, d.Items);
    }

    [Fact]
    public void Replace_SwapsInPlace()
    {
        var d = NewDoc();
        var a = Line(); var b = Line();
        d.Add(a); d.Add(b);
        var a2 = (ShapeAnnotation)a.Clone(); a2.End = new Pt(5, 5);
        d.Replace(a, a2);
        Assert.Same(a2, d.Items[0]);
        d.Undo();
        Assert.Same(a, d.Items[0]);
    }

    [Fact]
    public void NewActionClearsRedo()
    {
        var d = NewDoc();
        d.Add(Line()); d.Undo();
        Assert.True(d.CanRedo);
        d.Add(Line());
        Assert.False(d.CanRedo);
    }

    [Fact]
    public void HitTest_ReturnsTopmostFirst()
    {
        var d = NewDoc();
        var a = Line(); var b = Line();
        d.Add(a); d.Add(b);
        Assert.Same(b, d.HitTest(new Pt(50, 0), 2).First());
    }

    [Fact]
    public void ChangedEventsFire()
    {
        var d = NewDoc();
        var events = new List<DocumentChange>();
        d.Changed += (_, e) => events.Add(e.Change);
        d.Add(Line()); d.Clear(); d.Undo();
        Assert.Equal(new[] { DocumentChange.Added, DocumentChange.Removed, DocumentChange.Added }, events);
    }
}

public class FadeTests
{
    [Fact]
    public void FadeSpec_Opacity()
    {
        var f = new FadeSpec(2, 1);
        Assert.Equal(1, f.OpacityAt(0));
        Assert.Equal(1, f.OpacityAt(2));
        Assert.Equal(0.5, f.OpacityAt(2.5), 6);
        Assert.Equal(0, f.OpacityAt(3));
    }

    [Fact]
    public void ElementsExpireIndependentlyFromCreationTime()
    {
        double now = 10;
        var d = new AnnotationDocument(() => now);
        var a = new ShapeAnnotation(AnnotationKind.Line) { Fade = new FadeSpec(2, 1) };
        d.Add(a);
        now = 11.5;
        var b = new ShapeAnnotation(AnnotationKind.Line) { Fade = new FadeSpec(2, 1) };
        d.Add(b);
        var c = new ShapeAnnotation(AnnotationKind.Line); // non-fading
        d.Add(c);

        now = 12.5; // a half-faded, b fully visible
        Assert.Equal(0.5, d.FadeOpacity(a), 6);
        Assert.Equal(1, d.FadeOpacity(b));
        Assert.Empty(d.RemoveExpired());

        now = 13.0; // a expired
        Assert.Equal(new[] { a }, d.RemoveExpired());
        now = 14.5; // b expired
        Assert.Equal(new[] { b }, d.RemoveExpired());
        Assert.Equal(new[] { c }, d.Items);
    }

    [Fact]
    public void ExpiredInkCannotBeResurrectedByUndo()
    {
        double now = 0.5;
        var d = new AnnotationDocument(() => now);
        var a = new ShapeAnnotation(AnnotationKind.Line) { Fade = new FadeSpec(1, 0) };
        d.Add(a);
        d.Clear();
        now = 5;
        d.RemoveExpired(); // nothing in doc, but history references a
        d.Undo();
        Assert.Empty(d.Items);
    }

    [Theory]
    [InlineData(FadeMode.Short, 2)]
    [InlineData(FadeMode.Medium, 5)]
    [InlineData(FadeMode.Long, 10)]
    [InlineData(FadeMode.Off, 0)]
    public void ThreeDurationsHaveProjectDefaults(FadeMode m, double seconds) =>
        Assert.Equal(seconds, new AppSettings().FadeSeconds(m));
}

public class ToolStateTests
{
    [Fact]
    public void SizesAreIndependentPerTool()
    {
        var t = new ToolState(new AppSettings());
        t.Tool = ToolKind.Pen; t.CurrentSize = 7;
        t.Tool = ToolKind.Highlighter; t.CurrentSize = 30;
        t.Tool = ToolKind.Pen;
        Assert.Equal(7, t.CurrentSize);
        Assert.Equal(30, t.SizeOf(ToolKind.Highlighter));
    }

    [Fact]
    public void ShapesShareOneSize()
    {
        var t = new ToolState(new AppSettings());
        t.SetSize(ToolKind.Arrow, 9);
        Assert.Equal(9, t.SizeOf(ToolKind.Ellipse));
    }

    [Fact]
    public void CursorTracksLastDrawingTool()
    {
        var t = new ToolState(new AppSettings());
        t.Tool = ToolKind.Rectangle;
        t.Tool = ToolKind.Cursor;
        Assert.False(t.IsDrawingMode);
        Assert.Equal(ToolKind.Rectangle, t.LastDrawingTool);
        Assert.Equal(ToolKind.Rectangle, t.LastShapeTool);
        Assert.Equal(t.SizeOf(ToolKind.Rectangle), t.CurrentSize);
    }

    [Fact]
    public void SelectingDrawingToolRevealsHiddenInk()
    {
        var t = new ToolState(new AppSettings());
        t.InkVisible = false;
        t.Tool = ToolKind.Pen;
        Assert.True(t.InkVisible);
    }

    [Fact]
    public void SizeClampedAndStepped()
    {
        var t = new ToolState(new AppSettings());
        t.Tool = ToolKind.Pen;
        t.CurrentSize = 1000;
        Assert.Equal(ToolState.MaxSize, t.CurrentSize);
        t.CurrentSize = 4; t.StepSize(+1);
        Assert.Equal(5, t.CurrentSize);
        t.CurrentSize = 1; t.StepSize(-1);
        Assert.Equal(ToolState.MinSize, t.CurrentSize);
    }

    [Fact]
    public void FadeCyclesThroughAllModes()
    {
        var t = new ToolState(new AppSettings());
        Assert.Equal(FadeMode.Short, t.CycleFade());
        Assert.Equal(FadeMode.Medium, t.CycleFade());
        Assert.Equal(FadeMode.Long, t.CycleFade());
        Assert.Equal(FadeMode.Off, t.CycleFade());
    }

    [Fact]
    public void ChangedRaisedOnTransitions()
    {
        var t = new ToolState(new AppSettings());
        int n = 0; t.Changed += (_, _) => n++;
        t.Tool = ToolKind.Pen; t.Tool = ToolKind.Pen; t.Board = BoardMode.Whiteboard; t.Color = 1;
        Assert.Equal(3, n);
    }
}

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+P")]
    [InlineData("Ctrl+Shift+F5")]
    [InlineData("Alt+Shift+=")]
    [InlineData("Ctrl+Num7")]
    [InlineData("Ctrl+Alt+Delete")]
    public void RoundTrip(string text)
    {
        Assert.True(Hotkey.TryParse(text, out var hk));
        Assert.Equal(text, hk.ToString());
    }

    [Theory]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+P+Q")]
    [InlineData("Ctrl+Bogus")]
    public void InvalidTextRejected(string text) => Assert.False(Hotkey.TryParse(text, out _));

    [Fact]
    public void DefaultsHaveNoDuplicatesOrReserved()
    {
        var d = HotkeyRules.Defaults();
        Assert.Empty(HotkeyRules.FindDuplicates(d));
        foreach (var kv in d) Assert.Equal(HotkeyProblem.None, HotkeyRules.Validate(kv.Key, kv.Value, d));
        Assert.Equal(Enum.GetValues<HotkeyAction>().Length, d.Count);
    }

    [Fact]
    public void DetectsDuplicate()
    {
        var d = HotkeyRules.Defaults();
        Assert.Equal(HotkeyProblem.Duplicate, HotkeyRules.Validate(HotkeyAction.Pen, d[HotkeyAction.Eraser], d));
    }

    [Fact]
    public void DetectsReservedAndUnmodified()
    {
        var d = HotkeyRules.Defaults();
        Assert.True(Hotkey.TryParse("Ctrl+Alt+Delete", out var cad));
        Assert.Equal(HotkeyProblem.Reserved, HotkeyRules.Validate(HotkeyAction.Pen, cad, d));
        Assert.Equal(HotkeyProblem.Reserved, HotkeyRules.Validate(HotkeyAction.Pen, new Hotkey(HotkeyModifiers.Win, 'L'), d));
        Assert.Equal(HotkeyProblem.NoModifier, HotkeyRules.Validate(HotkeyAction.Pen, new Hotkey(HotkeyModifiers.None, 'P'), d));
        Assert.Equal(HotkeyProblem.None, HotkeyRules.Validate(HotkeyAction.Pen, new Hotkey(HotkeyModifiers.None, 0x78), d)); // F9
    }

    [Fact]
    public void EmptyBindingAlwaysValid() =>
        Assert.Equal(HotkeyProblem.None, HotkeyRules.Validate(HotkeyAction.Pen, Hotkey.None, HotkeyRules.Defaults()));
}

public class SettingsTests
{
    [Fact]
    public void SaveLoadRoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), "sq-test-" + Guid.NewGuid() + ".json");
        try
        {
            var store = new SettingsStore(path);
            var s = new AppSettings { PenSize = 9, Color = 0xFF123456, FadeMode = FadeMode.Long, ToolbarX = -1500, ToolbarY = 20 };
            var hk = s.GetHotkeys(); hk[HotkeyAction.Pen] = new Hotkey(HotkeyModifiers.Control | HotkeyModifiers.Shift, 'Q');
            s.SetHotkeys(hk);
            s.CustomColors.Add(0xFF0A0B0C);
            store.Save(s);
            store.Save(s); // second save exercises File.Replace path
            var l = store.Load();
            Assert.Equal(9, l.PenSize);
            Assert.Equal(0xFF123456u, l.Color);
            Assert.Equal(FadeMode.Long, l.FadeMode);
            Assert.Equal(-1500, l.ToolbarX);
            Assert.Equal("Ctrl+Shift+Q", l.GetHotkeys()[HotkeyAction.Pen].ToString());
            Assert.Contains(0xFF0A0B0Cu, l.CustomColors);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void CorruptFileFallsBackToDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), "sq-test-" + Guid.NewGuid() + ".json");
        File.WriteAllText(path, "{ not json");
        try
        {
            var l = new SettingsStore(path).Load();
            Assert.Equal(new AppSettings().PenSize, l.PenSize);
        }
        finally { File.Delete(path); File.Delete(path + ".bad"); }
    }

    [Fact]
    public void SanitizeClampsOutOfRange()
    {
        var s = new AppSettings { PenSize = -5, HighlighterOpacity = 5, FadeShortSeconds = 0 };
        s.Sanitize();
        Assert.Equal(ToolState.MinSize, s.PenSize);
        Assert.Equal(0.9, s.HighlighterOpacity);
        Assert.Equal(0.5, s.FadeShortSeconds);
    }
}

public class PaletteTests
{
    [Fact]
    public void Has24UniqueQuickColours()
    {
        Assert.Equal(24, Palette.Quick.Length);
        Assert.Equal(24, Palette.Quick.Distinct().Count());
        Assert.Equal(Palette.Columns * Palette.Rows, Palette.Quick.Length);
    }

    [Fact]
    public void PushRecent_MovesToFrontAndDedupes()
    {
        var r = Palette.PushRecent(new uint[] { 1, 2, 3, 4 }, 3);
        Assert.Equal(new uint[] { 3, 1, 2, 4 }, r);
        Assert.Equal(4, Palette.PushRecent(new uint[] { 1 }, 9).Count);
    }

    [Theory]
    [InlineData("#FF0000", 0xFFFF0000)]
    [InlineData("0f0", 0xFF00FF00)]
    [InlineData("#80112233", 0x80112233)]
    public void ParseHex(string s, uint expected)
    {
        Assert.True(Palette.TryParseHex(s, out var v));
        Assert.Equal(expected, v);
    }

    [Fact]
    public void HexRoundTrip() => Assert.Equal("#ABCDEF", Palette.ToHex(0xFFABCDEF));
}

public class ScreenshotNamingTests
{
    [Fact]
    public void AppendsCounterWhenExists()
    {
        var existing = new HashSet<string>();
        var now = new DateTime(2026, 11, 22, 16, 5, 9);
        var p1 = ScreenshotNaming.NextPath("C:\\x", now, existing.Contains);
        Assert.EndsWith("ScreenQuill 2026-11-22 16-05-09.png", p1);
        existing.Add(p1);
        var p2 = ScreenshotNaming.NextPath("C:\\x", now, existing.Contains);
        Assert.EndsWith("(2).png", p2);
    }

    [Fact]
    public void ClampSelection_NegativeOriginVirtualScreen()
    {
        var vs = new RectD(-1920, 0, 3840, 1080);
        var r = ScreenshotNaming.ClampSelection(new RectD(-2000, -10, 300, 100), vs);
        Assert.Equal(new RectD(-1920, 0, 220, 90), r);
        Assert.Null(ScreenshotNaming.ClampSelection(new RectD(5000, 0, 10, 10), vs));
    }
}

public class FadeHistoryTests
{
    [Fact]
    public void UndoClear_RestoresPermanentInk_EvenAfterMixedFadingExpired()
    {
        double now = 1;
        var d = new AnnotationDocument(() => now);
        var fading = new ShapeAnnotation(AnnotationKind.Line) { Fade = new FadeSpec(1, 0) };
        var permanent = new ShapeAnnotation(AnnotationKind.Line);
        d.Add(fading); d.Add(permanent);
        d.Clear();
        now = 10;
        d.RemoveExpired();
        d.Undo();
        Assert.Equal(new Annotation[] { permanent }, d.Items);
    }
}
