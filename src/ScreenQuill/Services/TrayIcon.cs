using System;
using System.Windows;
using ScreenQuill.Core;
using WinForms = System.Windows.Forms;

namespace ScreenQuill.Services;

/// <summary>Notification-area icon: restore from Ghost Mode, quick actions, settings, exit.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly AppController _app;
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ToolStripMenuItem _showToolbar, _highlight;
    private readonly System.Drawing.Icon _ico;

    public TrayIcon(AppController app)
    {
        _app = app;
        var res = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/ScreenQuill.ico"));
        _ico = new System.Drawing.Icon(res!.Stream, WinForms.SystemInformation.SmallIconSize);
        var menu = new WinForms.ContextMenuStrip();
        _showToolbar = new WinForms.ToolStripMenuItem("Show toolbar", null, (_, _) => _app.SetGhostMode(!_app.IsGhost)) { CheckOnClick = false };
        menu.Items.Add(_showToolbar);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Cursor (use apps)", null, (_, _) => _app.SelectTool(ToolKind.Cursor));
        menu.Items.Add("Pen", null, (_, _) => _app.SelectTool(ToolKind.Pen));
        menu.Items.Add("Highlighter", null, (_, _) => _app.SelectTool(ToolKind.Highlighter));
        menu.Items.Add("Clear all ink", null, (_, _) => _app.ClearAll());
        _highlight = new WinForms.ToolStripMenuItem("Highlighted cursor", null, (_, _) => _app.ToggleCursorHighlight());
        menu.Items.Add(_highlight);
        menu.Items.Add("Screenshot…", null, (_, _) => _app.Screenshot(false));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Settings…", null, (_, _) => _app.OpenSettings());
        menu.Items.Add("Exit", null, (_, _) => _app.Exit());
        _icon = new WinForms.NotifyIcon
        {
            Icon = _ico,
            Text = "ScreenQuill",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == WinForms.MouseButtons.Left) _app.SetGhostMode(!_app.IsGhost); };
        _icon.BalloonTipClicked += (_, _) => { if (_app.LastScreenshotPath != null) _app.OpenScreenshotFolder(); };
        Refresh();
    }

    public void Refresh()
    {
        _showToolbar.Checked = !_app.IsGhost;
        _highlight.Checked = _app.Tools.CursorHighlight;
        _icon.Text = _app.IsGhost ? "ScreenQuill (Ghost Mode – click to show toolbar)" : "ScreenQuill";
    }

    public void SetVisible(bool visible) => _icon.Visible = visible;

    public void Notify(string title, string text)
    {
        if (!_icon.Visible) return;
        _icon.ShowBalloonTip(2500, title, text, WinForms.ToolTipIcon.None);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _ico.Dispose();
    }
}
