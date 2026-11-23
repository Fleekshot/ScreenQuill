using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using ScreenQuill.Native;

namespace ScreenQuill.Toolbar;

/// <summary>
/// Non-activating topmost popup used for every toolbar flyout (palette, sizes, tool variants, menu).
/// It never takes focus from the application the user is annotating.
/// </summary>
public sealed class FlyoutWindow : Window
{
    private IntPtr _hwnd;
    private readonly System.Windows.Shapes.Path _notch;

    public FlyoutWindow(UIElement content, bool notchOnLeft, double notchY)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Title = "ScreenQuill Flyout";
        Focusable = false;

        var panel = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC5, 0xC5, 0xC5)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(3),
            Child = content,
            Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 1.5, Opacity = 0.35, Direction = 270 },
        };
        NotchLeft = notchOnLeft;
        // Small pointer notch toward the toolbar (visible in the reference palette flyout).
        var notch = _notch = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(notchOnLeft ? "M6,0 L0,6 L6,12 Z" : "M0,0 L6,6 L0,12 Z"),
            Fill = Brushes.White,
            Stroke = new SolidColorBrush(Color.FromRgb(0xC5, 0xC5, 0xC5)),
            StrokeThickness = 1,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = notchOnLeft ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            Margin = new Thickness(notchOnLeft ? 1 : 0, Math.Max(4, notchY - 6), notchOnLeft ? 0 : 1, 0),
        };
        var grid = new Grid { Margin = new Thickness(6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(notch, notchOnLeft ? 0 : 2);
        Grid.SetColumn(panel, 1);
        panel.Margin = new Thickness(notchOnLeft ? -1 : 0, 0, notchOnLeft ? 0 : -1, 0);
        grid.Children.Add(panel);
        grid.Children.Add(notch);
        Panel.SetZIndex(notch, 1);
        Content = grid;
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Win32.AddExStyle(_hwnd, Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE, Win32.WS_EX_APPWINDOW);
            HwndSource.FromHwnd(_hwnd)!.AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
            {
                if (msg == Win32.WM_MOUSEACTIVATE) { handled = true; return new IntPtr(Win32.MA_NOACTIVATE); }
                return IntPtr.Zero;
            });
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    public bool NotchLeft { get; }

    /// <summary>Moves the pointer notch so it points at the given y (DIPs from the window top).</summary>
    public void SetNotchY(double y)
    {
        var m = _notch.Margin;
        _notch.Margin = new Thickness(m.Left, Math.Clamp(y - OuterMargin - 6, 4, Math.Max(4, ActualHeight - 2 * OuterMargin - 16)), m.Right, 0);
    }
    public IntPtr Handle => _hwnd;

    /// <summary>Margin (DIP) between the window edge and the visible panel/notch.</summary>
    public const double OuterMargin = 6;

    public Win32.RECT PixelRect()
    {
        Win32.GetWindowRect(_hwnd, out var r);
        return r;
    }

    public void MoveTo(int x, int y) =>
        Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, x, y, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
}
