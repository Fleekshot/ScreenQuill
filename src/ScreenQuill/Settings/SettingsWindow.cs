using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ScreenQuill.Core;
using ScreenQuill.Overlay;
using ScreenQuill.Services;

namespace ScreenQuill.Settings;

/// <summary>
/// Settings dialog. Edits a copy of the preferences; OK/Apply validate and persist, Cancel discards.
/// Hotkeys: click a binding, press a chord. Global hotkeys are suspended while recording so the
/// chord being recorded cannot trigger an action. Duplicates, reserved chords, unmodified letters
/// and chords already owned by other applications are flagged before they can be saved.
/// </summary>
public sealed class SettingsWindow : Window
{
    private readonly AppController _app;
    private readonly AppSettings _edit;
    private readonly Dictionary<HotkeyAction, Hotkey> _keys;
    private readonly TabControl _tabs = new() { Margin = new Thickness(10, 10, 10, 0) };
    private readonly Dictionary<HotkeyAction, (Button Button, TextBlock Status)> _keyRows = new();
    private readonly TextBlock _hotkeySummary = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly Button _ok, _apply;
    private HotkeyAction? _recording;

    public SettingsWindow(AppController app)
    {
        _app = app;
        _edit = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(app.Settings))!;
        _edit.LaunchAtStartup = StartupRegistration.IsSet();
        _keys = _edit.GetHotkeys();
        Title = "ScreenQuill Settings";
        Width = 640; Height = 600;
        MinWidth = 520; MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/ScreenQuill.ico"));

        _tabs.Items.Add(Tab("General", General()));
        _tabs.Items.Add(Tab("Tools", ToolsTab()));
        _tabs.Items.Add(Tab("Fading & Cursor", FadeCursorTab()));
        _tabs.Items.Add(Tab("Screenshots", ScreenshotTab()));
        _tabs.Items.Add(Tab("Hotkeys", HotkeyTab()));
        _tabs.Items.Add(Tab("About", AboutTab()));

        _ok = new Button { Content = "OK", Width = 86, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        _apply = new Button { Content = "Apply", Width = 86, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 86, IsCancel = true };
        _ok.Click += (_, _) => { if (Commit()) Close(); };
        _apply.Click += (_, _) => Commit();
        cancel.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(10) };
        buttons.Children.Add(_ok); buttons.Children.Add(_apply); buttons.Children.Add(cancel);

        var dock = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        dock.Children.Add(buttons);
        dock.Children.Add(_tabs);
        Content = dock;

