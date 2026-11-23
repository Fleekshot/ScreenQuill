using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using ScreenQuill.Core;
using ScreenQuill.Core.Model;
using ScreenQuill.Native;
using ScreenQuill.Overlay;
using ScreenQuill.Services;
using ScreenQuill.Settings;
using ScreenQuill.Toolbar;

namespace ScreenQuill;

/// <summary>
/// Composition root and command surface. Owns the document, tool state, overlays (one per monitor),
/// toolbar, global hotkeys, cursor halo, tray icon and persistence. All toolbar buttons, hotkeys and
/// tray items funnel through the public methods here, so every entry point behaves identically.
/// </summary>
public sealed class AppController : IInkHost, IDisposable
{
    private readonly SettingsStore _store;
    private readonly List<OverlayWindow> _overlays = new();
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _zOrderTimer;
    private ToolbarWindow? _toolbar;
    private HotkeyManager? _hotkeys;
    private CursorHalo? _halo;
    private TrayIcon? _tray;
    private SettingsWindow? _settingsWindow;
    private string _monitorSignature = "";
    private bool _fadeHooked, _capturing, _ghost, _exiting;
    private FadeMode _lastFade = FadeMode.Medium;
    private BoardMode _lastBoard = BoardMode.Whiteboard;

    public AppController(SettingsStore? store = null)
    {
        _store = store ?? new SettingsStore();
        Settings = _store.Load();
        Tools = new ToolState(Settings);
        if (Tools.Fade != FadeMode.Off) _lastFade = Tools.Fade;
        Doc = new AnnotationDocument();
        Input = new InkInteraction(this);
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveNow(); };
        _zOrderTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _zOrderTimer.Tick += (_, _) => ReassertZOrder();
    }

    public AnnotationDocument Doc { get; }
    public ToolState Tools { get; }
    public AppSettings Settings { get; }
    public InkInteraction Input { get; }
    public IReadOnlyList<OverlayWindow> Overlays => _overlays;
    public ToolbarWindow? Toolbar => _toolbar;
    public HotkeyManager? Hotkeys => _hotkeys;
    public bool IsGhost => _ghost;

    // ---- lifecycle ------------------------------------------------------------------------------
    public void Start()
    {
        BuildOverlays();
        _toolbar = new ToolbarWindow(this);
        _toolbar.Show();
        PlaceToolbar();
        _toolbar.BringToFront();

        _hotkeys = new HotkeyManager();
        _hotkeys.Pressed += OnHotkey;
        _hotkeys.Apply(Settings.GetHotkeys());

        _halo = new CursorHalo();
        _halo.MouseDown += (x, y) => _toolbar?.Dispatcher.BeginInvoke(() =>
        {
            if (_toolbar != null && _toolbar.HasFlyout && !_toolbar.FlyoutContains(x, y)) _toolbar.CloseFlyout();
        });

        _tray = new TrayIcon(this);
        _tray.SetVisible(Settings.ShowTrayIcon);

        Tools.Changed += (_, _) => ApplyState();
        Doc.Changed += OnDocChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        SystemEvents.SessionEnding += (_, _) => SaveNow();
        ApplyState();
        _zOrderTimer.Start();

        if (_hotkeys.Failed.Count > 0)
            _tray.Notify("Some hotkeys are in use by another app",
                string.Join(", ", _hotkeys.Failed.Select(HotkeyRules.Describe)) + ". Change them in Settings → Hotkeys.");
        if (Settings.StartInGhostMode) SetGhostMode(true);
    }

    private void BuildOverlays()
    {
        foreach (var o in _overlays) o.Close();
        _overlays.Clear();
        Input.CancelAll();
        var monitors = Monitors.GetAll();
        _monitorSignature = Monitors.Signature(monitors);
        foreach (var m in monitors)
        {
            var o = new OverlayWindow(m, Input);
            o.Surface.FadeOpacity = Doc.FadeOpacity;
            o.Surface.HighlighterOpacity = Settings.HighlighterOpacity;
            o.Surface.Smooth = Settings.SmoothInk;
            o.Show();
            _overlays.Add(o);
            o.Surface.Sync(Doc.Items);
        }
    }

    private void OnDisplayChanged(object? sender, EventArgs e)
    {
        // Raised on a system thread; marshal and let the new layout settle.
        Application.Current?.Dispatcher.BeginInvoke(async () =>
        {
            await Task.Delay(400);
            var monitors = Monitors.GetAll();
            if (Monitors.Signature(monitors) == _monitorSignature) return;
            BuildOverlays();
            ApplyState();
            _toolbar?.ClampToScreen();
            _toolbar?.BringToFront();
        });
    }

    private void PlaceToolbar()
    {
        if (_toolbar == null) return;
        var r = _toolbar.PixelRect();
        int x, y;
        var monitors = Monitors.GetAll();
        if (Settings.ToolbarX is double sx && Settings.ToolbarY is double sy)
        {
            x = (int)sx; y = (int)sy;
        }
        else
        {
            // Default: right edge of the primary work area, near the top (as in the reference screenshot).
            var w = (monitors.FirstOrDefault(m => m.Primary) ?? monitors[0]).WorkArea;
            x = w.Right - r.Width - (int)(40 * Win32.DpiScaleAt(w.X + 1, w.Y + 1));
            y = w.Y + (int)(20 * Win32.DpiScaleAt(w.X + 1, w.Y + 1));
        }
        _toolbar.MoveTo(x, y);
        _toolbar.ClampToScreen();
    }

    public void SaveToolbarPosition()
    {
        if (_toolbar == null) return;
        var r = _toolbar.PixelRect();
        Settings.ToolbarX = r.Left; Settings.ToolbarY = r.Top;
        ScheduleSave();
    }

    public void Exit()
    {
        if (_exiting) return;
        _exiting = true;
        Input.CommitText();
        SaveToolbarPosition();
        SaveNow();
        Dispose();
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        _zOrderTimer.Stop();
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        _hotkeys?.Dispose(); _hotkeys = null;
        _halo?.Dispose(); _halo = null;
        _tray?.Dispose(); _tray = null;
        _toolbar?.CloseFlyout();
        _settingsWindow?.Close();
        _toolbar?.Close();
        foreach (var o in _overlays.ToList()) o.Close();
        _overlays.Clear();
    }

    // ---- state propagation ------------------------------------------------------------------------
    private void ApplyState()
    {
        bool interactive = Tools.IsDrawingMode && !_capturing;
        foreach (var o in _overlays)
        {
            o.SetInteractive(interactive);
            bool boardHere = Settings.BoardOnAllMonitors || IsToolbarMonitor(o.Monitor);
            o.SetBoard(boardHere ? Tools.Board : BoardMode.None);
            o.SetInkVisible(Tools.InkVisible && !(Settings.HideInkWhenCursor && !Tools.IsDrawingMode));
            o.Cursor = InkInteraction.CursorFor(Tools.Tool);
        }
        Input.OnToolChanged();
        Input.RefreshEditorStyle();
        _toolbar?.Refresh();
        _halo?.Configure(Tools.CursorHighlight, Settings.CursorHighlightColor, Settings.CursorHighlightRadius,
            Settings.CursorHighlightOpacity, Settings.CursorHighlightRing);
        _tray?.Refresh();
        Tools.WriteTo(Settings);
        ScheduleSave();
    }

    private bool IsToolbarMonitor(MonitorInfo m)
    {
        if (_toolbar == null) return m.Primary;
        var r = _toolbar.PixelRect();
        return m.Bounds.Contains(r.Left + r.Width / 2, r.Top + 30);
    }

    private void OnDocChanged(object? sender, DocumentChangedEventArgs e)
    {
        foreach (var o in _overlays) o.Surface.Sync(Doc.Items);
        _toolbar?.Refresh();
        if (Doc.HasFadingItems && !_fadeHooked)
        {
            _fadeHooked = true;
            CompositionTarget.Rendering += OnFadeFrame;
        }
    }

    private void OnFadeFrame(object? sender, EventArgs e)
    {
        Doc.RemoveExpired();
        foreach (var o in _overlays) o.Surface.UpdateOpacities(Doc.Items);
        if (!Doc.HasFadingItems)
        {
            CompositionTarget.Rendering -= OnFadeFrame;
            _fadeHooked = false;
        }
    }

    private void ReassertZOrder()
    {
        if (_capturing || _toolbar == null) return;
        // Other topmost windows (e.g. pinned apps) can rise above us; keep ink then toolbar on top.
        if (Tools.IsDrawingMode && !Input.IsBusy && !Input.IsEditingText)
            foreach (var o in _overlays) if (o.Handle != IntPtr.Zero) Win32.BringToTopmost(o.Handle);
        if (!_ghost) _toolbar.BringToFront();
    }

    public void ScheduleSave() { _saveTimer.Stop(); _saveTimer.Start(); }

    public void SaveNow()
    {
        try
        {
            Tools.WriteTo(Settings);
            _store.Save(Settings);
        }
        catch (Exception ex) { Debug.WriteLine("Settings save failed: " + ex.Message); }
    }

    // ---- IInkHost ------------------------------------------------------------------------------
    public FadeSpec? NewFadeSpec() =>
        Tools.Fade == FadeMode.Off ? null : new FadeSpec(Settings.FadeSeconds(Tools.Fade), Settings.FadeOutSeconds);

    public void OnPointerActivity() => _toolbar?.CloseFlyout();

    public void OnTextEditing(bool editing)
    {
        // Global hotkeys must never fire while the user is typing a label.
        if (editing) _hotkeys?.Suspend(); else _hotkeys?.Resume();
    }

    // ---- commands ------------------------------------------------------------------------------
    public void SelectTool(ToolKind t)
    {
        Input.CancelAll();
        Tools.Tool = t;
        _toolbar?.BringToFront();
    }

    public void ToggleInkVisible()
    {
        if (Tools.InkVisible)
        {
            Input.CommitText();
            Tools.Tool = ToolKind.Cursor; // nothing to draw on while ink is hidden
            Tools.InkVisible = false;
        }
        else Tools.InkVisible = true;
    }

    public void SetColor(uint c)
    {
        Tools.Color = c | 0xFF000000;
        ScheduleSave();
    }

    public void AssignQuickColor(int index)
    {
        var list = Settings.RecentColors.ToList();
        while (list.Count < 4) list.Add(Palette.DefaultRecent[list.Count]);
        list[index] = Tools.Color;
        Settings.RecentColors = list;
        _toolbar?.Refresh();
        ScheduleSave();
    }

    public void NextQuickColor()
    {
        var all = Palette.Quick;
        int i = Array.IndexOf(all, Tools.Color | 0xFF000000);
        SetColor(all[(i + 1) % all.Length]);
    }

    public void SetSize(ToolKind tool, double size) => Tools.SetSize(tool, size);

    public void SetFade(FadeMode m)
    {
        if (m != FadeMode.Off) _lastFade = m;
        Tools.Fade = m;
    }

    public void ToggleFade() => SetFade(Tools.Fade == FadeMode.Off ? _lastFade : FadeMode.Off);

    public void SetBoard(BoardMode b)
    {
        if (b != BoardMode.None) _lastBoard = b;
        Tools.Board = b;
        if (b != BoardMode.None && Tools.Tool == ToolKind.Cursor) Tools.Tool = Tools.LastDrawingTool;
    }

    public void ToggleBoard() => SetBoard(Tools.Board == BoardMode.None ? _lastBoard : BoardMode.None);

    public void ToggleCursorHighlight() => Tools.CursorHighlight = !Tools.CursorHighlight;

    public void Undo() { Input.CancelAll(); Doc.Undo(); }
    public void Redo() { Input.CancelAll(); Doc.Redo(); }

    public void ClearAll()
    {
        Input.CancelText();
        Input.CancelAll();
        Doc.Clear();
    }

    public void SetGhostMode(bool ghost)
    {
        if (_toolbar == null) return;
        _ghost = ghost;
        _toolbar.CloseFlyout();
        if (ghost) _toolbar.Hide();
        else
        {
            _toolbar.Show();
            _toolbar.ClampToScreen();
            _toolbar.BringToFront();
        }
        _tray?.Refresh();
        if (ghost && _hotkeys != null && Settings.GetHotkeys()[HotkeyAction.GhostMode] is var hk && !hk.IsEmpty)
            _tray?.Notify("Ghost Mode", $"Toolbar hidden. Press {hk} (or use the tray icon) to bring it back.");
    }

    public string HotkeyText(HotkeyAction a)
    {
        var hk = Settings.GetHotkeys()[a];
        return hk.IsEmpty ? "" : hk.ToString();
    }

    private void OnHotkey(HotkeyAction a)
    {
        switch (a)
        {
            case HotkeyAction.Cursor: SelectTool(ToolKind.Cursor); break;
            case HotkeyAction.Pen: ToggleTool(ToolKind.Pen); break;
            case HotkeyAction.Highlighter: ToggleTool(ToolKind.Highlighter); break;
            case HotkeyAction.Eraser: ToggleTool(ToolKind.Eraser); break;
            case HotkeyAction.Text: ToggleTool(ToolKind.Text); break;
            case HotkeyAction.Line: ToggleTool(ToolKind.Line); break;
            case HotkeyAction.Arrow: ToggleTool(ToolKind.Arrow); break;
            case HotkeyAction.Rectangle: ToggleTool(ToolKind.Rectangle); break;
            case HotkeyAction.Ellipse: ToggleTool(ToolKind.Ellipse); break;
            case HotkeyAction.Screenshot: Screenshot(false); break;
            case HotkeyAction.ScreenshotToClipboard: Screenshot(true); break;
            case HotkeyAction.Clear: ClearAll(); break;
            case HotkeyAction.Undo: Undo(); break;
            case HotkeyAction.Redo: Redo(); break;
            case HotkeyAction.Whiteboard: SetBoard(Tools.Board == BoardMode.Whiteboard ? BoardMode.None : BoardMode.Whiteboard); break;
            case HotkeyAction.Blackboard: SetBoard(Tools.Board == BoardMode.Blackboard ? BoardMode.None : BoardMode.Blackboard); break;
            case HotkeyAction.CycleFadingInk:
                var m = Tools.CycleFade();
                if (m != FadeMode.Off) _lastFade = m;
                _tray?.Notify("Fading ink", m == FadeMode.Off ? "Off" : $"{m} ({Settings.FadeSeconds(m):0.#} s)");
                break;
            case HotkeyAction.CursorHighlight: ToggleCursorHighlight(); break;
            case HotkeyAction.ToggleInkVisibility: ToggleInkVisible(); break;
            case HotkeyAction.GhostMode: SetGhostMode(!_ghost); break;
            case HotkeyAction.IncreaseSize: Tools.StepSize(+1); break;
            case HotkeyAction.DecreaseSize: Tools.StepSize(-1); break;
            case HotkeyAction.NextColor: NextQuickColor(); break;
            case HotkeyAction.OpenSettings: OpenSettings(); break;
        }
    }

    /// <summary>Hotkey for the active tool returns to Cursor, so one key toggles drawing on and off.</summary>
    private void ToggleTool(ToolKind t) => SelectTool(Tools.Tool == t ? ToolKind.Cursor : t);

    public void OnFlyoutChanged(bool open) => _halo?.SetWantClicks(open);

    // ---- screenshots ---------------------------------------------------------------------------
    public async void Screenshot(bool clipboardOnly)
    {
        if (_capturing) return;
        _capturing = true;
        bool toolbarWasVisible = _toolbar?.IsVisible == true;
        try
        {
            Input.CommitText();
            Input.CancelAll();
            _toolbar?.CloseFlyout();
            _toolbar?.Hide();
            _halo?.SetHiddenForCapture(true);
            _hotkeys?.Suspend();
            await Task.Delay(180); // let DWM present the frame without the toolbar
            var (image, bounds) = ScreenCapture.CaptureVirtualScreen();
            var sel = await ScreenCapture.SelectRegionAsync(image, bounds, Monitors.GetAll());
            if (sel == null) return; // cancelled
            var clamp = ScreenshotNaming.ClampSelection(sel.Value, new RectD(bounds.X, bounds.Y, bounds.Width, bounds.Height));
            if (clamp == null) return;
            var crop = ScreenCapture.Crop(image, bounds, clamp.Value);
            if (clipboardOnly)
            {
                bool ok = ScreenCapture.CopyToClipboard(crop);
                _tray?.Notify("Screenshot", ok ? "Copied to the clipboard." : "Could not open the clipboard.");
            }
            else
            {
                var path = ScreenshotNaming.NextPath(Settings.ScreenshotFolder, DateTime.Now);
                ScreenCapture.SavePng(crop, path);
                bool copied = Settings.ScreenshotAlsoCopyToClipboard && ScreenCapture.CopyToClipboard(crop);
                LastScreenshotPath = path;
                _tray?.Notify("Screenshot saved", path + (copied ? "\n(also copied to the clipboard)" : ""));
                if (Settings.ScreenshotOpenFolderAfterSave) OpenScreenshotFolder();
            }
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            _tray?.Notify("Screenshot failed", ex.Message);
        }
        finally
        {
            _hotkeys?.Resume();
            _halo?.SetHiddenForCapture(false);
            if (toolbarWasVisible && !_ghost) { _toolbar?.Show(); _toolbar?.BringToFront(); }
            _capturing = false;
        }
    }

    public string? LastScreenshotPath { get; private set; }

    public void OpenScreenshotFolder()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Settings.ScreenshotFolder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Settings.ScreenshotFolder}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { _tray?.Notify("Could not open folder", ex.Message); }
    }

    // ---- dialogs -------------------------------------------------------------------------------
    public void OpenSettings(string? tab = null)
    {
        // Dialogs need the mouse: drawing-mode overlays would otherwise sit on top of them.
        if (Tools.IsDrawingMode) SelectTool(ToolKind.Cursor);
        if (_settingsWindow != null)
        {
            _settingsWindow.SelectTab(tab);
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(this);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.SelectTab(tab);
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    /// <summary>Applies an edited copy of settings from the Settings window.</summary>
    public void ApplySettings(AppSettings edited)
    {
        var s = Settings;
        s.HighlighterOpacity = edited.HighlighterOpacity;
        s.UsePenPressure = edited.UsePenPressure;
        s.SmoothInk = edited.SmoothInk;
        s.FadeShortSeconds = edited.FadeShortSeconds;
        s.FadeMediumSeconds = edited.FadeMediumSeconds;
        s.FadeLongSeconds = edited.FadeLongSeconds;
        s.FadeOutSeconds = edited.FadeOutSeconds;
        s.CursorHighlightColor = edited.CursorHighlightColor;
        s.CursorHighlightRadius = edited.CursorHighlightRadius;
        s.CursorHighlightOpacity = edited.CursorHighlightOpacity;
        s.CursorHighlightRing = edited.CursorHighlightRing;
        s.ScreenshotFolder = edited.ScreenshotFolder;
        s.ScreenshotAlsoCopyToClipboard = edited.ScreenshotAlsoCopyToClipboard;
        s.ScreenshotOpenFolderAfterSave = edited.ScreenshotOpenFolderAfterSave;
        s.StartInGhostMode = edited.StartInGhostMode;
        s.ShowTooltips = edited.ShowTooltips;
        s.HideInkWhenCursor = edited.HideInkWhenCursor;
        s.BoardOnAllMonitors = edited.BoardOnAllMonitors;
        s.ShowTrayIcon = edited.ShowTrayIcon;
        s.Hotkeys = new Dictionary<HotkeyAction, string>(edited.Hotkeys);
        if (s.LaunchAtStartup != edited.LaunchAtStartup)
        {
            s.LaunchAtStartup = edited.LaunchAtStartup;
            StartupRegistration.Set(edited.LaunchAtStartup);
        }
        s.Sanitize();
        foreach (var o in _overlays)
        {
            o.Surface.HighlighterOpacity = s.HighlighterOpacity;
            o.Surface.Smooth = s.SmoothInk;
            o.Surface.Invalidate(Doc.Items);
        }
        _hotkeys?.Apply(s.GetHotkeys());
        _tray?.SetVisible(s.ShowTrayIcon);
        if (!s.ShowTrayIcon && _ghost) SetGhostMode(false); // never strand the user without a way back
        ApplyState();
        SaveNow();
    }

    public void PickCustomColor()
    {
        ToolKind previous = Tools.Tool;
        if (Tools.IsDrawingMode) SelectTool(ToolKind.Cursor);
        var dlg = new ColorPickerWindow(Tools.Color);
        if (dlg.ShowDialog() == true)
        {
            uint c = dlg.SelectedColor;
            SetColor(c);
            var list = Settings.CustomColors.Where(x => x != (c | 0xFF000000)).ToList();
            list.Insert(0, c | 0xFF000000);
            Settings.CustomColors = list.Take(6).ToList();
            ScheduleSave();
            _toolbar?.Refresh();
        }
        if (previous != ToolKind.Cursor) SelectTool(previous); // back to drawing with the new colour
    }
}
