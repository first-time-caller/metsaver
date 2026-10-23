using System.Runtime.InteropServices;

namespace MetSaver;

/// <summary>Win32 calls: monitor enumeration (in physical pixels), window placement, cursor and system settings.</summary>
static class Native
{
    public readonly record struct Monitor(int X, int Y, int Width, int Height);

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

    delegate bool MonitorEnumProc(IntPtr monitor, IntPtr dc, ref RECT rect, IntPtr data);

    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    public static extern bool SystemParametersInfo(uint action, uint param, IntPtr pvParam, uint winIni);

    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new(-2);
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SPI_SETSCREENSAVETIMEOUT = 0x000F;
    public const uint SPI_SETSCREENSAVEACTIVE = 0x0011;
    public const uint SPIF_UPDATEINIFILE_SENDCHANGE = 0x0003;

    public static List<Monitor> GetMonitors()
    {
        var list = new List<Monitor>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr m, IntPtr dc, ref RECT r, IntPtr d) =>
        {
            list.Add(new Monitor(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top));
            return true;
        }, IntPtr.Zero);
        if (list.Count == 0) list.Add(new Monitor(0, 0, 2560, 1440));
        return list;
    }
}
