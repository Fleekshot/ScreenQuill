using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ScreenQuill.Core;
using ScreenQuill.Core.Model;
using ScreenQuill.Native;

namespace ScreenQuill.Overlay;

public interface IInkHost
{
    AnnotationDocument Doc { get; }
    ToolState Tools { get; }
    AppSettings Settings { get; }
    IReadOnlyList<OverlayWindow> Overlays { get; }
    FadeSpec? NewFadeSpec();
    void OnPointerActivity();
    void OnTextEditing(bool editing);
}

/// <summary>
/// Turns pointer contacts (mouse, each pen, each finger) into annotations. Every contact owns an
/// independent session, so two fingers draw two strokes. Coordinates are global physical pixels.
/// </summary>
public sealed class InkInteraction
{
    public static readonly object MouseKey = "mouse";
    private const string EraserCursorKey = "eraser-cursor";

    private readonly IInkHost _host;
    private readonly Dictionary<object, Session> _sessions = new();
    private readonly HashSet<Annotation> _pendingErase = new();
    private bool _renderHooked;
    private TextEditor? _editor;

    public InkInteraction(IInkHost host) => _host = host;

    public bool IsEditingText => _editor != null;
    public bool IsBusy => _sessions.Count > 0;

    private sealed class Session
    {
        public required ToolKind Tool;
        public required OverlayWindow Origin;
        public required double Scale;
        public Annotation? Live;
        public Pt Start;
        public Pt Last;
        public bool Dirty;
        public TextAnnotation? TextTarget;
        public bool Dragging;
        public bool HasPressure;
    }

    // ---- pointer events from overlays --------------------------------------------------------
    public void PointerDown(object key, Pt p, float pressure, bool hasPressure, bool eraserEnd, OverlayWindow ow)
    {
        _host.OnPointerActivity();
        if (_editor != null) { CommitText(); return; } // first click outside the editor commits it
        var tools = _host.Tools;
        var tool = eraserEnd ? ToolKind.Eraser : tools.Tool;
        if (tool == ToolKind.Cursor) return;
        PointerCancel(key, ow);

        double scale = ow.Surface.Scale;
        var s = new Session { Tool = tool, Origin = ow, Scale = scale, Start = p, Last = p, HasPressure = hasPressure && _host.Settings.UsePenPressure };
        _sessions[key] = s;
        uint color = tools.Color;
        double width = tools.SizeOf(tool) * scale;

        switch (tool)
        {
            case ToolKind.Pen:
            case ToolKind.Highlighter:
                var stroke = new InkStroke(tool == ToolKind.Highlighter) { Color = color, Width = width, HasPressure = s.HasPressure };
                stroke.Points.Add(new InkPoint(p.X, p.Y, pressure));
                s.Live = stroke;
                s.Dirty = true;
                break;
            case ToolKind.Line:
            case ToolKind.Arrow:
            case ToolKind.Rectangle:
            case ToolKind.Ellipse:
                s.Live = new ShapeAnnotation(ShapeKind(tool)) { Color = color, Width = width, Start = p, End = p };
                s.Dirty = true;
                break;
            case ToolKind.Eraser:
                EraseAt(p, p, s);
                break;
            case ToolKind.Text:
                s.TextTarget = _host.Doc.HitTest(p, 4 * scale).OfType<TextAnnotation>().FirstOrDefault();
                break;
        }
        EnsureRenderHook();
    }

