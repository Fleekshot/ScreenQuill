namespace ScreenQuill.Core.Model;

public enum DocumentChange { Added, Removed, Changed, Reset }

public sealed class DocumentChangedEventArgs : EventArgs
{
    public DocumentChangedEventArgs(DocumentChange change, IReadOnlyList<Annotation> items) { Change = change; Items = items; }
    public DocumentChange Change { get; }
    public IReadOnlyList<Annotation> Items { get; }
}

/// <summary>
/// The retained annotation model shared by every monitor overlay. Coordinates are global physical pixels.
/// Supports undo/redo (supplemental quality-of-life feature) and independent per-element fade lifetimes.
/// Not thread-safe: use from the UI thread.
/// </summary>
public sealed class AnnotationDocument
{
    private readonly List<Annotation> _items = new();
    private readonly Stack<ICommand> _undo = new();
    private readonly Stack<ICommand> _redo = new();
    private readonly Func<double> _clock;

    public AnnotationDocument(Func<double>? clock = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _clock = clock ?? (() => sw.Elapsed.TotalSeconds);
    }

    public event EventHandler<DocumentChangedEventArgs>? Changed;

    public double Clock => _clock();
    public IReadOnlyList<Annotation> Items => _items;
    public int Count => _items.Count;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public bool HasFadingItems => _items.Any(i => i.Fade != null);

    /// <summary>Adds a finished element (stamps CreatedAt if unset) and records an undo step.</summary>
    public void Add(Annotation a)
    {
        if (a.CreatedAt == 0) a.CreatedAt = Clock;
        Execute(new AddCommand(a));
    }

    public void Remove(IEnumerable<Annotation> items)
    {
        var list = items.Where(_items.Contains).Distinct().ToList();
        if (list.Count > 0) Execute(new RemoveCommand(list));
    }

    /// <summary>Removes every annotation (ink, shapes and text). Undoable.</summary>
    public void Clear()
    {
        if (_items.Count > 0) Execute(new RemoveCommand(_items.ToList()));
    }

    /// <summary>Replaces an element with an edited copy (text edits, moves). Undoable.</summary>
    public void Replace(Annotation oldItem, Annotation newItem)
    {
        if (!_items.Contains(oldItem)) { Add(newItem); return; }
        Execute(new ReplaceCommand(oldItem, newItem));
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        var c = _undo.Pop();
        c.Undo(this);
        _redo.Push(c);
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        var c = _redo.Pop();
        c.Do(this);
        _undo.Push(c);
    }

    public IEnumerable<Annotation> HitTest(Pt p, double tolerance)
    {
        for (int i = _items.Count - 1; i >= 0; i--)
            if (_items[i].HitTest(p, tolerance)) yield return _items[i];
    }

    /// <summary>Opacity multiplier for an element at the current time (1 for non-fading).</summary>
    public double FadeOpacity(Annotation a) => a.Fade == null ? 1 : a.Fade.OpacityAt(Clock - a.CreatedAt);

    /// <summary>
    /// Removes fading elements whose lifetime has expired. Expired elements are also purged from the
    /// undo/redo history so undo can never resurrect ink that has faded away. Returns removed elements.
    /// </summary>
    public IReadOnlyList<Annotation> RemoveExpired()
    {
        double now = Clock;
        bool IsExpired(Annotation i) => i.Fade != null && now - i.CreatedAt >= i.Fade.Total;
        var expired = _items.Where(IsExpired).ToList();
        // Elements that already left the canvas (erased/cleared) can still expire inside the history.
        var historyExpired = _undo.Concat(_redo).SelectMany(c => c.References).Where(IsExpired).Distinct().ToList();
        if (historyExpired.Count > 0)
        {
            Purge(_undo, historyExpired);
            Purge(_redo, historyExpired);
        }
        if (expired.Count == 0) return expired;
        foreach (var e in expired) _items.Remove(e);
        Changed?.Invoke(this, new DocumentChangedEventArgs(DocumentChange.Removed, expired));
        return expired;
    }

    private static void Purge(Stack<ICommand> stack, List<Annotation> expired)
    {
        var keep = stack.Reverse().Where(c => c.Prune(expired)).ToList();
        stack.Clear();
        foreach (var c in keep) stack.Push(c);
    }

    private void Execute(ICommand c)
    {
        c.Do(this);
        _undo.Push(c);
        _redo.Clear();
    }

    // ---- low level mutation used by commands -------------------------------------------------
    private void InsertRaw(IReadOnlyList<(int Index, Annotation Item)> entries)
    {
        foreach (var (idx, item) in entries.OrderBy(e => e.Index))
            _items.Insert(Math.Clamp(idx, 0, _items.Count), item);
        Changed?.Invoke(this, new DocumentChangedEventArgs(DocumentChange.Added, entries.Select(e => e.Item).ToList()));
    }

    private List<(int, Annotation)> RemoveRaw(IReadOnlyList<Annotation> items)
    {
        var removed = items.Select(i => (_items.IndexOf(i), i)).Where(t => t.Item1 >= 0).OrderBy(t => t.Item1).ToList();
        foreach (var (_, i) in removed) _items.Remove(i);
        Changed?.Invoke(this, new DocumentChangedEventArgs(DocumentChange.Removed, removed.Select(t => t.i).ToList()));
        return removed;
    }

    private interface ICommand
    {
        IEnumerable<Annotation> References { get; }
        /// <summary>Drops references to expired elements; returns false if the command became meaningless.</summary>
        bool Prune(ICollection<Annotation> expired);
        void Do(AnnotationDocument d);
        void Undo(AnnotationDocument d);
    }

    private sealed class AddCommand : ICommand
    {
        private readonly Annotation _a;
        public AddCommand(Annotation a) => _a = a;
        public IEnumerable<Annotation> References => new[] { _a };
        public bool Prune(ICollection<Annotation> expired) => !expired.Contains(_a);
        public void Do(AnnotationDocument d) => d.InsertRaw(new[] { (d._items.Count, _a) });
        public void Undo(AnnotationDocument d) => d.RemoveRaw(new[] { _a });
    }

    private sealed class RemoveCommand : ICommand
    {
        private readonly List<Annotation> _items;
        private List<(int, Annotation)> _positions = new();
        public RemoveCommand(List<Annotation> items) => _items = items;
        public IEnumerable<Annotation> References => _items;
        public bool Prune(ICollection<Annotation> expired)
        {
            _items.RemoveAll(expired.Contains);
            _positions.RemoveAll(p => expired.Contains(p.Item2));
            return _items.Count > 0;
        }
        public void Do(AnnotationDocument d) => _positions = d.RemoveRaw(_items);
        public void Undo(AnnotationDocument d) => d.InsertRaw(_positions);
    }

    private sealed class ReplaceCommand : ICommand
    {
        private readonly Annotation _old, _new;
        private int _index;
        public ReplaceCommand(Annotation o, Annotation n) { _old = o; _new = n; }
        public IEnumerable<Annotation> References => new[] { _old, _new };
        public bool Prune(ICollection<Annotation> expired) => !expired.Contains(_old) && !expired.Contains(_new);
        public void Do(AnnotationDocument d) { _index = d.RemoveRaw(new[] { _old }).Select(t => t.Item1).DefaultIfEmpty(d._items.Count).First(); d.InsertRaw(new[] { (_index, _new) }); }
        public void Undo(AnnotationDocument d) { d.RemoveRaw(new[] { _new }); d.InsertRaw(new[] { (_index, _old) }); }
    }
}
