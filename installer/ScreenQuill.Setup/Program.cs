using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace ScreenQuill.Setup;

/// <summary>
/// ScreenQuill-Setup.exe            → installer window
/// ScreenQuill-Setup.exe /silent    → install with no UI (exit code 0 = success)
/// uninstall.exe /uninstall [/silent] → used by Settings › Apps
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        bool Has(string flag) => args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
        bool silent = Has("/silent") || Has("/s");

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        if (Has("/uninstall"))
        {
            if (!silent && MessageBox.Show(
                    "Remove ScreenQuill from this computer?\n\nYour settings in %APPDATA%\\ScreenQuill and your screenshots are kept.",
                    "Uninstall ScreenQuill", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return 1;
            try
            {
                Installer.Uninstall();
                if (!silent) MessageBox.Show("ScreenQuill was removed.", "Uninstall ScreenQuill", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            catch (Exception ex)
            {
                if (!silent) MessageBox.Show("Uninstall failed: " + ex.Message, "Uninstall ScreenQuill", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 2;
            }
        }

        if (silent)
        {
            try
            {
                Installer.InstallAsync(new Progress<(string, int)>(_ => { }), desktopShortcut: false, CancellationToken.None)
                    .GetAwaiter().GetResult();
                return 0;
            }
            catch { return 2; }
        }

        Application.Run(new SetupForm());
        return 0;
    }
}