        PreviewKeyDown += OnPreviewKeyDown;
        Closed += (_, _) => StopRecording();
        RefreshHotkeys();
    }

    public void SelectTab(string? name)
    {
        if (name == null) return;
        foreach (TabItem t in _tabs.Items) if ((string)t.Header == name) _tabs.SelectedItem = t;
    }

    private static TabItem Tab(string header, UIElement content) => new()
    {
        Header = header,
        Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(14) },
    };

    private static StackPanel Panel() => new();
    private static TextBlock Heading(string t) => new() { Text = t, FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 10, 0, 6) };
    private static TextBlock Note(string t) => new() { Text = t, Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 8), FontSize = 12 };

    private static CheckBox Check(string label, bool value, Action<bool> set)
    {
        var c = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 4, 0, 4) };
        c.Checked += (_, _) => set(true);
        c.Unchecked += (_, _) => set(false);
        return c;
    }

    private static UIElement SliderRow(string label, double min, double max, double value, string format, Action<double> set, double step = 1)
    {
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        var l = new TextBlock { Text = label, Width = 190, VerticalAlignment = VerticalAlignment.Center };
        var v = new TextBlock { Width = 60, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Text = value.ToString(format) };
        var s = new Slider { Minimum = min, Maximum = max, Value = value, SmallChange = step, LargeChange = step * 5, IsSnapToTickEnabled = true, TickFrequency = step, VerticalAlignment = VerticalAlignment.Center };
        s.ValueChanged += (_, e) => { v.Text = e.NewValue.ToString(format); set(e.NewValue); };
        DockPanel.SetDock(l, Dock.Left); DockPanel.SetDock(v, Dock.Right);
        row.Children.Add(l); row.Children.Add(v); row.Children.Add(s);
        return row;
    }

    private UIElement General()
    {
        var p = Panel();
        p.Children.Add(Heading("Startup"));
        p.Children.Add(Check("Launch ScreenQuill when I sign in to Windows", _edit.LaunchAtStartup, v => _edit.LaunchAtStartup = v));
        p.Children.Add(Check("Start in Ghost Mode (toolbar hidden)", _edit.StartInGhostMode, v => _edit.StartInGhostMode = v));
        p.Children.Add(Check("Show notification-area (tray) icon", _edit.ShowTrayIcon, v => _edit.ShowTrayIcon = v));
        p.Children.Add(Heading("Toolbar"));
        p.Children.Add(Check("Show tooltips on toolbar buttons", _edit.ShowTooltips, v => _edit.ShowTooltips = v));
        var reset = new Button { Content = "Reset toolbar position", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 6, 0, 0) };
        reset.Click += (_, _) => { _app.Settings.ToolbarX = null; _app.Settings.ToolbarY = null; _edit.ToolbarX = null; _edit.ToolbarY = null; MessageBox.Show(this, "The toolbar will return to the top-right corner the next time ScreenQuill starts.", "ScreenQuill"); };
        p.Children.Add(reset);
        p.Children.Add(Heading("Ink visibility"));
        p.Children.Add(Check("Hide ink while in Cursor mode", _edit.HideInkWhenCursor, v => _edit.HideInkWhenCursor = v));
        p.Children.Add(Check("Show whiteboard / blackboard on all monitors (otherwise only the toolbar's monitor)", _edit.BoardOnAllMonitors, v => _edit.BoardOnAllMonitors = v));
        p.Children.Add(Heading("Privacy"));
        p.Children.Add(Note("ScreenQuill works entirely offline. It never uploads screenshots, screen contents or input, and collects no analytics."));
        return p;
    }

    private UIElement ToolsTab()
    {
        var p = Panel();
        p.Children.Add(Heading("Pen and highlighter"));
        p.Children.Add(SliderRow("Highlighter opacity", 10, 90, _edit.HighlighterOpacity * 100, "0'%'", v => _edit.HighlighterOpacity = v / 100));
        p.Children.Add(Check("Use pen pressure when the stylus reports it", _edit.UsePenPressure, v => _edit.UsePenPressure = v));
        p.Children.Add(Check("Smooth freehand strokes", _edit.SmoothInk, v => _edit.SmoothInk = v));
        p.Children.Add(Note("Each tool (pen, highlighter, eraser, text, shapes) keeps its own size; change it from the size button on the toolbar or with the size hotkeys."));
        p.Children.Add(Heading("Stylus and touch"));
        p.Children.Add(Note("Flip the stylus to its eraser end, or hold the barrel button while touching down, to erase. Each finger draws its own stroke on multi-touch screens. To map a pen-tablet button to a ScreenQuill action, assign that button the same key combination as the action's hotkey in your tablet's driver settings."));
        p.Children.Add(Heading("Shapes"));
        p.Children.Add(Note("Hold Shift while dragging to snap lines and arrows to 45° and to draw squares and circles."));
        p.Children.Add(Heading("Text"));
        p.Children.Add(Note("Click to place text. Enter commits, Shift+Enter adds a line, Esc cancels. Click existing text with the Text tool to edit it, or drag it to move it. Delete text with the eraser, or by removing all its characters."));
        return p;
    }

    private UIElement FadeCursorTab()
    {
        var p = Panel();
        p.Children.Add(Heading("Fading ink durations"));
        p.Children.Add(Note("How long each new mark stays fully visible before fading away. Epic Pen documents short / medium / long without publishing values; these defaults (2 s, 5 s, 10 s) are ScreenQuill's own."));
        p.Children.Add(SliderRow("Short", 0.5, 30, _edit.FadeShortSeconds, "0.0 's'", v => _edit.FadeShortSeconds = v, 0.5));
        p.Children.Add(SliderRow("Medium", 0.5, 60, _edit.FadeMediumSeconds, "0.0 's'", v => _edit.FadeMediumSeconds = v, 0.5));
        p.Children.Add(SliderRow("Long", 0.5, 120, _edit.FadeLongSeconds, "0.0 's'", v => _edit.FadeLongSeconds = v, 0.5));
        p.Children.Add(SliderRow("Fade-out time", 0, 5, _edit.FadeOutSeconds, "0.0 's'", v => _edit.FadeOutSeconds = v, 0.25));

        p.Children.Add(Heading("Highlighted cursor"));
        var colorRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        colorRow.Children.Add(new TextBlock { Text = "Halo colour", Width = 190, VerticalAlignment = VerticalAlignment.Center });
        var swatches = new uint[] { 0xFFFFEB3B, 0xFFFF5252, 0xFF2FC5EA, 0xFF4CAF50, 0xFFE040FB, 0xFFFF9800 };
        var chosen = new Border { Width = 28, Height = 28, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Background = new SolidColorBrush(AnnotationRenderer.ToColor(_edit.CursorHighlightColor)), Margin = new Thickness(0, 0, 10, 0) };
        colorRow.Children.Add(chosen);
        foreach (var c in swatches)
        {
            uint cc = c;
            var b = new Border { Width = 22, Height = 22, Margin = new Thickness(2), Cursor = Cursors.Hand, Background = new SolidColorBrush(AnnotationRenderer.ToColor(c)) };
            b.MouseLeftButtonUp += (_, _) => { _edit.CursorHighlightColor = cc; chosen.Background = b.Background; };
            colorRow.Children.Add(b);
        }
        var custom = new Button { Content = "Custom…", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 1, 8, 1) };
        custom.Click += (_, _) =>
        {
            var dlg = new ColorPickerWindow(_edit.CursorHighlightColor) { Owner = this };
            if (dlg.ShowDialog() == true) { _edit.CursorHighlightColor = dlg.SelectedColor; chosen.Background = new SolidColorBrush(AnnotationRenderer.ToColor(dlg.SelectedColor)); }
        };
        colorRow.Children.Add(custom);
        p.Children.Add(colorRow);
        p.Children.Add(SliderRow("Halo radius", 8, 120, _edit.CursorHighlightRadius, "0 'px'", v => _edit.CursorHighlightRadius = v));
        p.Children.Add(SliderRow("Halo opacity", 10, 100, _edit.CursorHighlightOpacity * 100, "0'%'", v => _edit.CursorHighlightOpacity = v / 100));
        p.Children.Add(Check("Draw a ring around the halo", _edit.CursorHighlightRing, v => _edit.CursorHighlightRing = v));
        p.Children.Add(Note("The halo never intercepts clicks and works in Cursor mode. It is hidden automatically while taking screenshots."));
        return p;
    }

    private UIElement ScreenshotTab()
    {
        var p = Panel();
        p.Children.Add(Heading("Save location"));
        var row = new DockPanel();
        var box = new TextBox { Text = _edit.ScreenshotFolder, VerticalContentAlignment = VerticalAlignment.Center };
        box.TextChanged += (_, _) => _edit.ScreenshotFolder = box.Text;
        var browse = new Button { Content = "Browse…", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
        browse.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Choose screenshot folder", InitialDirectory = System.IO.Directory.Exists(box.Text) ? box.Text : "" };
            if (dlg.ShowDialog(this) == true) box.Text = dlg.FolderName;
        };
        DockPanel.SetDock(browse, Dock.Right);
        row.Children.Add(browse); row.Children.Add(box);
        p.Children.Add(row);
        p.Children.Add(Note("Files are named \"ScreenQuill yyyy-MM-dd HH-mm-ss.png\"."));
        p.Children.Add(Check("Also copy saved screenshots to the clipboard", _edit.ScreenshotAlsoCopyToClipboard, v => _edit.ScreenshotAlsoCopyToClipboard = v));
        p.Children.Add(Check("Open the folder after saving", _edit.ScreenshotOpenFolderAfterSave, v => _edit.ScreenshotOpenFolderAfterSave = v));
        p.Children.Add(Heading("How it works"));
        p.Children.Add(Note("Click the camera (or press the Screenshot hotkey), then drag a rectangle on any monitor. Your ink, shapes, text and the whiteboard/blackboard are included; the toolbar, halo and selection frame are not. Esc or right-click cancels. 'Screenshot to clipboard' in the menu copies without saving."));
        return p;
    }

    private UIElement HotkeyTab()
    {
        var p = Panel();
        p.Children.Add(Note("Click a shortcut and press the new key combination. Esc cancels recording, Backspace clears the binding. Hotkeys work everywhere, including in Ghost Mode, but are paused while you type text annotations. Pressing a tool's hotkey while that tool is active returns to the cursor. Defaults are ScreenQuill's own (Epic Pen does not publish its defaults)."));
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        int r = 0;
        foreach (var a in Enum.GetValues<HotkeyAction>())
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
            var label = new TextBlock { Text = HotkeyRules.Describe(a), VerticalAlignment = VerticalAlignment.Center };
            var btn = new Button { Margin = new Thickness(0, 2, 6, 2), Tag = a };
            var action = a;
            btn.Click += (_, _) => StartRecording(action);
            var status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(label, r); Grid.SetRow(btn, r); Grid.SetRow(status, r);
            Grid.SetColumn(btn, 1); Grid.SetColumn(status, 2);
            grid.Children.Add(label); grid.Children.Add(btn); grid.Children.Add(status);
            _keyRows[a] = (btn, status);
            r++;
        }
        p.Children.Add(grid);
        var reset = new Button { Content = "Reset all to defaults", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 10, 0, 0) };
        reset.Click += (_, _) =>
        {
            StopRecording();
            foreach (var kv in HotkeyRules.Defaults()) _keys[kv.Key] = kv.Value;
            RefreshHotkeys();
        };
        p.Children.Add(reset);
        p.Children.Add(_hotkeySummary);
        return p;
    }

    private UIElement AboutTab()
    {
        var p = Panel();
        p.Children.Add(new TextBlock { Text = "ScreenQuill 1.0", FontSize = 20, FontWeight = FontWeights.SemiBold });
        p.Children.Add(Note("An independently implemented, always-on-top screen annotation tool for Windows 10/11, inspired by Epic Pen. Not affiliated with or endorsed by Epic Pen. All features are free, offline and ad-free."));
        p.Children.Add(Note("Known limitations: exclusive-fullscreen games/apps can draw above overlays (use borderless/windowed mode); capturing a single window in Zoom/Teams/OBS excludes overlay windows (share the whole display instead); overlays cannot appear on the secure desktop (UAC prompts, lock screen)."));
        p.Children.Add(Note("Settings file: " + System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenQuill", "settings.json")));
        return p;
    }

    // ---- hotkey recording ---------------------------------------------------------------------
    private void StartRecording(HotkeyAction a)
    {
        if (_recording == null) _app.Hotkeys?.Suspend();
        _recording = a;
        RefreshHotkeys();
        _keyRows[a].Button.Content = "Press keys…";
        Keyboard.Focus(_keyRows[a].Button);
    }

    private void StopRecording()
    {
        if (_recording == null) return;
        _recording = null;
        _app.Hotkeys?.Resume();
        RefreshHotkeys();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_recording is not HotkeyAction a) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None) { StopRecording(); return; }
        if (key is Key.Back or Key.Delete && Keyboard.Modifiers == ModifierKeys.None) { _keys[a] = Hotkey.None; StopRecording(); return; }
        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0 || VirtualKeys.IsModifierKey(vk)) return; // wait for a non-modifier key
        var mods = HotkeyModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods |= HotkeyModifiers.Control;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= HotkeyModifiers.Alt;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= HotkeyModifiers.Shift;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= HotkeyModifiers.Win;
        _keys[a] = new Hotkey(mods, vk);
        StopRecording();
    }

    private void RefreshHotkeys()
    {
        bool anyError = false;
        foreach (var (a, (btn, status)) in _keyRows)
        {
            var hk = _keys[a];
            btn.Content = _recording == a ? "Press keys…" : hk.ToString();
            var problem = HotkeyRules.Validate(a, hk, _keys);
            string msg = problem switch
            {
                HotkeyProblem.Duplicate => "Already used by " + HotkeyRules.Describe(_keys.First(k => k.Key != a && k.Value == hk).Key),
                HotkeyProblem.Reserved => "Reserved by Windows",
                HotkeyProblem.NoModifier => "Add Ctrl, Alt or Shift (would block typing)",
                _ => "",
            };
            if (problem == HotkeyProblem.None && !hk.IsEmpty && _app.Hotkeys != null && !_app.Hotkeys.IsAvailable(hk))
                msg = "In use by another application";
            if (problem != HotkeyProblem.None) anyError = true;
            status.Text = msg;
            status.Foreground = problem != HotkeyProblem.None ? Brushes.Firebrick : Brushes.DarkOrange;
        }
        _hotkeySummary.Text = anyError ? "Fix the highlighted conflicts before saving." : "";
        _hotkeySummary.Foreground = Brushes.Firebrick;
        _ok.IsEnabled = _apply.IsEnabled = !anyError;
    }

    private bool Commit()
    {
        StopRecording();
        if (HotkeyRules.FindDuplicates(_keys).Any()) { SelectTab("Hotkeys"); return false; }
        if (string.IsNullOrWhiteSpace(_edit.ScreenshotFolder))
        {
            MessageBox.Show(this, "Please choose a screenshot folder.", "ScreenQuill");
            SelectTab("Screenshots");
            return false;
        }
        _edit.SetHotkeys(_keys);
        _app.ApplySettings(_edit);
        RefreshHotkeys();
        return true;
    }
}
