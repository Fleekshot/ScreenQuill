using System;
using System.Collections.Generic;
using System.Windows.Interop;
using ScreenQuill.Core;
using ScreenQuill.Native;

namespace ScreenQuill.Services;

/// <summary>
/// System-wide hotkeys via RegisterHotKey on a message-only window. Bindings that another
/// application already owns are reported in <see cref="Failed"/> instead of silently ignored.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<int, HotkeyAction> _ids = new();
    private Dictionary<HotkeyAction, Hotkey> _bindings = new();
    private int _suspendCount;

    public HotkeyManager()
    {
        var p = new HwndSourceParameters("ScreenQuillHotkeys") { ParentWindow = Win32.HWND_MESSAGE, WindowStyle = 0 };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
    }

    public event Action<HotkeyAction>? Pressed;
    public List<HotkeyAction> Failed { get; } = new();
    public bool IsSuspended => _suspendCount > 0;

    public void Apply(IReadOnlyDictionary<HotkeyAction, Hotkey> bindings)
    {
        _bindings = new Dictionary<HotkeyAction, Hotkey>(bindings);
        if (!IsSuspended) RegisterAll(); else UnregisterAll();
    }

    /// <summary>Suspends all hotkeys (text entry, hotkey recording). Calls nest.</summary>
    public void Suspend()
    {
        if (_suspendCount++ == 0) UnregisterAll();
    }

    public void Resume()
    {
        if (_suspendCount == 0) return;
        if (--_suspendCount == 0) RegisterAll();
    }

    /// <summary>True if the chord can currently be registered (i.e. no other app owns it).</summary>
    public bool IsAvailable(Hotkey h, HotkeyAction? ownedBy = null)
    {
        if (h.IsEmpty) return true;
        foreach (var kv in _ids)
            if (_bindings.TryGetValue(kv.Value, out var mine) && mine == h) return true; // we hold it
        const int probeId = 0xBFFF;
        bool ok = Win32.RegisterHotKey(_source.Handle, probeId, (uint)h.Modifiers | Win32.MOD_NOREPEAT, (uint)h.VirtualKey);
        if (ok) Win32.UnregisterHotKey(_source.Handle, probeId);
        return ok;
    }

    private void RegisterAll()
    {
        UnregisterAll();
        Failed.Clear();
        int id = 1;
        foreach (var (action, hk) in _bindings)
        {
            if (hk.IsEmpty) continue;
            if (Win32.RegisterHotKey(_source.Handle, id, (uint)hk.Modifiers | Win32.MOD_NOREPEAT, (uint)hk.VirtualKey))
                _ids[id++] = action;
            else Failed.Add(action);
        }
    }

    private void UnregisterAll()
    {
        foreach (var id in _ids.Keys) Win32.UnregisterHotKey(_source.Handle, id);
        _ids.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_HOTKEY && _ids.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            Pressed?.Invoke(action);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.Dispose();
    }
}
