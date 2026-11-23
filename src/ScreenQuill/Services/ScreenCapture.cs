using System;
using System.Collections.Generic;

using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ScreenQuill.Core;
using ScreenQuill.Native;

namespace ScreenQuill.Services;

/// <summary>
/// Region screenshots. The composed desktop (applications + ink overlays + board) is frozen first,
/// then the user drags a rectangle on per-monitor selection windows showing that frozen image.
/// The selection UI is therefore never part of the output. Everything stays local.
/// </summary>
public static class ScreenCapture
{
    /// <summary>Captures the whole virtual screen (all monitors) in physical pixels.</summary>
    public static (BitmapSource Image, PixelRect Bounds) CaptureVirtualScreen()
    {
        var vs = Win32.VirtualScreen();
        var bounds = new PixelRect(vs.Left, vs.Top, vs.Width, vs.Height);
        IntPtr screen = Win32.GetDC(IntPtr.Zero);
        IntPtr mem = Win32.CreateCompatibleDC(screen);
        IntPtr hbmp = Win32.CreateCompatibleBitmap(screen, bounds.Width, bounds.Height);
        IntPtr old = Win32.SelectObject(mem, hbmp);
        try
        {
            // CAPTUREBLT includes layered (per-pixel-alpha) windows such as the ink overlays.
            if (!Win32.BitBlt(mem, 0, 0, bounds.Width, bounds.Height, screen, bounds.X, bounds.Y, Win32.SRCCOPY | Win32.CAPTUREBLT))
                throw new System.ComponentModel.Win32Exception();
            Win32.SelectObject(mem, old);
            var src = Imaging.CreateBitmapSourceFromHBitmap(hbmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var conv = new FormatConvertedBitmap(src, PixelFormats.Bgr32, null, 0);
            conv.Freeze();
            return (conv, bounds);
        }
        finally
        {
            Win32.DeleteObject(hbmp);
            Win32.DeleteDC(mem);
            Win32.ReleaseDC(IntPtr.Zero, screen);
        }
    }

    public static BitmapSource Crop(BitmapSource full, PixelRect fullBounds, RectD sel)
    {
        var r = new Int32Rect((int)(sel.X - fullBounds.X), (int)(sel.Y - fullBounds.Y), (int)sel.Width, (int)sel.Height);
        var c = new CroppedBitmap(full, r);
        c.Freeze();
        return c;
    }

    public static void SavePng(BitmapSource img, string path)
    {
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(img));
        using var fs = System.IO.File.Create(path);
        enc.Save(fs);
    }

    /// <summary>Copies to the clipboard, retrying briefly if another process holds it open.</summary>
    public static bool CopyToClipboard(BitmapSource img)
    {
        for (int i = 0; i < 10; i++)
        {
            try { Clipboard.SetImage(img); return true; }
            catch (System.Runtime.InteropServices.COMException) { System.Threading.Thread.Sleep(50); }
        }
        return false;
    }

    /// <summary>Shows the region selector across all monitors. Returns null if cancelled (Esc / right-click).</summary>
    public static Task<RectD?> SelectRegionAsync(BitmapSource frozen, PixelRect bounds, IReadOnlyList<MonitorInfo> monitors)
    {
        var tcs = new TaskCompletionSource<RectD?>();
        var session = new RegionSession(tcs);
        foreach (var m in monitors)
        {
            var w = new RegionSelectWindow(session, frozen, bounds, m);
            session.Windows.Add(w);
            w.Show();
        }
        // Give keyboard focus to the window under the cursor so Escape works immediately.
        Win32.GetCursorPos(out var p);
        var focus = session.Windows.FirstOrDefault(w => w.Monitor.Bounds.Contains(p.X, p.Y)) ?? session.Windows.FirstOrDefault();
        focus?.ActivateForKeys();
        return tcs.Task;
    }

    internal sealed class RegionSession
    {
        private readonly TaskCompletionSource<RectD?> _tcs;
        public RegionSession(TaskCompletionSource<RectD?> tcs) => _tcs = tcs;
        public List<RegionSelectWindow> Windows { get; } = new();
        public Pt? Start { get; set; }
        public Pt Current { get; set; }

        public void Changed()
        {
            foreach (var w in Windows) w.UpdateSelection();
        }

        public void Finish(RectD? result)
        {
            if (_tcs.Task.IsCompleted) return;
            foreach (var w in Windows.ToList()) w.Close();
            Windows.Clear();
            _tcs.TrySetResult(result);
        }
    }

    internal sealed class RegionSelectWindow : Window
    {
        private readonly RegionSession _s;
        private readonly Canvas _canvas = new();
        private readonly Path _dim;
        private readonly Rectangle _outline;
        private readonly Border _label;
        private readonly TextBlock _labelText;
        private IntPtr _hwnd;
        private double _scale = 1;

