using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace MetSaver;

/// <summary>Draws a still painting inside the small monitor in Windows' screensaver dialog (/p hwnd).</summary>
static class PreviewHost
{
    public static void Run(IntPtr parent)
    {
        Native.GetClientRect(parent, out var rc);
        int pw = Math.Max(rc.Right, 1), ph = Math.Max(rc.Bottom, 1);
        var p = new HwndSourceParameters("MetSaverPreview")
        {
            ParentWindow = parent,
            WindowStyle = 0x40000000 | 0x10000000,  // WS_CHILD | WS_VISIBLE
            PositionX = 0, PositionY = 0, Width = pw, Height = ph,
        };
        var source = new HwndSource(p);
        double dpi = source.CompositionTarget?.TransformToDevice.M11 ?? 1.0;

        var s = Settings.Load();
        s.ShowCaption = false; s.Motion = "off";
        var art = Cache.Pick();
        if (art is not null)
            source.RootVisual = new ArtLayer(ImagePrep.Prepare(art, s, pw, ph), s, pw / dpi, ph / dpi);

        var watch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        watch.Tick += (_, _) => { if (!Native.IsWindow(parent)) Application.Current.Shutdown(); };
        watch.Start();
    }
}
