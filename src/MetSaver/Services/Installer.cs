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
        var srcDir = Path.GetDirectoryName(Path.GetFullPath(self))!;
        var destDir = Path.GetDirectoryName(Paths.InstalledScr)!;
        Directory.CreateDirectory(destDir);
        if (!string.Equals(srcDir, destDir, StringComparison.OrdinalIgnoreCase))
        {
            // A framework-dependent build keeps its code in MetSaver.dll (plus config files) beside the exe, so the whole
            // folder has to come along. A single-file build is just the exe. Only copy a folder that is clearly our build output.
            if (File.Exists(Path.Combine(srcDir, "MetSaver.dll")))
                foreach (var file in Directory.GetFiles(srcDir))
                    if (!file.EndsWith(".scr", StringComparison.OrdinalIgnoreCase))
                        File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
            File.Copy(self, Paths.InstalledScr, overwrite: true);
        }
        // Older versions installed a lone launcher straight into the data folder; remove that leftover.
        var legacy = Path.Combine(Paths.Home, "MetSaver.scr");
        if (File.Exists(legacy)) { try { File.Delete(legacy); } catch { } }

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