    public void PointerMove(object key, Pt p, float pressure, OverlayWindow ow)
    {
        if (!_sessions.TryGetValue(key, out var s)) { Hover(p, ow); return; }
        switch (s.Live)
        {
            case InkStroke stroke:
                var last = stroke.Points[^1];
                double minStep = 0.75;
                if (Math.Abs(last.X - p.X) < minStep && Math.Abs(last.Y - p.Y) < minStep) break;
                stroke.Points.Add(new InkPoint(p.X, p.Y, pressure));
                s.Dirty = true;
                break;
            case ShapeAnnotation shape:
                bool constrain = Win32.IsKeyDown(Win32.VK_SHIFT);
                shape.End = constrain ? GeometryMath.Constrain(shape.Start, p, s.Tool is ToolKind.Line or ToolKind.Arrow) : p;
                s.Dirty = true;
                break;
        }
        if (s.Tool == ToolKind.Eraser)
        {
            EraseAt(s.Last, p, s);
            ShowEraserCursor(p);
        }
        if (s.Tool == ToolKind.Text && s.TextTarget != null)
        {
            if (!s.Dragging && GeometryMath.Distance(s.Start, p) > 4 * s.Scale)
            {
                s.Dragging = true;
                SetHidden(new[] { s.TextTarget });
            }
            if (s.Dragging)
            {
                var moved = (TextAnnotation)s.TextTarget.Clone();
                moved.Position = s.TextTarget.Position + (p - s.Start);
                s.Live = moved;
                s.Dirty = true;
            }
        }
        s.Last = p;
    }

    public void PointerUp(object key, Pt p, OverlayWindow ow)
    {
        if (!_sessions.TryGetValue(key, out var s)) return;
        if (s.Tool != ToolKind.Eraser && s.Tool != ToolKind.Text) PointerMove(key, p, s.Live is InkStroke st && st.Points.Count > 0 ? st.Points[^1].Pressure : 0.5f, ow);
        _sessions.Remove(key);
        RemoveLiveEverywhere(key);
        var doc = _host.Doc;

        switch (s.Tool)
        {
            case ToolKind.Pen:
            case ToolKind.Highlighter:
                if (s.Live is InkStroke stroke && stroke.Points.Count > 0) Commit(stroke);
                break;
            case ToolKind.Line:
            case ToolKind.Arrow:
            case ToolKind.Rectangle:
            case ToolKind.Ellipse:
                if (s.Live is ShapeAnnotation shape && GeometryMath.Distance(shape.Start, shape.End) >= 2) Commit(shape);
                break;
            case ToolKind.Eraser:
                if (_pendingErase.Count > 0)
                {
                    var hits = _pendingErase.ToList();
                    _pendingErase.Clear();
                    doc.Remove(hits);
                    SetHidden(Array.Empty<Annotation>());
                }
                break;
            case ToolKind.Text:
                if (s.Dragging && s.TextTarget != null && s.Live is TextAnnotation moved)
                {
                    SetHidden(Array.Empty<Annotation>());
                    doc.Replace(s.TextTarget, moved);
                }
                else if (s.TextTarget != null) BeginTextEdit(s.TextTarget, ow);
                else BeginTextEdit(null, ow, p);
                break;
        }
    }

    public void PointerCancel(object key, OverlayWindow ow)
    {
        if (!_sessions.Remove(key, out var s)) return;
        RemoveLiveEverywhere(key);
        if (s.Tool == ToolKind.Eraser || s.Dragging)
        {
            // A cancelled erase/move gesture still applies what the user already saw happen.
            if (s.Tool == ToolKind.Eraser && _pendingErase.Count > 0) { _host.Doc.Remove(_pendingErase.ToList()); _pendingErase.Clear(); }
            SetHidden(Array.Empty<Annotation>());
        }
    }

    public void RightClick(OverlayWindow ow)
    {
        foreach (var k in _sessions.Keys.ToList()) PointerCancel(k, ow);
    }

    public void Hover(Pt p, OverlayWindow ow)
    {
        if (_host.Tools.Tool == ToolKind.Eraser) ShowEraserCursor(p);
    }

    public void HoverLeave(OverlayWindow ow) => ow.Surface.RemoveLive(EraserCursorKey);

    public void ForgetOverlay(OverlayWindow ow)
    {
        foreach (var k in _sessions.Where(kv => kv.Value.Origin == ow).Select(kv => kv.Key).ToList()) _sessions.Remove(k);
        if (_editor?.Overlay == ow) CancelText();
    }

    /// <summary>Abandons every in-progress gesture (tool switch, clear, display change).</summary>
    public void CancelAll()
    {
        foreach (var k in _sessions.Keys.ToList()) RemoveLiveEverywhere(k);
        _sessions.Clear();
        _pendingErase.Clear();
        foreach (var o in _host.Overlays) { o.Surface.ClearLive(); }
        SetHidden(Array.Empty<Annotation>());
    }