        public RegionSelectWindow(RegionSession s, BitmapSource frozen, PixelRect all, MonitorInfo m)
        {
            _s = s; Monitor = m;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            Cursor = Cursors.Cross;
            Title = "ScreenQuill – select screenshot region";
            Left = m.Bounds.X / m.Scale; Top = m.Bounds.Y / m.Scale;
            Width = m.Bounds.Width / m.Scale; Height = m.Bounds.Height / m.Scale;

            var crop = new CroppedBitmap(frozen, new Int32Rect(m.Bounds.X - all.X, m.Bounds.Y - all.Y, m.Bounds.Width, m.Bounds.Height));
            var img = new Image { Source = crop, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
            _dim = new Path { Fill = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)), IsHitTestVisible = false };
            _outline = new Rectangle { Stroke = new SolidColorBrush(Color.FromRgb(0x2F, 0xC5, 0xEA)), StrokeThickness = 1.5, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            _labelText = new TextBlock { Foreground = Brushes.White, FontSize = 12, Margin = new Thickness(6, 2, 6, 2) };
            _label = new Border { Background = new SolidColorBrush(Color.FromArgb(200, 20, 20, 20)), CornerRadius = new CornerRadius(3), Child = _labelText, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            var hint = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(200, 20, 20, 20)), CornerRadius = new CornerRadius(4),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 24, 0, 0),
                IsHitTestVisible = false,
                Child = new TextBlock { Text = "Drag to capture a region  ·  Esc or right-click to cancel", Foreground = Brushes.White, FontSize = 13, Margin = new Thickness(12, 6, 12, 6) },
            };
            var grid = new Grid();
            grid.Children.Add(img);
            grid.Children.Add(_canvas);
            _canvas.Children.Add(_dim);
            _canvas.Children.Add(_outline);
            _canvas.Children.Add(_label);
            grid.Children.Add(hint);
            Content = grid;

            SourceInitialized += (_, _) =>
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                Win32.AddExStyle(_hwnd, Win32.WS_EX_TOOLWINDOW, Win32.WS_EX_APPWINDOW);
                Pin();
            };
            DpiChanged += (_, _) => Dispatcher.BeginInvoke(Pin);
            Loaded += (_, _) => { Pin(); UpdateSelection(); };

            MouseLeftButtonDown += (_, e) =>
            {
                _s.Start = _s.Current = CursorGlobal();
                CaptureMouse();
                _s.Changed();
            };
            MouseMove += (_, e) =>
            {
                if (_s.Start == null) return;
                _s.Current = CursorGlobal();
                _s.Changed();
            };
            MouseLeftButtonUp += (_, e) =>
            {
                if (_s.Start == null) return;
                ReleaseMouseCapture();
                var r = GeometryMath.NormalizeRect(_s.Start.Value, CursorGlobal());
                _s.Finish(r.Width >= 3 && r.Height >= 3 ? r : null);
            };
            MouseRightButtonUp += (_, _) => _s.Finish(null);
            KeyDown += (_, e) => { if (e.Key == Key.Escape) _s.Finish(null); };
            Deactivated += (_, _) => { /* keep open; Esc works once re-focused */ };
        }

        public MonitorInfo Monitor { get; }

        private void Pin()
        {
            if (_hwnd == IntPtr.Zero) return;
            var b = Monitor.Bounds;
            Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, b.X, b.Y, b.Width, b.Height, Win32.SWP_NOACTIVATE);
            _scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            UpdateSelection();
        }

        public void ActivateForKeys()
        {
            if (_hwnd != IntPtr.Zero) Win32.SetForegroundWindow(_hwnd);
            Activate();
            Focus();
        }

        /// <summary>Physical cursor position: exact across monitors/DPI, independent of WPF layout.</summary>
        private static Pt CursorGlobal() { Win32.GetCursorPos(out var p); return new Pt(p.X, p.Y); }

        private Pt ToGlobal(Point local) => new(Monitor.Bounds.X + local.X * _scale, Monitor.Bounds.Y + local.Y * _scale);
        private Point ToLocal(Pt g) => new((g.X - Monitor.Bounds.X) / _scale, (g.Y - Monitor.Bounds.Y) / _scale);

        public void UpdateSelection()
        {
            var full = new RectangleGeometry(new Rect(0, 0, Math.Max(1, ActualWidth), Math.Max(1, ActualHeight)));
            if (_s.Start == null)
            {
                _dim.Data = full;
                _outline.Visibility = _label.Visibility = Visibility.Collapsed;
                return;
            }
            var r = GeometryMath.NormalizeRect(_s.Start.Value, _s.Current);
            var tl = ToLocal(new Pt(r.X, r.Y));
            var br = ToLocal(new Pt(r.Right, r.Bottom));
            var hole = new Rect(tl, br);
            _dim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(hole));
            _outline.Visibility = Visibility.Visible;
            Canvas.SetLeft(_outline, hole.X); Canvas.SetTop(_outline, hole.Y);
            _outline.Width = hole.Width; _outline.Height = hole.Height;
            _labelText.Text = $"{(int)r.Width} × {(int)r.Height}";
            _label.Visibility = Visibility.Visible;
            Canvas.SetLeft(_label, hole.X);
            Canvas.SetTop(_label, hole.Y > 26 ? hole.Y - 24 : hole.Bottom + 4);
        }
    }
}
