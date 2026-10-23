using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace MetSaver;

/// <summary>
/// Registers MetSaver as the current user's screensaver (HKCU only - no admin rights) and can put the
/// previous one back. Used by the "Set as my screensaver" button, the installer and /install, /uninstall.
/// </summary>
static class Installer
{
    const string Key = @"Control Panel\Desktop";

    public static string? CurrentScreensaver()
    {
        using var k = Registry.CurrentUser.OpenSubKey(Key);
        return k?.GetValue("SCRNSAVE.EXE") as string;
    }

    public static bool IsActive() =>
        string.Equals(CurrentScreensaver(), Paths.InstalledScr, StringComparison.OrdinalIgnoreCase);

    public static void Install(Settings s)
    {
        Paths.Ensure();
        var self = Environment.ProcessPath!;
        if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(Paths.InstalledScr), StringComparison.OrdinalIgnoreCase))
            File.Copy(self, Paths.InstalledScr, overwrite: true);

        var current = CurrentScreensaver();
        if (!string.IsNullOrEmpty(current) && !string.Equals(current, Paths.InstalledScr, StringComparison.OrdinalIgnoreCase))
        {
            s.PreviousScreensaver = current;
            s.Save();
        }

        using (var k = Registry.CurrentUser.CreateSubKey(Key))
        {
            k.SetValue("SCRNSAVE.EXE", Paths.InstalledScr);
            k.SetValue("ScreenSaveActive", "1");
            k.SetValue("ScreenSaveTimeOut", s.IdleSeconds.ToString());
        }
        Apply(s.IdleSeconds, active: true);
        Log.Info($"registered as screensaver ({s.IdleSeconds}s idle)");
    }

    public static void Uninstall(Settings s)
    {
        using (var k = Registry.CurrentUser.CreateSubKey(Key))
        {
            var prev = s.PreviousScreensaver;
            if (!string.IsNullOrEmpty(prev) && File.Exists(prev)) k.SetValue("SCRNSAVE.EXE", prev!);
            else { k.DeleteValue("SCRNSAVE.EXE", throwOnMissingValue: false); k.SetValue("ScreenSaveActive", "0"); }
        }
        Apply(s.IdleSeconds, active: !string.IsNullOrEmpty(s.PreviousScreensaver) && File.Exists(s.PreviousScreensaver));
        Log.Info("screensaver registration removed");
    }

    /// <summary>Ask Windows to pick up the new timeout/active flag immediately rather than at next sign-in.</summary>
    static void Apply(int timeoutSeconds, bool active)
    {
        Native.SystemParametersInfo(Native.SPI_SETSCREENSAVETIMEOUT, (uint)timeoutSeconds, IntPtr.Zero, Native.SPIF_UPDATEINIFILE_SENDCHANGE);
        Native.SystemParametersInfo(Native.SPI_SETSCREENSAVEACTIVE, active ? 1u : 0u, IntPtr.Zero, Native.SPIF_UPDATEINIFILE_SENDCHANGE);
    }
}