    public void OnToolChanged()
    {
        if (_host.Tools.Tool != ToolKind.Eraser)
            foreach (var o in _host.Overlays) o.Surface.RemoveLive(EraserCursorKey);
        if (_host.Tools.Tool != ToolKind.Text && _editor != null) CommitText();
        foreach (var o in _host.Overlays) o.Cursor = CursorFor(_host.Tools.Tool);
    }

    public static Cursor? CursorFor(ToolKind t) => t switch
    {
        ToolKind.Pen or ToolKind.Highlighter => Cursors.Pen,
        ToolKind.Eraser => Cursors.None,
        ToolKind.Text => Cursors.IBeam,
        ToolKind.Line or ToolKind.Arrow or ToolKind.Rectangle or ToolKind.Ellipse => Cursors.Cross,
        _ => null,
    };

    // ---- helpers -------------------------------------------------------------------------------
    private void Commit(Annotation a)
    {
        a.Fade = _host.NewFadeSpec();
        a.CreatedAt = 0; // stamped by the document: fade lifetime starts when the mark is completed
        _host.Doc.Add(a);
    }

    private static AnnotationKind ShapeKind(ToolKind t) => t switch
    {
        ToolKind.Line => AnnotationKind.Line,
        ToolKind.Arrow => AnnotationKind.Arrow,
        ToolKind.Rectangle => AnnotationKind.Rectangle,
        _ => AnnotationKind.Ellipse,
    };

    private void EraseAt(Pt from, Pt to, Session s)
    {
        double r = Math.Max(2, _host.Tools.SizeOf(ToolKind.Eraser) * s.Scale / 2);
        bool any = false;
        foreach (var q in GeometryMath.Densify(from, to, r / 2).Prepend(from))
            foreach (var hit in _host.Doc.HitTest(q, r))
                any |= _pendingErase.Add(hit);
        if (any) SetHidden(_pendingErase);
    }

    private void SetHidden(IEnumerable<Annotation> items)
    {
        var ids = items.Select(i => i.Id).ToList();
        foreach (var o in _host.Overlays) o.Surface.SetHidden(ids, _host.Doc.Items);
    }

