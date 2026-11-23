using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ScreenQuill.Core;
using ScreenQuill.Overlay;

namespace ScreenQuill.Settings;

/// <summary>Custom colour picker: saturation/value square, hue strip, hex and RGB entry.</summary>
public sealed class ColorPickerWindow : Window
{
    private double _h, _s, _v;
    private readonly Rectangle _hueBase = new();
    private readonly Canvas _sv = new() { Width = 220, Height = 160, ClipToBounds = true, Cursor = Cursors.Cross };
    private readonly Ellipse _svThumb = new() { Width = 12, Height = 12, Stroke = Brushes.White, StrokeThickness = 2, IsHitTestVisible = false };
    private readonly Slider _hue = new() { Minimum = 0, Maximum = 360, Width = 220, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBox _hex = new() { Width = 90 };
    private readonly TextBox _r = new() { Width = 40 }, _g = new() { Width = 40 }, _b = new() { Width = 40 };
    private readonly Border _preview = new() { Width = 60, Height = 40, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) };
    private bool _updating;

    public ColorPickerWindow(uint initial)
    {
        Title = "Custom colour";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        ShowInTaskbar = false;

        var white = new Rectangle { Width = 220, Height = 160, Fill = new LinearGradientBrush(Colors.White, Color.FromArgb(0, 255, 255, 255), 0) };
        var black = new Rectangle { Width = 220, Height = 160, Fill = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Colors.Black, 90) };
        _hueBase.Width = 220; _hueBase.Height = 160;
        _sv.Children.Add(_hueBase); _sv.Children.Add(white); _sv.Children.Add(black); _sv.Children.Add(_svThumb);
        _sv.MouseLeftButtonDown += (_, e) => { _sv.CaptureMouse(); PickSv(e.GetPosition(_sv)); };
        _sv.MouseMove += (_, e) => { if (_sv.IsMouseCaptured) PickSv(e.GetPosition(_sv)); };
        _sv.MouseLeftButtonUp += (_, _) => _sv.ReleaseMouseCapture();

        var rainbow = new GradientStopCollection();
        for (int i = 0; i <= 6; i++) rainbow.Add(new GradientStop(FromHsv(i * 60, 1, 1), i / 6.0));
        var hueBar = new Rectangle { Height = 10, Width = 220, Fill = new LinearGradientBrush(rainbow, 0), Margin = new Thickness(0, 8, 0, 0) };
        _hue.ValueChanged += (_, e) => { if (_updating) return; _h = e.NewValue; Update(fromText: false); };

        var rgbRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        rgbRow.Children.Add(Label("Hex")); rgbRow.Children.Add(_hex);
        rgbRow.Children.Add(Label("R")); rgbRow.Children.Add(_r);
        rgbRow.Children.Add(Label("G")); rgbRow.Children.Add(_g);
        rgbRow.Children.Add(Label("B")); rgbRow.Children.Add(_b);
        _hex.TextChanged += (_, _) => { if (!_updating && Palette.TryParseHex(_hex.Text, out var c)) SetFrom(c, fromText: true); };
        foreach (var tb in new[] { _r, _g, _b })
            tb.TextChanged += (_, _) =>
            {
                if (_updating) return;
                if (byte.TryParse(_r.Text, out var r) && byte.TryParse(_g.Text, out var g) && byte.TryParse(_b.Text, out var b))
                    SetFrom(0xFF000000u | (uint)r << 16 | (uint)g << 8 | b, fromText: true);
            };

        var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => { DialogResult = true; };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(_preview); buttons.Children.Add(new Border { Width = 20 }); buttons.Children.Add(ok); buttons.Children.Add(cancel);

        var root = new StackPanel { Margin = new Thickness(14) };
        root.Children.Add(_sv); root.Children.Add(hueBar); root.Children.Add(_hue); root.Children.Add(rgbRow); root.Children.Add(buttons);
        Content = root;
        SetFrom(initial | 0xFF000000, fromText: false);
    }

    public uint SelectedColor { get; private set; }

    private static TextBlock Label(string t) => new() { Text = t, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) };

    private void PickSv(Point p)
    {
        _s = Math.Clamp(p.X / _sv.Width, 0, 1);
        _v = 1 - Math.Clamp(p.Y / _sv.Height, 0, 1);
        Update(fromText: false);
    }

    private void SetFrom(uint argb, bool fromText)
    {
        var c = AnnotationRenderer.ToColor(argb);
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        _v = max; _s = max == 0 ? 0 : d / max;
        if (d > 0)
        {
            _h = max == r ? 60 * (((g - b) / d) % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
            if (_h < 0) _h += 360;
        }
        Update(fromText);
    }

    private void Update(bool fromText)
    {
        _updating = true;
        var c = FromHsv(_h, _s, _v);
        SelectedColor = 0xFF000000u | (uint)c.R << 16 | (uint)c.G << 8 | c.B;
        _hueBase.Fill = new SolidColorBrush(FromHsv(_h, 1, 1));
        Canvas.SetLeft(_svThumb, _s * _sv.Width - 6);
        Canvas.SetTop(_svThumb, (1 - _v) * _sv.Height - 6);
        _svThumb.Stroke = _v > 0.6 && _s < 0.4 ? Brushes.Black : Brushes.White;
        _hue.Value = _h;
        _preview.Background = new SolidColorBrush(c);
        if (!fromText)
        {
            _hex.Text = Palette.ToHex(SelectedColor);
            _r.Text = c.R.ToString(); _g.Text = c.G.ToString(); _b.Text = c.B.ToString();
        }
        _updating = false;
    }

    private static Color FromHsv(double h, double s, double v)
    {
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        (double r, double g, double b) = (h % 360) switch
        {
            < 60 => (c, x, 0d), < 120 => (x, c, 0d), < 180 => (0d, c, x),
            < 240 => (0d, x, c), < 300 => (x, 0d, c), _ => (c, 0d, x),
        };
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}
