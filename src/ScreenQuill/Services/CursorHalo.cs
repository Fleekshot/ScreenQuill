using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using ScreenQuill.Native;

namespace ScreenQuill.Services;

/// <summary>
/// Runs on its own thread with its own message loop so that the low-level mouse hook can never be
/// delayed by UI work (a slow LL hook lags the whole system cursor). It owns:
///  • the highlighted-cursor halo: a tiny click-through, non-activating layered window re-positioned
///    on every mouse move and re-rendered when the monitor DPI under the cursor changes;
///  • mouse-down notifications used to dismiss toolbar flyouts when clicking elsewhere.
/// The hook is only installed while the halo is on or a flyout is open.
/// </summary>
public sealed class CursorHalo : IDisposable
{
    private const uint WM_APP_CMD = Win32.WM_USER + 77;
    private readonly Thread _thread;
    private readonly ConcurrentQueue<Action> _queue = new();
    private readonly ManualResetEventSlim _ready = new();
    private uint _threadId;
    private IntPtr _hwnd, _hook;
    private Win32.WndProc? _wndProc;
    private Win32.HookProc? _hookProc;

    // state (halo thread only)
    private bool _haloOn, _wantClicks, _hiddenForCapture;
    private uint _color = 0xFFFFEB3B;
    private double _radiusDip = 32, _opacity = 0.45;
    private bool _ring = true;
    private double _renderedScale = -1;
    private int _sizePx;
    private int _lastX = int.MinValue, _lastY = int.MinValue;
    private bool _timerOn;

