using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ScreenQuill.Core;
using ScreenQuill.Native;
using ScreenQuill.Services;

namespace ScreenQuill.Overlay;

/// <summary>
/// One transparent, topmost, non-activating, taskbar-less window covering exactly one monitor.
/// Cursor mode: WS_EX_TRANSPARENT + fully transparent background → every click, scroll and key reaches
/// the application underneath. Drawing mode: a 1/255-alpha background makes every pixel hit-testable.
/// </summary>
public sealed class OverlayWindow : Window
{
    private static readonly SolidColorBrush HitBrush = Freeze(new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)));
    private static readonly SolidColorBrush WhiteBoard = Freeze(new SolidColorBrush(Colors.White));
    private static readonly SolidColorBrush BlackBoard = Freeze(new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14)));

    private readonly InkInteraction _input;
    private IntPtr _hwnd;
    private bool _interactive;
    private bool _allowActivate;
    private BoardMode _board;
    private bool _placed;

    public OverlayWindow(MonitorInfo monitor, InkInteraction input)
    {
        Monitor = monitor;
        _input = input;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;
        Title = "ScreenQuill Overlay";
        // Place roughly on the target monitor before the HWND exists so it is created with that DPI.
        Left = monitor.Bounds.X / monitor.Scale; Top = monitor.Bounds.Y / monitor.Scale;
        Width = monitor.Bounds.Width / monitor.Scale; Height = monitor.Bounds.Height / monitor.Scale;

        Surface = new InkSurface();
        EditorLayer = new Canvas { Background = null };
        var grid = new Grid();
        grid.Children.Add(Surface);
        grid.Children.Add(EditorLayer);
        Content = grid;

        Stylus.SetIsPressAndHoldEnabled(this, false);
        Stylus.SetIsFlicksEnabled(this, false);
        Stylus.SetIsTapFeedbackEnabled(this, false);
        Stylus.SetIsTouchFeedbackEnabled(this, false);

        SourceInitialized += OnSourceInitialized;
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(ApplyBounds);
        Loaded += (_, _) => ApplyBounds();

        MouseDown += OnMouseDown; MouseMove += OnMouseMove; MouseUp += OnMouseUp;
        LostMouseCapture += (_, _) => _input.PointerCancel(InkInteraction.MouseKey, this);
        MouseLeave += (_, _) => _input.HoverLeave(this);
        StylusDown += OnStylusDown; StylusMove += OnStylusMove; StylusUp += OnStylusUp;
        StylusInAirMove += (_, e) => _input.Hover(Global(e.GetPosition(Surface)), this);
    }

    public MonitorInfo Monitor { get; private set; }
    public InkSurface Surface { get; }
    public Canvas EditorLayer { get; }
    public IntPtr Handle => _hwnd;

    private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        Win32.AddExStyle(_hwnd, Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TRANSPARENT, Win32.WS_EX_APPWINDOW);
        HwndSource.FromHwnd(_hwnd)!.AddHook(WndProc);
        ApplyBounds();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_MOUSEACTIVATE && !_allowActivate)
        {
            handled = true;
            return new IntPtr(Win32.MA_NOACTIVATE);
        }
        return IntPtr.Zero;
    }

    /// <summary>Pins the window to the monitor's exact physical-pixel bounds and refreshes the mapping.</summary>
    public void ApplyBounds()
    {
        if (_hwnd == IntPtr.Zero) return;
        var b = Monitor.Bounds;
        // Only the first placement touches z-order, so later DPI/bounds fixes never cover the toolbar.
        uint flags = Win32.SWP_NOACTIVATE | (_placed ? Win32.SWP_NOZORDER : 0);
        Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, b.X, b.Y, b.Width, b.Height, flags);
        _placed = true;
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        Surface.SetMapping(b.X, b.Y, scale);
    }

    public void UpdateMonitor(MonitorInfo m) { Monitor = m; ApplyBounds(); }

    /// <summary>Switches between drawing (captures input) and cursor (fully click-through) mode.</summary>
    public void SetInteractive(bool interactive)
    {
        _interactive = interactive;
        if (_hwnd != IntPtr.Zero)
        {
            if (interactive) Win32.AddExStyle(_hwnd, 0, Win32.WS_EX_TRANSPARENT);
            else Win32.AddExStyle(_hwnd, Win32.WS_EX_TRANSPARENT);
        }
        UpdateBackground();
        if (!interactive) Cursor = null;
    }

    public void SetBoard(BoardMode board)
    {
        _board = board;
        UpdateBackground();
    }

    private void UpdateBackground()
    {
        Background = _board switch
        {
            BoardMode.Whiteboard => WhiteBoard,
            BoardMode.Blackboard => BlackBoard,
            _ => _interactive ? HitBrush : Brushes.Transparent,
        };
    }

    public void SetInkVisible(bool visible) => Surface.Visibility = visible ? Visibility.Visible : Visibility.Hidden;

    /// <summary>Temporarily allows keyboard focus (text editing) then returns to non-activating.</summary>
    public void AllowActivation(bool allow)
    {
        _allowActivate = allow;
        if (_hwnd == IntPtr.Zero) return;
        if (allow)
        {
            Win32.AddExStyle(_hwnd, 0, Win32.WS_EX_NOACTIVATE);
            Win32.SetForegroundWindow(_hwnd);
            Activate();
        }
        else Win32.AddExStyle(_hwnd, Win32.WS_EX_NOACTIVATE);
    }

    private Pt Global(Point local) => Surface.ToGlobal(local);

    // ---- mouse ---------------------------------------------------------------------------
    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.StylusDevice != null || !_interactive) return; // promoted from stylus/touch: handled there
        if (e.ChangedButton == MouseButton.Right) { _input.RightClick(this); e.Handled = true; return; }
        if (e.ChangedButton != MouseButton.Left) return;
        CaptureMouse();
        _input.PointerDown(InkInteraction.MouseKey, Global(e.GetPosition(Surface)), 0.5f, false, false, this);
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.StylusDevice != null || !_interactive) return;
        var p = Global(e.GetPosition(Surface));
        if (e.LeftButton == MouseButtonState.Pressed && IsMouseCaptured) _input.PointerMove(InkInteraction.MouseKey, p, 0.5f, this);
        else _input.Hover(p, this);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.StylusDevice != null || e.ChangedButton != MouseButton.Left) return;
        if (!IsMouseCaptured) return;
        _input.PointerUp(InkInteraction.MouseKey, Global(e.GetPosition(Surface)), this);
        ReleaseMouseCapture();
        e.Handled = true;
    }

    // ---- stylus / touch (each contact is an independent stroke) ------------------------
    private static object StylusKey(StylusDevice d) => d.Id;

    private void OnStylusDown(object sender, StylusDownEventArgs e)
    {
        if (!_interactive) return;
        var pts = e.GetStylusPoints(Surface);
        bool hasPressure = e.StylusDevice.TabletDevice?.Type == TabletDeviceType.Stylus &&
                           pts.Description.HasProperty(System.Windows.Input.StylusPointProperties.NormalPressure);
        bool eraser = e.Inverted || IsBarrelPressed(e.StylusDevice);
        e.StylusDevice.Capture(this);
        var first = pts.Count > 0 ? pts[0] : new System.Windows.Input.StylusPoint(e.GetPosition(Surface).X, e.GetPosition(Surface).Y);
        _input.PointerDown(StylusKey(e.StylusDevice), Global(first.ToPoint()), first.PressureFactor, hasPressure, eraser, this);
        for (int i = 1; i < pts.Count; i++) _input.PointerMove(StylusKey(e.StylusDevice), Global(pts[i].ToPoint()), pts[i].PressureFactor, this);
        e.Handled = true;
    }

    private void OnStylusMove(object sender, StylusEventArgs e)
    {
        if (!_interactive) return;
        foreach (var sp in e.GetStylusPoints(Surface))
            _input.PointerMove(StylusKey(e.StylusDevice), Global(sp.ToPoint()), sp.PressureFactor, this);
        e.Handled = true;
    }

    private void OnStylusUp(object sender, StylusEventArgs e)
    {
        var pts = e.GetStylusPoints(Surface);
        var last = pts.Count > 0 ? pts[^1].ToPoint() : e.GetPosition(Surface);
        _input.PointerUp(StylusKey(e.StylusDevice), Global(last), this);
        if (e.StylusDevice.Captured == this) e.StylusDevice.Capture(null);
        e.Handled = true;
    }

    private static bool IsBarrelPressed(StylusDevice d)
    {
        foreach (var b in d.StylusButtons)
            if (b.Guid == StylusPointProperties.BarrelButton.Id && b.StylusButtonState == StylusButtonState.Down) return true;
        return false;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _input.ForgetOverlay(this);
    }
}
