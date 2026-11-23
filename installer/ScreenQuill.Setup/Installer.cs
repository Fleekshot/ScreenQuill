using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace ScreenQuill.Setup;

/// <summary>Per-user install of the latest GitHub release: download, verify, copy, shortcuts, Apps entry.</summary>
internal static class Installer
{
    public const string Repo = "Fleekshot/ScreenQuill";
    public const string AssetName = "ScreenQuill-win-x64.zip";
    public static readonly string ZipUrl = $"https://github.com/{Repo}/releases/latest/download/{AssetName}";
    public static readonly string SumUrl = ZipUrl + ".sha256";

    public static readonly string InstallDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "ScreenQuill");
    public static readonly string ExePath = Path.Combine(InstallDir, "ScreenQuill.exe");
    public static readonly string UninstallerPath = Path.Combine(InstallDir, "uninstall.exe");
    public static readonly string StartMenuShortcut = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), "ScreenQuill.lnk");
    public static readonly string DesktopShortcut = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "ScreenQuill.lnk");
    public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ScreenQuill";
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Version of the current install, or null when ScreenQuill is not installed.</summary>
    public static string? InstalledVersion()
    {
        using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
        return File.Exists(ExePath) ? key?.GetValue("DisplayVersion") as string ?? "unknown" : null;
    }

    /// <summary>Downloads and installs. Cancellation is honoured only during the download, so a
    /// cancelled install never leaves a half-copied program folder.</summary>
    public static async Task<string> InstallAsync(IProgress<(string Status, int Percent)> progress, bool desktopShortcut, CancellationToken ct)
    {
        string tmp = Path.Combine(Path.GetTempPath(), "ScreenQuill-setup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ScreenQuill-Setup/1.0");

            progress.Report(("Contacting GitHub…", 0));
            string sumText = await http.GetStringAsync(SumUrl).ConfigureAwait(false);
            string expected = sumText.Trim().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];

            string zip = Path.Combine(tmp, AssetName);
            using (var resp = await http.GetAsync(ZipUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                long? total = resp.Content.Headers.ContentLength;
                using var src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var dst = File.Create(zip);
                var buffer = new byte[81920];
                long done = 0;
                int read, lastPct = -1;
                while ((read = await src.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
                {
                    await dst.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                    done += read;
                    int pct = total > 0 ? (int)(done * 85 / total.Value) : 0;
                    if (pct != lastPct)
                    {
                        lastPct = pct;
                        string size = total > 0 ? $"{done / 1048576.0:0.0} of {total.Value / 1048576.0:0.0} MB" : $"{done / 1048576.0:0.0} MB";
                        progress.Report(($"Downloading… {size}", pct));
                    }
                }
            }

            progress.Report(("Verifying download…", 87));
            string actual;
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(zip))
                actual = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "");
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The download is damaged (checksum mismatch). Please try again.");

            progress.Report(("Extracting…", 90));
            string files = Path.Combine(tmp, "files");
            ZipFile.ExtractToDirectory(zip, files);
            if (!File.Exists(Path.Combine(files, "ScreenQuill.exe")))
                throw new InvalidDataException("The release does not contain ScreenQuill.exe.");

            progress.Report(("Closing ScreenQuill if it is running…", 92));
            StopRunningApp();

            progress.Report(("Installing…", 94));
            ClearDirectory(InstallDir, keep: null);
            Directory.CreateDirectory(InstallDir);
            CopyTree(files, InstallDir);
            string self = Assembly.GetExecutingAssembly().Location;
            if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(UninstallerPath), StringComparison.OrdinalIgnoreCase))
                File.Copy(self, UninstallerPath, true);

            progress.Report(("Creating shortcuts…", 97));
            CreateShortcut(StartMenuShortcut);
            if (desktopShortcut) CreateShortcut(DesktopShortcut);
            else TryDelete(DesktopShortcut);

            string version = (FileVersionInfo.GetVersionInfo(ExePath).ProductVersion ?? "1.0.0").Split('+')[0];
            RegisterUninstall(version);
            progress.Report(($"ScreenQuill {version} was installed.", 100));
            return version;
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { /* temp cleanup is best effort */ }
        }
    }

    public static void Launch() =>
        Process.Start(new ProcessStartInfo(ExePath) { UseShellExecute = true, WorkingDirectory = InstallDir });

    public static void StopRunningApp()
    {
        foreach (var p in Process.GetProcessesByName("ScreenQuill"))
        {
            try { p.Kill(); p.WaitForExit(5000); } catch { /* already gone */ }
            finally { p.Dispose(); }
        }
    }

    /// <summary>Removes shortcuts, the startup entry and the Apps entry; then the program folder.</summary>
    public static void Uninstall()
    {
        StopRunningApp();
        TryDelete(StartMenuShortcut);
        TryDelete(DesktopShortcut);
        using (var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)) run?.DeleteValue("ScreenQuill", false);
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);

        string self = Path.GetFullPath(Assembly.GetExecutingAssembly().Location);
        bool runningFromInstallDir = self.StartsWith(Path.GetFullPath(InstallDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        if (!runningFromInstallDir) { ClearDirectory(InstallDir, keep: null); return; }

        // A running exe cannot delete itself: remove everything else now, then let a hidden cmd
        // remove the folder a moment after this process exits.
        ClearDirectory(InstallDir, keep: self);
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"{InstallDir}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetTempPath(),
        });
    }

    private static void RegisterUninstall(string version)
    {
        long bytes = new DirectoryInfo(InstallDir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
        key.SetValue("DisplayName", "ScreenQuill");
        key.SetValue("DisplayVersion", version);
        key.SetValue("Publisher", "ScreenQuill");
        key.SetValue("DisplayIcon", ExePath);
        key.SetValue("InstallLocation", InstallDir);
        key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
        key.SetValue("URLInfoAbout", $"https://github.com/{Repo}");
        key.SetValue("UninstallString", $"\"{UninstallerPath}\" /uninstall");
        key.SetValue("QuietUninstallString", $"\"{UninstallerPath}\" /uninstall /silent");
        key.SetValue("EstimatedSize", (int)(bytes / 1024), RegistryValueKind.DWord);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    private static void CreateShortcut(string lnkPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(lnkPath)!);
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell is unavailable.");
        object shell = Activator.CreateInstance(shellType);
        object? lnk = null;
        try
        {
            lnk = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
            var t = lnk.GetType();
            t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { ExePath });
            t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { InstallDir });
            t.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { ExePath + ",0" });
            t.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { "Draw on your screen" });
            t.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
        }
        finally
        {
            if (lnk != null) Marshal.FinalReleaseComObject(lnk);
            Marshal.FinalReleaseComObject(shell);
        }
    }

    /// <summary>Deletes the folder's contents (retrying briefly while files are released), optionally keeping one file.</summary>
    private static void ClearDirectory(string dir, string? keep)
    {
        if (!Directory.Exists(dir)) return;
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    if (keep != null && string.Equals(Path.GetFullPath(f), keep, StringComparison.OrdinalIgnoreCase)) continue;
                    File.SetAttributes(f, FileAttributes.Normal);
                    File.Delete(f);
                }
                foreach (var d in Directory.GetDirectories(dir).OrderByDescending(d => d.Length))
                    Directory.Delete(d, true);
                if (keep == null) Directory.Delete(dir, true);
                return;
            }
            catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && attempt < 10)
            {
                Thread.Sleep(300);
            }
        }
    }

    private static void CopyTree(string from, string to)
    {
        foreach (var d in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, d.Substring(from.Length + 1)));
        foreach (var f in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(f, Path.Combine(to, f.Substring(from.Length + 1)), true);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }
}
