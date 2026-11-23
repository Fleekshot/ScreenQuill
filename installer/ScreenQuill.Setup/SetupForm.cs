using System;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace ScreenQuill.Setup;

internal sealed class SetupForm : Form
{
    private readonly Label _intro, _status;
    private readonly CheckBox _desktop, _launch;
    private readonly ProgressBar _bar;
    private readonly Button _primary, _cancel;
    private CancellationTokenSource? _cts;
    private bool _busy, _finished, _closeWhenIdle;

    public SetupForm()
    {
        Text = "ScreenQuill Setup";
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        ClientSize = new Size(500, 330);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = SystemColors.Window;

        Icon? icon = null;
        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("ScreenQuill.ico"))
            if (s != null) icon = new Icon(s);
        if (icon != null) Icon = icon;

        var header = new Panel { Dock = DockStyle.Top, Height = 78, BackColor = Color.White };
        var logo = new PictureBox
        {
            Location = new Point(20, 15), Size = new Size(48, 48), SizeMode = PictureBoxSizeMode.Zoom,
            Image = icon != null ? new Icon(icon, 48, 48).ToBitmap() : null,
        };
        var title = new Label { Text = "ScreenQuill", Font = new Font("Segoe UI Semibold", 15f), Location = new Point(80, 14), AutoSize = true };
        var subtitle = new Label { Text = "Draw on your screen, over any app", ForeColor = Color.DimGray, Location = new Point(82, 46), AutoSize = true };
        header.Controls.AddRange(new Control[] { logo, title, subtitle });
        var rule = new Label { Dock = DockStyle.Top, Height = 1, BackColor = Color.Gainsboro };

        string? installed = Installer.InstalledVersion();
        _intro = new Label
        {
            Location = new Point(20, 96), Size = new Size(460, 52),
            Text = (installed != null
                    ? $"ScreenQuill {installed} is already installed. Setup will replace it with the latest version from GitHub."
                    : "Setup will download the latest version of ScreenQuill from GitHub and install it for your Windows account.")
                + " No administrator rights are needed.",
        };
        var where = new Label
        {
            Location = new Point(20, 150), Size = new Size(460, 18), ForeColor = Color.DimGray, AutoEllipsis = true,
            Text = "Install folder: " + Installer.InstallDir,
        };
        _desktop = new CheckBox { Text = "Create a desktop shortcut", Location = new Point(20, 178), AutoSize = true };
        _launch = new CheckBox { Text = "Start ScreenQuill when setup finishes", Location = new Point(20, 202), AutoSize = true, Checked = true };
        _bar = new ProgressBar { Location = new Point(20, 236), Size = new Size(460, 18) };
        _status = new Label { Location = new Point(20, 258), Size = new Size(460, 18), ForeColor = Color.DimGray, AutoEllipsis = true };

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 48, BackColor = SystemColors.Control };
        _primary = new Button { Text = installed != null ? "Update" : "Install", Size = new Size(88, 28), Location = new Point(302, 10) };
        _cancel = new Button { Text = "Cancel", Size = new Size(88, 28), Location = new Point(398, 10) };
        footer.Controls.AddRange(new Control[] { _primary, _cancel });

        Controls.AddRange(new Control[] { _intro, where, _desktop, _launch, _bar, _status, footer, rule, header });
        AcceptButton = _primary;
        _primary.Click += OnPrimary;
        _cancel.Click += (_, _) => Close();
    }

    private async void OnPrimary(object? sender, EventArgs e)
    {
        if (_finished)
        {
            if (_launch.Checked) TryLaunch();
            Close();
            return;
        }

        _busy = true;
        _cts = new CancellationTokenSource();
        _primary.Enabled = _desktop.Enabled = _launch.Enabled = false;
        var progress = new Progress<(string Status, int Percent)>(p => { _status.Text = p.Status; _bar.Value = p.Percent; });
        try
        {
            await Installer.InstallAsync(progress, _desktop.Checked, _cts.Token);
            _finished = true;
            _primary.Text = "Finish";
            _primary.Enabled = _launch.Enabled = true;
            _cancel.Visible = false;
            _intro.Text = "ScreenQuill is installed. You can start it any time from the Start menu, and remove it from Settings › Apps.";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Setup was cancelled. Nothing was changed.";
            _bar.Value = 0;
            ResetButtons();
        }
        catch (Exception ex)
        {
            _status.Text = "Setup failed.";
            _bar.Value = 0;
            ResetButtons();
            if (!_closeWhenIdle)
                MessageBox.Show(this, "ScreenQuill could not be installed:\n\n" + Describe(ex), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _busy = false;
            _cts.Dispose();
            _cts = null;
        }
        if (_closeWhenIdle) Close();
    }

    private void ResetButtons()
    {
        _primary.Text = "Retry";
        _primary.Enabled = _desktop.Enabled = _launch.Enabled = true;
    }

    private void TryLaunch()
    {
        try { Installer.Launch(); }
        catch (Exception ex) { MessageBox.Show(this, "ScreenQuill could not be started: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private static string Describe(Exception ex)
    {
        var inner = ex;
        while (inner.InnerException != null) inner = inner.InnerException;
        return inner is System.Net.WebException || inner is System.Net.Sockets.SocketException || ex is System.Net.Http.HttpRequestException
            ? "Could not download from GitHub. Check your internet connection and try again.\n\n(" + inner.Message + ")"
            : inner.Message;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_busy)
        {
            // Downloads stop at once; the copy step is short and runs to completion.
            e.Cancel = true;
            _closeWhenIdle = true;
            _cts?.Cancel();
            _status.Text = "Cancelling…";
        }
        base.OnFormClosing(e);
    }
}
