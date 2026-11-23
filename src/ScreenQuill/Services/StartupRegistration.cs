using System;
using Microsoft.Win32;

namespace ScreenQuill.Services;

/// <summary>Per-user "launch at sign-in" via HKCU\...\Run (no elevation). Only changed when the user ticks the option.</summary>
public static class StartupRegistration
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "ScreenQuill";

    public static void Set(bool enabled)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(Key, writable: true) ?? Registry.CurrentUser.CreateSubKey(Key);
            if (enabled) k.SetValue(Name, $"\"{Environment.ProcessPath}\"");
            else k.DeleteValue(Name, throwOnMissingValue: false);
        }
        catch (Exception) { /* policy-restricted machines: setting simply has no effect */ }
    }

    public static bool IsSet()
    {
        using var k = Registry.CurrentUser.OpenSubKey(Key);
        return k?.GetValue(Name) != null;
    }
}
