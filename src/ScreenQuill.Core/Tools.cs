namespace ScreenQuill.Core;

public enum ToolKind
{
    Cursor, Pen, Highlighter, Eraser, Text, Line, Arrow, Rectangle, Ellipse
}

public enum BoardMode { None, Whiteboard, Blackboard }

public enum FadeMode { Off, Short, Medium, Long }

public static class ToolInfo
{
    public static bool IsShape(this ToolKind t) => t is ToolKind.Line or ToolKind.Arrow or ToolKind.Rectangle or ToolKind.Ellipse;
    public static bool IsInk(this ToolKind t) => t is ToolKind.Pen or ToolKind.Highlighter;
    /// <summary>Drawing tools take pointer input; the cursor tool makes the overlay click-through.</summary>
    public static bool CapturesInput(this ToolKind t) => t != ToolKind.Cursor;

    public static string DisplayName(this ToolKind t) => t switch
    {
        ToolKind.Cursor => "Cursor",
        ToolKind.Pen => "Pen",
        ToolKind.Highlighter => "Highlighter",
        ToolKind.Eraser => "Eraser",
        ToolKind.Text => "Text",
        ToolKind.Line => "Line",
        ToolKind.Arrow => "Arrow",
        ToolKind.Rectangle => "Rectangle",
        ToolKind.Ellipse => "Ellipse",
        _ => t.ToString()
    };
}

/// <summary>
/// Explicit tool state machine. Each tool keeps an independent size; the colour is shared but
/// highlighter keeps its own translucency. Raises <see cref="Changed"/> on every transition.
/// </summary>
public sealed class ToolState
{
    private ToolKind _tool = ToolKind.Cursor;
    private readonly Dictionary<ToolKind, double> _sizes;
    private uint _color;
    private BoardMode _board;
    private FadeMode _fade;
    private bool _inkVisible = true;
    private bool _cursorHighlight;

    public ToolState(AppSettings s)
    {
        _sizes = new Dictionary<ToolKind, double>
        {
            [ToolKind.Pen] = s.PenSize,
            [ToolKind.Highlighter] = s.HighlighterSize,
            [ToolKind.Eraser] = s.EraserSize,
            [ToolKind.Text] = s.TextSize,
            [ToolKind.Line] = s.ShapeSize,
            [ToolKind.Arrow] = s.ShapeSize,
            [ToolKind.Rectangle] = s.ShapeSize,
            [ToolKind.Ellipse] = s.ShapeSize,
        };
        _color = s.Color;
        _fade = s.FadeMode;
        LastDrawingTool = ToolKind.Pen;
        LastShapeTool = ToolKind.Line;
        LastInkTool = ToolKind.Pen;
    }

    public event EventHandler? Changed;

    public ToolKind Tool
    {
        get => _tool;
        set
        {
            if (_tool == value) return;
            _tool = value;
            if (value != ToolKind.Cursor) LastDrawingTool = value;
            if (value.IsShape()) LastShapeTool = value;
            if (value.IsInk()) LastInkTool = value;
            if (value != ToolKind.Cursor) _inkVisible = true; // drawing always reveals ink
            Raise();
        }
    }

    public ToolKind LastDrawingTool { get; private set; }
    public ToolKind LastShapeTool { get; private set; }
    public ToolKind LastInkTool { get; private set; }

    public bool IsDrawingMode => _tool.CapturesInput();

    public uint Color { get => _color; set { if (_color != value) { _color = value; Raise(); } } }

    public BoardMode Board { get => _board; set { if (_board != value) { _board = value; Raise(); } } }

    public FadeMode Fade { get => _fade; set { if (_fade != value) { _fade = value; Raise(); } } }

    public bool InkVisible { get => _inkVisible; set { if (_inkVisible != value) { _inkVisible = value; Raise(); } } }

    public bool CursorHighlight { get => _cursorHighlight; set { if (_cursorHighlight != value) { _cursorHighlight = value; Raise(); } } }

    public const double MinSize = 1, MaxSize = 120;

    /// <summary>Size (DIPs) of the given tool. Cursor maps to the last drawing tool.</summary>
    public double SizeOf(ToolKind t)
    {
        if (t == ToolKind.Cursor) t = LastDrawingTool;
        return _sizes.TryGetValue(t, out var v) ? v : 4;
    }

    public void SetSize(ToolKind t, double size)
    {
        if (t == ToolKind.Cursor) t = LastDrawingTool;
        size = Math.Clamp(Math.Round(size, 1), MinSize, MaxSize);
        if (t.IsShape())
        {
            foreach (var k in new[] { ToolKind.Line, ToolKind.Arrow, ToolKind.Rectangle, ToolKind.Ellipse }) _sizes[k] = size;
        }
        else _sizes[t] = size;
        Raise();
    }

    public double CurrentSize { get => SizeOf(_tool); set => SetSize(_tool, value); }

    /// <summary>Steps the current tool's size up/down in perceptually even increments.</summary>
    public void StepSize(int direction)
    {
        double s = CurrentSize;
        double step = s < 6 ? 1 : s < 20 ? 2 : s < 50 ? 5 : 10;
        CurrentSize = s + direction * step;
    }

    public FadeMode CycleFade()
    {
        Fade = (FadeMode)(((int)_fade + 1) % 4);
        return Fade;
    }

    public void WriteTo(AppSettings s)
    {
        s.PenSize = _sizes[ToolKind.Pen];
        s.HighlighterSize = _sizes[ToolKind.Highlighter];
        s.EraserSize = _sizes[ToolKind.Eraser];
        s.TextSize = _sizes[ToolKind.Text];
        s.ShapeSize = _sizes[ToolKind.Line];
        s.Color = _color;
        s.FadeMode = _fade;
    }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}
