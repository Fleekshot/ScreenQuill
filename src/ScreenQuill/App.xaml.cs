using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace ScreenQuill;

public partial class App : Application
{
    private const string MutexName = @"Local\ScreenQuill.SingleInstance";
    private const string ShowEventName = @"Local\ScreenQuill.ShowToolbar";
    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, MutexName, out bool first);
        if (!first)
        {
            // Second launch: ask the running instance to bring its toolbar back (e.g. from Ghost Mode).
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { /* not ready */ }
            Shutdown();
            return;
        }
        base.OnStartup(e);

        DispatcherUnhandledException += OnUnhandled;
        _controller = new AppController();
        _controller.Start();

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var t = new Thread(() =>
        {
            while (_showEvent.WaitOne())
                Dispatcher.BeginInvoke(() => _controller?.SetGhostMode(false));
        }) { IsBackground = true, Name = "ScreenQuill single-instance listener" };
        t.Start();
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Keep the overlay alive on unexpected errors rather than leaving a frozen topmost window.
        LogError(e.Exception);
        e.Handled = true;
    }

    /// <summary>Local-only error log (never uploaded): %APPDATA%\ScreenQuill\error.log.</summary>
    public static void LogError(Exception ex)
    {
        System.Diagnostics.Debug.WriteLine(ex);
        try
        {
            var log = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenQuill", "error.log");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(log)!);
            System.IO.File.AppendAllText(log, $"[{DateTime.Now:O}] {ex}\n\n");
        }
        catch { /* ignore */ }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.SaveNow();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