    private void ShowEraserCursor(Pt p)
    {
        foreach (var o in _host.Overlays)
        {
            double r = Math.Max(2, _host.Tools.SizeOf(ToolKind.Eraser) * o.Surface.Scale / 2);
            double thin = o.Surface.Scale;
            o.Surface.SetLiveDrawing(EraserCursorKey, dc =>
            {
                var c = new Point(p.X, p.Y);
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), new Pen(Brushes.White, 3 * thin), c, r, r);
                dc.DrawEllipse(null, new Pen(Brushes.Black, 1.2 * thin), c, r, r);
            });
        }
    }

    private void RemoveLiveEverywhere(object key)
    {
        foreach (var o in _host.Overlays) o.Surface.RemoveLive(key);
    }

    private void EnsureRenderHook()
    {
        if (_renderHooked) return;
        _renderHooked = true;
        CompositionTarget.Rendering += OnRendering;
    }

    /// <summary>Coalesces many input samples into at most one redraw per frame per contact.</summary>
    private void OnRendering(object? sender, EventArgs e)
    {
        foreach (var (key, s) in _sessions)
        {
            if (!s.Dirty || s.Live == null) continue;
            s.Dirty = false;
            foreach (var o in _host.Overlays)
            {
                var b = s.Live.Bounds;
                var m = o.Monitor.Bounds;
                bool visible = b.IntersectsWith(new RectD(m.X, m.Y, m.Width, m.Height));
                if (visible) o.Surface.SetLive(key, s.Live); else o.Surface.RemoveLive(key);
            }
        }
        if (_sessions.Count == 0)
        {
            CompositionTarget.Rendering -= OnRendering;
            _renderHooked = false;
        }
    }

    // ---- text -----------------------------------------------------------------------------------
    private void BeginTextEdit(TextAnnotation? existing, OverlayWindow ow, Pt? at = null)
    {
        var tools = _host.Tools;
        double scale = ow.Surface.Scale;
        var model = existing != null ? (TextAnnotation)existing.Clone() : new TextAnnotation
        {
            Position = at!.Value,
            Color = tools.Color,
            Width = tools.SizeOf(ToolKind.Text) * scale,
        };
        if (existing != null) SetHidden(new[] { existing });
        _editor = new TextEditor(this, ow, existing, model);
        _host.OnTextEditing(true);
    }

    internal void CommitText()
    {
        var ed = _editor;
        if (ed == null) return;
        _editor = null;
        string text = ed.Box.Text.TrimEnd();
        ed.Close();
        SetHidden(Array.Empty<Annotation>());
        if (ed.Original != null)
        {
            if (text.Length == 0) _host.Doc.Remove(new[] { ed.Original });
            else if (text != ed.Original.Text || ed.Model.Color != ed.Original.Color || ed.Model.Width != ed.Original.Width)
            {
                var edited = (TextAnnotation)ed.Model.Clone();
                edited.Text = text;
                AnnotationRenderer.Measure(edited);
                _host.Doc.Replace(ed.Original, edited);
            }
        }
        else if (text.Length > 0)
        {
            ed.Model.Text = text;
            AnnotationRenderer.Measure(ed.Model);
            Commit(ed.Model);
        }
        _host.OnTextEditing(false);
    }

    internal void CancelText()
    {
        var ed = _editor;
        if (ed == null) return;
        _editor = null;
        ed.Close();
        SetHidden(Array.Empty<Annotation>());
        _host.OnTextEditing(false);
    }

    /// <summary>Applies a colour/size change to the text being edited (live), so the toolbar works during typing.</summary>
    public void RefreshEditorStyle()
    {
        if (_editor == null) return;
        _editor.Model.Color = _host.Tools.Color;
        _editor.Model.Width = _host.Tools.SizeOf(ToolKind.Text) * _editor.Overlay.Surface.Scale;
        _editor.ApplyStyle();
    }

    private sealed class TextEditor
    {
        private readonly InkInteraction _owner;
        public TextEditor(InkInteraction owner, OverlayWindow ow, TextAnnotation? original, TextAnnotation model)
        {
            _owner = owner; Overlay = ow; Original = original; Model = model;
            Box = new TextBox
            {
                Text = model.Text,
                AcceptsReturn = true,
                AcceptsTab = false,
                Background = new SolidColorBrush(Color.FromArgb(24, 128, 128, 128)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2F, 0xC5, 0xEA)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(0),
                MinWidth = 40,
                FontFamily = new FontFamily(model.FontFamily),
                FontWeight = FontWeights.SemiBold,
                CaretBrush = Brushes.Gray,
            };
            ApplyStyle();
            var local = ow.Surface.ToLocal(model.Position);
            Canvas.SetLeft(Box, local.X - 3);
            Canvas.SetTop(Box, local.Y - 1);
            Box.PreviewKeyDown += OnKey;
            ow.EditorLayer.Children.Add(Box);
            ow.AllowActivation(true);
            Box.Dispatcher.BeginInvoke(() => { Box.Focus(); Keyboard.Focus(Box); Box.CaretIndex = Box.Text.Length; },
                System.Windows.Threading.DispatcherPriority.Input);
        }

        public OverlayWindow Overlay { get; }
        public TextAnnotation? Original { get; }
        public TextAnnotation Model { get; }
        public TextBox Box { get; }

        public void ApplyStyle()
        {
            Box.FontSize = Math.Max(4, Model.Width / Overlay.Surface.Scale);
            Box.Foreground = AnnotationRenderer.OpaqueBrush(Model.Color);
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { e.Handled = true; _owner.CancelText(); }
            else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0) { e.Handled = true; _owner.CommitText(); }
        }

        public void Close()
        {
            Overlay.EditorLayer.Children.Remove(Box);
            Overlay.AllowActivation(false);
        }
    }
}
