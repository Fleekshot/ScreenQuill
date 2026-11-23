using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using ScreenQuill.Core;
using ScreenQuill.Core.Model;

namespace ScreenQuill.Overlay;

/// <summary>
/// Retained-mode renderer for one monitor. Every annotation gets its own DrawingVisual (rebuilt only
/// when that annotation changes) under a root transform mapping global physical pixels to this
/// window's DIPs. In-progress strokes and previews live in a separate "live" layer on top.
/// </summary>
public sealed class InkSurface : FrameworkElement
{
    private readonly VisualCollection _children;
    private readonly ContainerVisual _root = new();
    private readonly ContainerVisual _committed = new();
    private readonly ContainerVisual _live = new();
    private readonly Dictionary<long, (DrawingVisual Visual, int Version)> _map = new();
    private readonly Dictionary<object, DrawingVisual> _liveMap = new();
    private readonly HashSet<long> _hidden = new();

    public InkSurface()
    {
        _children = new VisualCollection(this) { _root };
        _root.Children.Add(_committed);
        _root.Children.Add(_live);
        IsHitTestVisible = false; // input is handled by the window, never by visuals
        RenderOptions.SetEdgeMode(this, EdgeMode.Unspecified);
    }

    public double OriginX { get; private set; }
    public double OriginY { get; private set; }
    public double Scale { get; private set; } = 1;
    public double HighlighterOpacity { get; set; } = 0.45;
    public bool Smooth { get; set; } = true;
    public Func<Annotation, double>? FadeOpacity { get; set; }

    /// <summary>Sets mapping: local DIP = (global px - origin) / scale.</summary>
    public void SetMapping(double originX, double originY, double scale)
    {
        OriginX = originX; OriginY = originY; Scale = scale;
        _root.Transform = new MatrixTransform(1 / scale, 0, 0, 1 / scale, -originX / scale, -originY / scale);
    }

    public Pt ToGlobal(Point local) => new(OriginX + local.X * Scale, OriginY + local.Y * Scale);
    public Point ToLocal(Pt global) => new((global.X - OriginX) / Scale, (global.Y - OriginY) / Scale);

    protected override int VisualChildrenCount => _children.Count;
    protected override Visual GetVisualChild(int index) => _children[index];

    /// <summary>Synchronises visuals with the document (adds, removes, reorders, rebuilds changed).</summary>
    public void Sync(IReadOnlyList<Annotation> items)
    {
        var alive = new HashSet<long>();
        bool orderChanged = _committed.Children.Count != items.Count;
        for (int i = 0; i < items.Count; i++)
        {
            var a = items[i];
            alive.Add(a.Id);
            if (!_map.TryGetValue(a.Id, out var entry))
            {
                entry = (new DrawingVisual(), -1);
                orderChanged = true;
            }
            if (entry.Version != a.Version)
            {
                Render(entry.Visual, a);
                entry = (entry.Visual, a.Version);
            }
            _map[a.Id] = entry;
            if (!orderChanged && !ReferenceEquals(_committed.Children[i], entry.Visual)) orderChanged = true;
        }
        foreach (var id in new List<long>(_map.Keys))
            if (!alive.Contains(id)) _map.Remove(id);
        if (orderChanged)
        {
            _committed.Children.Clear();
            foreach (var a in items) _committed.Children.Add(_map[a.Id].Visual);
        }
        UpdateOpacities(items);
    }

    private void Render(DrawingVisual v, Annotation a)
    {
        using var dc = v.RenderOpen();
        AnnotationRenderer.Draw(dc, a, Smooth);
    }

    /// <summary>Refreshes opacity for fading/hidden elements (cheap; no geometry rebuild).</summary>
    public void UpdateOpacities(IReadOnlyList<Annotation> items)
    {
        foreach (var a in items)
        {
            if (!_map.TryGetValue(a.Id, out var e)) continue;
            double o = _hidden.Contains(a.Id) ? 0 : AnnotationRenderer.BaseOpacity(a, HighlighterOpacity) * (FadeOpacity?.Invoke(a) ?? 1);
            if (Math.Abs(e.Visual.Opacity - o) > 0.001) e.Visual.Opacity = o;
        }
    }

    public void SetHidden(IEnumerable<long> ids, IReadOnlyList<Annotation> items)
    {
        _hidden.Clear();
        foreach (var id in ids) _hidden.Add(id);
        UpdateOpacities(items);
    }

    /// <summary>Forces every visual to be rebuilt (e.g. after smoothing setting changes).</summary>
    public void Invalidate(IReadOnlyList<Annotation> items)
    {
        _map.Clear();
        _committed.Children.Clear();
        Sync(items);
    }

    // ---- live layer ------------------------------------------------------------------------
    public void SetLive(object key, Annotation a)
    {
        if (!_liveMap.TryGetValue(key, out var v))
        {
            v = new DrawingVisual();
            _liveMap[key] = v;
            _live.Children.Add(v);
        }
        Render(v, a);
        v.Opacity = AnnotationRenderer.BaseOpacity(a, HighlighterOpacity);
    }

    /// <summary>Draws an arbitrary live decoration (eraser circle) given in global px.</summary>
    public void SetLiveDrawing(object key, Action<DrawingContext>? draw)
    {
        if (draw == null) { RemoveLive(key); return; }
        if (!_liveMap.TryGetValue(key, out var v))
        {
            v = new DrawingVisual();
            _liveMap[key] = v;
            _live.Children.Add(v);
        }
        using var dc = v.RenderOpen();
        draw(dc);
    }

    public void RemoveLive(object key)
    {
        if (_liveMap.Remove(key, out var v)) _live.Children.Remove(v);
    }

    public void ClearLive()
    {
        _liveMap.Clear();
        _live.Children.Clear();
    }
}