    public CursorHalo()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "ScreenQuill cursor halo / mouse hook" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(3000);
    }

    /// <summary>Raised (on the hook thread) for every mouse button press, with physical coordinates.</summary>
    public event Action<int, int>? MouseDown;

    public void Configure(bool on, uint color, double radiusDip, double opacity, bool ring) => Post(() =>
    {
        bool restyle = color != _color || Math.Abs(radiusDip - _radiusDip) > 0.01 || Math.Abs(opacity - _opacity) > 0.001 || ring != _ring;
        _color = color; _radiusDip = radiusDip; _opacity = opacity; _ring = ring;
        if (restyle) _renderedScale = -1;
        _haloOn = on;
        UpdateHook();
        if (_haloOn && !_hiddenForCapture)
        {
            Win32.GetCursorPos(out var p);
            MoveTo(p.X, p.Y);
        }
        else Win32.ShowWindow(_hwnd, 0);
    });

    public void SetWantClicks(bool want) => Post(() => { _wantClicks = want; UpdateHook(); });

    /// <summary>Hides the halo synchronously (for screenshots) and restores it afterwards.</summary>
    public void SetHiddenForCapture(bool hidden)
    {
        using var done = new ManualResetEventSlim();
        Post(() =>
        {
            _hiddenForCapture = hidden;
            if (hidden || !_haloOn) Win32.ShowWindow(_hwnd, 0);
            else { Win32.GetCursorPos(out var p); MoveTo(p.X, p.Y); }
            done.Set();
        });
        done.Wait(1000);
    }

    private void Post(Action a)
    {
        _queue.Enqueue(a);
        Win32.PostThreadMessage(_threadId, WM_APP_CMD, IntPtr.Zero, IntPtr.Zero);
    }

    private void Run()
    {
        _threadId = Win32.GetCurrentThreadId();
        _wndProc = (h, m, w, l) => Win32.DefWindowProc(h, m, w, l);
        var wc = new Win32.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<Win32.WNDCLASSEX>(),
            lpfnWndProc = _wndProc,
            hInstance = Win32.GetModuleHandle(null),
            lpszClassName = "ScreenQuillCursorHalo",
        };
        Win32.RegisterClassEx(ref wc);
        _hwnd = Win32.CreateWindowEx(
            Win32.WS_EX_LAYERED | Win32.WS_EX_TRANSPARENT | Win32.WS_EX_TOPMOST | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE,
            wc.lpszClassName, "ScreenQuill Cursor Halo", Win32.WS_POPUP, 0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
        _hookProc = HookCallback;
        _ready.Set();

        while (Win32.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == Win32.WM_TIMER && msg.hwnd == _hwnd)
            {
                // Fallback for cursor moves that bypass the input stream (SetCursorPos, remote tools, pen hover).
                if (_haloOn && !_hiddenForCapture && Win32.GetCursorPos(out var cp) && (cp.X != _lastX || cp.Y != _lastY)) MoveTo(cp.X, cp.Y);
                continue;
            }
            if (msg.message == WM_APP_CMD)
            {
                while (_queue.TryDequeue(out var a))
                {
                    try { a(); } catch { /* never let a bad command kill the hook thread */ }
                }
                continue;
            }
            Win32.TranslateMessage(ref msg);
            Win32.DispatchMessage(ref msg);
        }
        if (_hook != IntPtr.Zero) Win32.UnhookWindowsHookEx(_hook);
        Win32.DestroyWindow(_hwnd);
    }

    private void UpdateHook()
    {
        if (_haloOn && !_timerOn) { Win32.SetTimer(_hwnd, (IntPtr)1, 15, IntPtr.Zero); _timerOn = true; }
        else if (!_haloOn && _timerOn) { Win32.KillTimer(_hwnd, (IntPtr)1); _timerOn = false; }
        bool need = _haloOn || _wantClicks;
        if (need && _hook == IntPtr.Zero)
            _hook = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, _hookProc!, Win32.GetModuleHandle(null), 0);
        else if (!need && _hook != IntPtr.Zero)
        {
            Win32.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            var data = Marshal.PtrToStructure<Win32.MSLLHOOKSTRUCT>(lParam);
            if (msg == Win32.WM_MOUSEMOVE)
            {
                if (_haloOn && !_hiddenForCapture) MoveTo(data.pt.X, data.pt.Y);
            }
            else if (msg is Win32.WM_LBUTTONDOWN or Win32.WM_RBUTTONDOWN or Win32.WM_MBUTTONDOWN)
            {
                if (_wantClicks) MouseDown?.Invoke(data.pt.X, data.pt.Y);
            }
        }
        return Win32.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private void MoveTo(int x, int y)
    {
        _lastX = x; _lastY = y;
        double scale = Win32.DpiScaleAt(x, y);
        if (Math.Abs(scale - _renderedScale) > 0.001) Render(scale);
        int half = _sizePx / 2;
        Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, x - half, y - half, 0, 0,
            Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);
    }

    /// <summary>Renders the halo bitmap (premultiplied ARGB) at the given DPI scale.</summary>
    private void Render(double scale)
    {
        _renderedScale = scale;
        double r = _radiusDip * scale;
        _sizePx = (int)Math.Ceiling(r * 2 + 4 * scale) | 1;
        using var bmp = new Bitmap(_sizePx, _sizePx, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            var c = Color.FromArgb((byte)(_color >> 16), (byte)(_color >> 8), (byte)_color);
            float cx = _sizePx / 2f;
            using (var fill = new SolidBrush(Color.FromArgb((int)(255 * _opacity), c)))
                g.FillEllipse(fill, (float)(cx - r), (float)(cx - r), (float)(2 * r), (float)(2 * r));
            if (_ring)
            {
                float w = (float)(2 * scale);
                using var pen = new Pen(Color.FromArgb((int)(255 * Math.Min(1, _opacity + 0.35)), c), w);
                g.DrawEllipse(pen, (float)(cx - r + w / 2), (float)(cx - r + w / 2), (float)(2 * r - w), (float)(2 * r - w));
            }
        }
        IntPtr screen = Win32.GetDC(IntPtr.Zero);
        IntPtr mem = Win32.CreateCompatibleDC(screen);
        IntPtr hbmp = bmp.GetHbitmap(Color.FromArgb(0));
        IntPtr old = Win32.SelectObject(mem, hbmp);
        try
        {
            var size = new Win32.SIZE(_sizePx, _sizePx);
            var src = new Win32.POINT(0, 0);
            Win32.GetCursorPos(out var p);
            var dst = new Win32.POINT(p.X - _sizePx / 2, p.Y - _sizePx / 2);
            var blend = new Win32.BLENDFUNCTION { BlendOp = Win32.AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = Win32.AC_SRC_ALPHA };
            Win32.UpdateLayeredWindow(_hwnd, screen, ref dst, ref size, mem, ref src, 0, ref blend, Win32.ULW_ALPHA);
        }
        finally
        {
            Win32.SelectObject(mem, old);
            Win32.DeleteObject(hbmp);
            Win32.DeleteDC(mem);
            Win32.ReleaseDC(IntPtr.Zero, screen);
        }
    }

    public void Dispose()
    {
        if (_threadId != 0) Win32.PostThreadMessage(_threadId, Win32.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(1000);
    }
}
