using System.Windows;

namespace MetSaver;

/// <summary>
/// Windows runs a screensaver as:  /s (run)  /c (settings)  /p &lt;hwnd&gt; (preview in the Settings dialog).
/// Extras:  /test (windowed demo)  /install [seconds]  /uninstall  /seed (pre-download paintings).
/// </summary>
static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception e) { Log.Error(e, "fatal"); return 1; }
    }

    static int Run(string[] args)
    {
        var first = args.Length > 0 ? args[0].ToLowerInvariant() : "/c";
        var word = first.TrimStart('/', '-').Split(':')[0];
        Paths.Ensure();
        var settings = Settings.Load();

        switch (word)
        {
            case "install":
                if (args.Length > 1 && int.TryParse(args[1], out var idle)) { settings.IdleSeconds = idle; settings.Normalize(); settings.Save(); }
                Installer.Install(settings);
                return 0;
            case "uninstall":
                Installer.Uninstall(settings);
                return 0;
            case "seed":
                return Seed(settings);
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnLastWindowClose };
        switch (word.Length > 0 ? word[0] : 'c')
        {
            case 's':
                using (var gate = new Mutex(true, @"Local\MetSaver.Screensaver", out var owned))
                {
                    if (!owned) return 0;  // already running
                    app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    app.Startup += (_, _) => new SaverRunner(settings, SaverMode.Saver).Start();
                    return app.Run();
                }
            case 't':
                app.Startup += (_, _) => new SaverRunner(settings, SaverMode.Test).Start();
                return app.Run();
            case 'g':
                app.Startup += (_, _) => new SaverRunner(settings, SaverMode.Gallery).Start();
                return app.Run();
            case 'p':
                var hwnd = ParseHwnd(first, args);
                if (hwnd == IntPtr.Zero) return 0;
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                app.Startup += (_, _) => PreviewHost.Run(hwnd);
                return app.Run();
            default:
                return app.Run(new SettingsWindow());
        }
    }

    static IntPtr ParseHwnd(string first, string[] args)
    {
        var raw = first.Contains(':') ? first.Split(':')[1] : args.Length > 1 ? args[1] : "";
        return long.TryParse(raw, out var h) ? new IntPtr(h) : IntPtr.Zero;
    }

    /// <summary>Pre-download a handful of paintings so the first run (or an offline one) has something to show.</summary>
    static int Seed(Settings s)
    {
        var mon = Native.GetMonitors()[0];
        using var curator = new Curator(s);
        var deadline = DateTime.UtcNow.AddMinutes(4);
        while (Cache.Count < 12 && DateTime.UtcNow < deadline)
        {
            var art = curator.NextAsync(mon.Width, mon.Height, CancellationToken.None).GetAwaiter().GetResult();
            if (art is null) Thread.Sleep(2000);
        }
        return 0;
    }
}
