using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ScreenQuill.Toolbar;

/// <summary>
/// One toolbar cell: 40×36 DIP, 24 DIP icon, cyan fill when selected, pale hover, optional
/// bottom-right black triangle indicating a flyout (as in the reference toolbar).
/// </summary>
public sealed class ToolButton : Border
{
    public static readonly Color SelectedColor = Color.FromRgb(0x2F, 0xC5, 0xEA);
    public static readonly Brush SelectedBrush = Frozen(new SolidColorBrush(SelectedColor));
    public static readonly Brush HoverBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xE3, 0xF5, 0xFC)));
    public static readonly Brush SelectedHoverBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x5C, 0xD3, 0xF0)));
    public static readonly Brush PressedBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xBF, 0xEA, 0xF8)));

    public const double CellWidth = 40, CellHeight = 36, IconSize = 24;

    private readonly Image _icon;
    private readonly Path _triangle;
    private bool _selected, _pressed;

    public ToolButton(string name, ImageSource icon, string tooltip, bool hasFlyout = false)
    {
        Name = name;
        Width = CellWidth; Height = CellHeight;
        Background = Brushes.Transparent;
        SnapsToDevicePixels = true;
        _icon = new Image { Source = icon, Width = IconSize, Height = IconSize, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(_icon, BitmapScalingMode.HighQuality);
        _triangle = new Path
        {
            Data = Geometry.Parse("M7,0 L7,7 L0,7 Z"),
            Fill = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 1, 1),
            Visibility = hasFlyout ? Visibility.Visible : Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        var grid = new Grid();
        grid.Children.Add(_icon);
        grid.Children.Add(_triangle);
        Child = grid;
        ToolTip = tooltip;
        ToolTipService.SetInitialShowDelay(this, 500);
        ToolTipService.SetPlacement(this, System.Windows.Controls.Primitives.PlacementMode.Left);
        Cursor = Cursors.Hand;
    }

    private static Brush Frozen(SolidColorBrush b) { b.Freeze(); return b; }

    /// <summary>Raised on left click. Argument is true when the click landed on the flyout triangle.</summary>
    public event Action<ToolButton, bool>? Clicked;
    public event Action<ToolButton>? FlyoutRequested;

    public bool HasFlyout
    {
        get => _triangle.Visibility == Visibility.Visible;
        set => _triangle.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public ImageSource Icon { get => _icon.Source; set => _icon.Source = value; }

    public bool IsSelected
    {
        get => _selected;
        set { _selected = value; UpdateVisual(); }
    }

    protected override void OnMouseEnter(MouseEventArgs e) { base.OnMouseEnter(e); UpdateVisual(); }
    protected override void OnMouseLeave(MouseEventArgs e) { base.OnMouseLeave(e); _pressed = false; UpdateVisual(); }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _pressed = true;
        CaptureMouse();
        UpdateVisual();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        bool wasPressed = _pressed;
        _pressed = false;
        ReleaseMouseCapture();
        UpdateVisual();
        var p = e.GetPosition(this);
        if (wasPressed && p.X >= 0 && p.Y >= 0 && p.X <= ActualWidth && p.Y <= ActualHeight)
        {
            bool onTriangle = HasFlyout && p.X >= ActualWidth - 14 && p.Y >= ActualHeight - 14;
            Clicked?.Invoke(this, onTriangle);
        }
        e.Handled = true;
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        if (HasFlyout) FlyoutRequested?.Invoke(this);
        e.Handled = true;
    }

    private void UpdateVisual()
    {
        Background = _pressed ? PressedBrush
            : _selected ? (IsMouseOver ? SelectedHoverBrush : SelectedBrush)
            : IsMouseOver ? HoverBrush : Brushes.Transparent;
    }
}
