using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using ScreenQuill.Core;
using ScreenQuill.Native;
using ScreenQuill.Overlay;
using ScreenQuill.Services;

namespace ScreenQuill.Toolbar;

/// <summary>
/// The floating vertical toolbar, laid out to match the reference screenshots:
/// round black logo (drag handle / collapse), then a white 42-DIP-wide column with
/// eye · cursor · fading ink · pen · shapes · eraser · size dot · undo · clear · board · camera · menu,
/// then the 2×2 quick colours (C/Y/M/K) and the large current-colour swatch.
/// </summary>
public sealed class ToolbarWindow : Window
{
    private readonly AppController _app;
    private IntPtr _hwnd;
    private readonly StackPanel _body;
    private readonly Border _bodyBorder;
    private readonly ToolButton _eye, _cursor, _fade, _pen, _shape, _eraser, _size, _undo, _clear, _board, _camera, _menu;
    private readonly Border _currentSwatch;
    private readonly Border[] _quick = new Border[4];
    private FlyoutWindow? _flyout;
    private string? _flyoutName;
    private bool _dragging, _dragMoved;
    private Win32.POINT _dragStartCursor;
    private Win32.RECT _dragStartRect;

    public ToolbarWindow(AppController app)
    {
        _app = app;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Title = "ScreenQuill";
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        var root = new StackPanel { Margin = new Thickness(8, 4, 8, 10) }; // room for the drop shadow

        // ---- logo / drag handle ----
        var logo = new Image { Source = Icons.Logo, Width = 40, Height = 40, Cursor = Cursors.SizeAll, ToolTip = "Drag to move · click to collapse/expand" };
        RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
        logo.MouseLeftButtonDown += LogoDown;
        logo.MouseMove += LogoMove;
        logo.MouseLeftButtonUp += LogoUp;
        logo.MouseRightButtonUp += (_, e) => { ShowFlyout("menu", _menuAnchor ?? logo, BuildMenu()); e.Handled = true; };
        root.Children.Add(new Border
        {
            Child = logo, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10),
            Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Opacity = 0.3, Direction = 270 },
        });

        // ---- buttons ----
        _eye = Add("eye", Icons.Eye, "Show / hide ink");
        _cursor = Add("cursor", Icons.Cursor, "Cursor – use your apps (ink stays on screen)", flyout: true);
        _fade = Add("fade", Icons.FadeOff, "Fading ink", flyout: true);
        _pen = Add("pen", Icons.Pen, "Pen", flyout: true);
        _shape = Add("shape", Icons.Line, "Shapes – line, arrow, rectangle, ellipse", flyout: true);
        _eraser = Add("eraser", Icons.Eraser, "Eraser – remove individual marks");
        _size = Add("size", Icons.SizeDot(4, Colors.Black), "Brush size");
        _undo = Add("undo", Icons.Undo, "Undo (right-click: redo)");
        _clear = Add("clear", Icons.Trash, "Clear all ink");
        _board = Add("board", Icons.Board, "Whiteboard / blackboard", flyout: true);
        _camera = Add("camera", Icons.Camera, "Screenshot – select a region (Esc cancels)");
        _menu = Add("menu", Icons.Clipboard, "Menu – settings, clipboard screenshot, Ghost Mode, exit");
        _menuAnchor = _menu;

        _body = new StackPanel();
        foreach (var b in new[] { _eye, _cursor, _fade, _pen, _shape, _eraser, _size, _undo, _clear, _board, _camera, _menu })
            _body.Children.Add(b);

        // ---- colours: 2×2 quick swatches + big current swatch ----
        var colours = new Grid { Width = 40, Margin = new Thickness(0, 2, 0, 0) };
        colours.ColumnDefinitions.Add(new ColumnDefinition());
        colours.ColumnDefinitions.Add(new ColumnDefinition());
        colours.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) });
        colours.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) });
        colours.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
        for (int i = 0; i < 4; i++)
        {
            int idx = i;
            var sw = new Border { Cursor = Cursors.Hand, ToolTip = "Quick colour (right-click: replace with current colour)" };
            sw.MouseLeftButtonUp += (_, e) => { CloseFlyout(); _app.SetColor(QuickColor(idx)); e.Handled = true; };
            sw.MouseRightButtonUp += (_, e) => { _app.AssignQuickColor(idx); e.Handled = true; };
            Grid.SetRow(sw, i / 2); Grid.SetColumn(sw, i % 2);
            colours.Children.Add(sw);
            _quick[i] = sw;
        }
        _currentSwatch = new Border { Cursor = Cursors.Hand, ToolTip = "Colour palette", BorderBrush = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)), BorderThickness = new Thickness(0, 1, 0, 0) };
        _currentSwatch.MouseLeftButtonUp += (_, e) => { ToggleFlyout("palette", _currentSwatch, BuildPalette); e.Handled = true; };
        Grid.SetRow(_currentSwatch, 2); Grid.SetColumnSpan(_currentSwatch, 2);
        colours.Children.Add(_currentSwatch);
        _body.Children.Add(colours);

        _bodyBorder = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC5, 0xC5, 0xC5)),
            BorderThickness = new Thickness(1),
            Child = _body,
            HorizontalAlignment = HorizontalAlignment.Center,
            Effect = new DropShadowEffect { BlurRadius = 7, ShadowDepth = 1.5, Opacity = 0.32, Direction = 270 },
        };
        root.Children.Add(_bodyBorder);
        Content = root;

        WireButtons();
        SourceInitialized += OnSourceInitialized;
        Refresh();
    }

    private UIElement? _menuAnchor;

    private ToolButton Add(string name, ImageSource icon, string tip, bool flyout = false) => new(name, icon, tip, flyout);

    public IntPtr Handle => _hwnd;
    public bool IsCollapsed => _bodyBorder.Visibility != Visibility.Visible;

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        Win32.AddExStyle(_hwnd, Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE, Win32.WS_EX_APPWINDOW);
        HwndSource.FromHwnd(_hwnd)!.AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
        {
            if (msg == Win32.WM_MOUSEACTIVATE) { handled = true; return new IntPtr(Win32.MA_NOACTIVATE); }
            return IntPtr.Zero;
        });
    }

    private void WireButtons()
    {
        _eye.Clicked += (_, _) => { CloseFlyout(); _app.ToggleInkVisible(); };
        _cursor.Clicked += (b, tri) => Tool(b, tri, ToolKind.Cursor, BuildCursorFlyout);
        _cursor.FlyoutRequested += b => ToggleFlyout(b.Name, b, BuildCursorFlyout);
        _fade.Clicked += (b, tri) =>
        {
            if (tri) { ToggleFlyout(b.Name, b, BuildFadeFlyout); return; }
            CloseFlyout();
            _app.ToggleFade();
        };
        _fade.FlyoutRequested += b => ToggleFlyout(b.Name, b, BuildFadeFlyout);
        _pen.Clicked += (b, tri) => Tool(b, tri, _penVariant, BuildPenFlyout);
        _pen.FlyoutRequested += b => ToggleFlyout(b.Name, b, BuildPenFlyout);
        _shape.Clicked += (b, tri) => Tool(b, tri, _app.Tools.LastShapeTool, BuildShapeFlyout);
        _shape.FlyoutRequested += b => ToggleFlyout(b.Name, b, BuildShapeFlyout);
        _eraser.Clicked += (_, _) => { CloseFlyout(); _app.SelectTool(ToolKind.Eraser); };
        _size.Clicked += (b, _) => ToggleFlyout(b.Name, b, BuildSizeFlyout);
        _undo.Clicked += (_, _) => { CloseFlyout(); _app.Undo(); };
        _undo.MouseRightButtonUp += (_, e) => { _app.Redo(); e.Handled = true; };
        _clear.Clicked += (_, _) => { CloseFlyout(); _app.ClearAll(); };
        _board.Clicked += (b, tri) =>
        {
            if (tri) { ToggleFlyout(b.Name, b, BuildBoardFlyout); return; }
            CloseFlyout();
            _app.ToggleBoard();
        };
        _board.FlyoutRequested += b => ToggleFlyout(b.Name, b, BuildBoardFlyout);
        _camera.Clicked += (_, _) => { CloseFlyout(); _app.Screenshot(clipboardOnly: false); };
        _menu.Clicked += (b, _) => ToggleFlyout(b.Name, b, BuildMenu);
    }

    private ToolKind _penVariant = ToolKind.Pen;

    /// <summary>Click selects the tool; clicking the triangle (or the already-active tool) opens its flyout.</summary>
    private void Tool(ToolButton b, bool onTriangle, ToolKind tool, Func<UIElement> flyout)
    {
        if (onTriangle || (b.IsSelected && b.HasFlyout && tool != ToolKind.Cursor))
        {
            ToggleFlyout(b.Name, b, flyout);
            return;
        }
        CloseFlyout();
        _app.SelectTool(tool);
    }

    private uint QuickColor(int i) => _app.Settings.RecentColors.ElementAtOrDefault(i) is var c && c != 0 ? c : Palette.DefaultRecent[i];

    /// <summary>Synchronises every button with the current state.</summary>
    public void Refresh()
    {
        var t = _app.Tools;
        _eye.IsSelected = t.InkVisible;
        _eye.Icon = t.InkVisible ? Icons.Eye : Icons.EyeClosed;
        _cursor.IsSelected = t.Tool == ToolKind.Cursor;
        _fade.IsSelected = t.Fade != FadeMode.Off;
        _fade.Icon = t.Fade == FadeMode.Off ? Icons.FadeOff : Icons.FadeOn;
        _fade.ToolTip = t.Fade == FadeMode.Off ? "Fading ink: off" : $"Fading ink: {t.Fade} ({_app.Settings.FadeSeconds(t.Fade):0.#} s)";

        var inkTool = t.Tool is ToolKind.Pen or ToolKind.Highlighter or ToolKind.Text ? t.Tool : _penVariant;
        if (t.Tool is ToolKind.Pen or ToolKind.Highlighter or ToolKind.Text) _penVariant = t.Tool;
        _pen.Icon = Icons.For(inkTool);
        _pen.ToolTip = inkTool.DisplayName();
        _pen.IsSelected = t.Tool is ToolKind.Pen or ToolKind.Highlighter or ToolKind.Text;
        _shape.Icon = Icons.For(t.LastShapeTool);
        _shape.ToolTip = $"Shapes – {t.LastShapeTool.DisplayName()}";
        _shape.IsSelected = t.Tool.IsShape();
        _eraser.IsSelected = t.Tool == ToolKind.Eraser;
        _size.Icon = Icons.SizeDot(t.CurrentSize, Colors.Black);
        _size.ToolTip = $"{(t.Tool == ToolKind.Cursor ? t.LastDrawingTool : t.Tool).DisplayName()} size: {t.CurrentSize:0.#}";
        _undo.Opacity = _app.Doc.CanUndo ? 1 : 0.45;
        _board.IsSelected = t.Board != BoardMode.None;
        _cursor.HasFlyout = true;

        _currentSwatch.Background = new SolidColorBrush(AnnotationRenderer.ToColor(t.Color));
        for (int i = 0; i < 4; i++) _quick[i].Background = new SolidColorBrush(AnnotationRenderer.ToColor(QuickColor(i)));

        foreach (var tb in _body.Children.OfType<ToolButton>()) ToolTipService.SetIsEnabled(tb, _app.Settings.ShowTooltips);
        if (_flyout != null && _flyoutName is "fade" or "board" or "cursor" or "pen" or "shape") RebuildFlyout();
    }

    // ---- logo drag / collapse --------------------------------------------------------------
    private void LogoDown(object sender, MouseButtonEventArgs e)
    {
        CloseFlyout();
        _dragging = true; _dragMoved = false;
        Win32.GetCursorPos(out _dragStartCursor);
        Win32.GetWindowRect(_hwnd, out _dragStartRect);
        ((UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    private void LogoMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        Win32.GetCursorPos(out var p);
        int dx = p.X - _dragStartCursor.X, dy = p.Y - _dragStartCursor.Y;
        if (!_dragMoved && Math.Abs(dx) + Math.Abs(dy) < 4) return;
        _dragMoved = true;
        Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, _dragStartRect.Left + dx, _dragStartRect.Top + dy, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
    }

    private void LogoUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ((UIElement)sender).ReleaseMouseCapture();
        if (_dragMoved) { ClampToScreen(); _app.SaveToolbarPosition(); }
        else SetCollapsed(!IsCollapsed);
        e.Handled = true;
    }

    public void SetCollapsed(bool collapsed)
    {
        CloseFlyout();
        _bodyBorder.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---- positioning -----------------------------------------------------------------------
    public Win32.RECT PixelRect()
    {
        Win32.GetWindowRect(_hwnd, out var r);
        return r;
    }

    public void MoveTo(int x, int y) =>
        Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, x, y, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);

    /// <summary>Keeps the whole toolbar on some monitor's work area (after drags and display changes).</summary>
    public void ClampToScreen()
    {
        if (_hwnd == IntPtr.Zero) return;
        var r = PixelRect();
        var ms = Monitors.GetAll();
        var m = Monitors.FromPoint(ms, r.Left + r.Width / 2, r.Top + 30);
        var w = m.WorkArea;
        int x = Math.Clamp(r.Left, w.X, Math.Max(w.X, w.Right - r.Width));
        int y = Math.Clamp(r.Top, w.Y, Math.Max(w.Y, w.Bottom - Math.Min(r.Height, w.Height)));
        if (x != r.Left || y != r.Top) MoveTo(x, y);
    }

    public void BringToFront()
    {
        if (_hwnd != IntPtr.Zero) Win32.BringToTopmost(_hwnd);
        if (_flyout != null) Win32.BringToTopmost(_flyout.Handle);
    }

    // ---- flyouts ------------------------------------------------------------------------------
    public bool HasFlyout => _flyout != null;

    public bool FlyoutContains(int x, int y)
    {
        bool In(Win32.RECT r) => x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom;
        return (_flyout != null && In(_flyout.PixelRect())) || (_hwnd != IntPtr.Zero && In(PixelRect()));
    }

    private Func<UIElement>? _flyoutBuilder;
    private FrameworkElement? _flyoutAnchor;

    private void ToggleFlyout(string name, FrameworkElement anchor, Func<UIElement> build)
    {
        if (_flyoutName == name) { CloseFlyout(); return; }
        ShowFlyout(name, anchor, build());
        _flyoutBuilder = build;
    }

    private void ShowFlyout(string name, UIElement anchorEl, UIElement content)
    {
        CloseFlyout();
        var anchor = (FrameworkElement)anchorEl;
        var tb = PixelRect();
        var ms = Monitors.GetAll();
        var mon = Monitors.FromPoint(ms, tb.Left + tb.Width / 2, tb.Top + 30);
        bool openLeft = tb.Left + tb.Width / 2 > mon.Bounds.X + mon.Bounds.Width / 2;
        var f = new FlyoutWindow(content, notchOnLeft: !openLeft, notchY: 20) { Opacity = 0 };
        _flyout = f; _flyoutName = name; _flyoutAnchor = anchor;
        f.Closed += (_, _) => { if (_flyout == f) { _flyout = null; _flyoutName = null; _app.OnFlyoutChanged(false); } };
        f.Show();
        PositionFlyout(f, anchor, openLeft, name == "palette");
        f.Opacity = 1;
        _app.OnFlyoutChanged(true);
    }

    private void PositionFlyout(FlyoutWindow f, FrameworkElement anchor, bool openLeft, bool alignBottom)
    {
        f.UpdateLayout();
        var fr = f.PixelRect();
        var body = _bodyBorder.IsVisible ? _bodyBorder : (FrameworkElement)this;
        var bodyTopLeft = body.PointToScreen(new Point(0, 0));
        var bodyBottomRight = body.PointToScreen(new Point(body.ActualWidth, body.ActualHeight));
        var aTop = anchor.PointToScreen(new Point(0, 0));
        var aBottom = anchor.PointToScreen(new Point(0, anchor.ActualHeight));
        double scale = VisualTreeHelper.GetDpi(f).DpiScaleX;
        int margin = (int)Math.Round(FlyoutWindow.OuterMargin * scale);
        int gap = (int)Math.Round(2 * scale);
        int x = openLeft ? (int)bodyTopLeft.X - fr.Width + margin - gap : (int)bodyBottomRight.X - margin + gap;
        int y = alignBottom ? (int)aBottom.Y - fr.Height + margin : (int)aTop.Y - margin;
        var ms = Monitors.GetAll();
        var w = Monitors.FromPoint(ms, (int)aTop.X, (int)aTop.Y).WorkArea;
        y = Math.Clamp(y, w.Y, Math.Max(w.Y, w.Bottom - fr.Height));
        x = Math.Clamp(x, w.X, Math.Max(w.X, w.Right - fr.Width));
        f.MoveTo(x, y);
        double anchorMid = (aTop.Y + aBottom.Y) / 2;
        f.SetNotchY((anchorMid - y) / scale);
    }

    private void RebuildFlyout()
    {
        if (_flyout == null || _flyoutBuilder == null || _flyoutAnchor == null || _flyoutName == null) return;
        var name = _flyoutName; var b = _flyoutBuilder; var a = _flyoutAnchor;
        _flyoutName = null; // allow re-show
        ShowFlyout(name, a, b());
        _flyoutBuilder = b;
    }

    public void CloseFlyout()
    {
        var f = _flyout;
        _flyout = null; _flyoutName = null; _flyoutBuilder = null;
        f?.Close();
    }

    // ---- flyout content -----------------------------------------------------------------------
    private static readonly Brush ItemHover = new SolidColorBrush(Color.FromRgb(0xE3, 0xF5, 0xFC));

    private UIElement Item(ImageSource icon, string text, bool selected, Action click, string? hint = null)
    {
        var grid = new Grid { Height = 32, MinWidth = 190 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var img = new Image { Source = icon, Width = 20, Height = 20 };
        grid.Children.Add(img);
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, FontSize = 13, FontFamily = new FontFamily("Segoe UI"), Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)) };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);
        if (hint != null)
        {
            var h = new TextBlock { Text = hint, VerticalAlignment = VerticalAlignment.Center, FontSize = 11, Margin = new Thickness(12, 0, 8, 0), Foreground = Brushes.Gray };
            Grid.SetColumn(h, 2);
            grid.Children.Add(h);
        }
        var border = new Border { Child = grid, Background = selected ? ToolButton.SelectedBrush : Brushes.Transparent, Cursor = Cursors.Hand };
        border.MouseEnter += (_, _) => { if (!selected) border.Background = ItemHover; };
        border.MouseLeave += (_, _) => { if (!selected) border.Background = Brushes.Transparent; };
        border.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        return border;
    }

    private string Hk(HotkeyAction a) => _app.HotkeyText(a);

    private UIElement Stack(params UIElement[] items)
    {
        var s = new StackPanel();
        foreach (var i in items) s.Children.Add(i);
        return s;
    }

    private UIElement BuildCursorFlyout() => Stack(
        Item(Icons.Cursor, "Cursor (use apps)", _app.Tools.Tool == ToolKind.Cursor, () => { CloseFlyout(); _app.SelectTool(ToolKind.Cursor); }, Hk(HotkeyAction.Cursor)),
        Item(Icons.CursorHighlight, "Highlighted cursor", _app.Tools.CursorHighlight, () => _app.ToggleCursorHighlight(), Hk(HotkeyAction.CursorHighlight)));

    private UIElement BuildFadeFlyout()
    {
        var s = _app.Settings;
        UIElement F(FadeMode m, string label) => Item(m == FadeMode.Off ? Icons.FadeOff : Icons.FadeOn,
            m == FadeMode.Off ? label : $"{label} ({s.FadeSeconds(m):0.#} s)", _app.Tools.Fade == m, () => { _app.SetFade(m); CloseFlyout(); });
        return Stack(F(FadeMode.Off, "Fading ink off"), F(FadeMode.Short, "Short"), F(FadeMode.Medium, "Medium"), F(FadeMode.Long, "Long"));
    }

    private UIElement BuildPenFlyout()
    {
        var t = _app.Tools.Tool;
        return Stack(
            Item(Icons.Pen, "Pen", t == ToolKind.Pen, () => { CloseFlyout(); _app.SelectTool(ToolKind.Pen); }, Hk(HotkeyAction.Pen)),
            Item(Icons.Highlighter, "Highlighter", t == ToolKind.Highlighter, () => { CloseFlyout(); _app.SelectTool(ToolKind.Highlighter); }, Hk(HotkeyAction.Highlighter)),
            Item(Icons.Text, "Text", t == ToolKind.Text, () => { CloseFlyout(); _app.SelectTool(ToolKind.Text); }, Hk(HotkeyAction.Text)));
    }

    private UIElement BuildShapeFlyout()
    {
        var t = _app.Tools.Tool;
        UIElement S(ToolKind k, HotkeyAction a) => Item(Icons.For(k), k.DisplayName(), t == k, () => { CloseFlyout(); _app.SelectTool(k); }, Hk(a));
        return Stack(S(ToolKind.Line, HotkeyAction.Line), S(ToolKind.Arrow, HotkeyAction.Arrow), S(ToolKind.Rectangle, HotkeyAction.Rectangle), S(ToolKind.Ellipse, HotkeyAction.Ellipse));
    }

    private UIElement BuildBoardFlyout()
    {
        var b = _app.Tools.Board;
        return Stack(
            Item(Icons.Desktop, "Desktop (transparent)", b == BoardMode.None, () => { CloseFlyout(); _app.SetBoard(BoardMode.None); }),
            Item(Icons.Whiteboard, "Whiteboard", b == BoardMode.Whiteboard, () => { CloseFlyout(); _app.SetBoard(BoardMode.Whiteboard); }, Hk(HotkeyAction.Whiteboard)),
            Item(Icons.Blackboard, "Blackboard", b == BoardMode.Blackboard, () => { CloseFlyout(); _app.SetBoard(BoardMode.Blackboard); }, Hk(HotkeyAction.Blackboard)));
    }

    private UIElement BuildMenu() => Stack(
        Item(Icons.Clipboard, "Screenshot to clipboard", false, () => { CloseFlyout(); _app.Screenshot(clipboardOnly: true); }, Hk(HotkeyAction.ScreenshotToClipboard)),
        Item(Icons.Folder, "Open screenshots folder", false, () => { CloseFlyout(); _app.OpenScreenshotFolder(); }),
        Item(Icons.Redo, "Redo", false, () => { _app.Redo(); }, Hk(HotkeyAction.Redo)),
        Item(Icons.Ghost, "Ghost Mode (hide toolbar)", false, () => { CloseFlyout(); _app.SetGhostMode(true); }, Hk(HotkeyAction.GhostMode)),
        Item(Icons.Settings, "Settings…", false, () => { CloseFlyout(); _app.OpenSettings(); }),
        Item(Icons.Help, "Hotkeys & help…", false, () => { CloseFlyout(); _app.OpenSettings("Hotkeys"); }),
        Item(Icons.Exit, "Exit ScreenQuill", false, () => { CloseFlyout(); _app.Exit(); }));

    private UIElement BuildSizeFlyout()
    {
        var t = _app.Tools;
        var tool = t.Tool == ToolKind.Cursor ? t.LastDrawingTool : t.Tool;
        var panel = new StackPanel { Width = 220, Margin = new Thickness(6) };
        var title = new TextBlock { Text = $"{tool.DisplayName()} size", FontSize = 12, Foreground = Brushes.DimGray, Margin = new Thickness(0, 0, 0, 4) };
        panel.Children.Add(title);
        var presets = new WrapPanel();
        double[] values = tool == ToolKind.Text ? new double[] { 14, 20, 28, 40, 56, 80 }
            : tool == ToolKind.Highlighter ? new double[] { 10, 16, 22, 30, 42, 60 }
            : tool == ToolKind.Eraser ? new double[] { 10, 20, 30, 50, 80, 120 }
            : new double[] { 2, 3, 4, 6, 10, 16 };
        var value = new TextBlock { FontSize = 12, Width = 34, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var slider = new Slider { Minimum = ToolState.MinSize, Maximum = ToolState.MaxSize, Value = t.SizeOf(tool), Width = 176, IsSnapToTickEnabled = false, Focusable = false, VerticalAlignment = VerticalAlignment.Center };
        foreach (var v in values)
        {
            double vv = v;
            var dot = new Border
            {
                Width = 35, Height = 35, Cursor = Cursors.Hand,
                Background = Math.Abs(t.SizeOf(tool) - v) < 0.01 ? ToolButton.SelectedBrush : Brushes.Transparent,
                Child = new Image { Source = Icons.SizeDot(v, AnnotationRenderer.ToColor(t.Color) is var c && c.R + c.G + c.B > 720 ? Colors.Black : c), Width = 24, Height = 24 },
                ToolTip = v.ToString("0.#"),
            };
            dot.MouseLeftButtonUp += (_, e) => { _app.SetSize(tool, vv); slider.Value = vv; e.Handled = true; };
            presets.Children.Add(dot);
        }
        panel.Children.Add(presets);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        value.Text = t.SizeOf(tool).ToString("0.#");
        slider.ValueChanged += (_, e) => { _app.SetSize(tool, e.NewValue); value.Text = t.SizeOf(tool).ToString("0.#"); };
        row.Children.Add(slider);
        row.Children.Add(value);
        panel.Children.Add(row);
        return panel;
    }

    private UIElement BuildPalette()
    {
        var panel = new StackPanel();
        var grid = new UniformGrid { Columns = Palette.Columns, Rows = Palette.Rows };
        foreach (var c in Palette.Quick) grid.Children.Add(Swatch(c, 40, 40));
        panel.Children.Add(grid);

        // Custom colours row: saved custom colours + "+" to open the picker.
        var custom = new UniformGrid { Columns = 3, Margin = new Thickness(0, 3, 0, 0) };
        foreach (var c in _app.Settings.CustomColors.Take(2)) custom.Children.Add(Swatch(c, 40, 28));
        while (custom.Children.Count < 2) custom.Children.Add(new Border { Width = 40, Height = 28, Background = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2)) });
        var plus = new Border
        {
            Width = 40, Height = 28, Cursor = Cursors.Hand, ToolTip = "Custom colour…",
            Background = new LinearGradientBrush(new GradientStopCollection
            {
                new(Colors.Red, 0), new(Colors.Yellow, 0.2), new(Colors.Lime, 0.4), new(Colors.Cyan, 0.6), new(Colors.Blue, 0.8), new(Colors.Magenta, 1)
            }, 0),
            Child = new TextBlock { Text = "+", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -4, 0, 0) },
        };
        plus.MouseLeftButtonUp += (_, e) => { e.Handled = true; CloseFlyout(); _app.PickCustomColor(); };
        custom.Children.Add(plus);
        panel.Children.Add(custom);
        return panel;
    }

    private Border Swatch(uint c, double w, double h)
    {
        bool selected = (c | 0xFF000000) == (_app.Tools.Color | 0xFF000000);
        var b = new Border
        {
            Width = w, Height = h, Cursor = Cursors.Hand, ToolTip = Palette.ToHex(c),
            Background = new SolidColorBrush(AnnotationRenderer.ToColor(c)),
            BorderThickness = new Thickness(selected ? 3 : 0),
            BorderBrush = selected ? (IsDark(c) ? Brushes.White : Brushes.Black) : null,
        };
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; _app.SetColor(c); CloseFlyout(); };
        return b;
    }

    private static bool IsDark(uint c) => ((c >> 16) & 0xFF) * 0.3 + ((c >> 8) & 0xFF) * 0.59 + (c & 0xFF) * 0.11 < 140;
}
