namespace MetSaver;

/// <summary>Everything MetSaver stores lives under %LOCALAPPDATA%\MetSaver (per user, no admin rights).</summary>
public static class Paths
{
    /// <summary>Override with the METSAVER_HOME environment variable (handy for tests and portable use).</summary>
    public static string Home { get; } = Environment.GetEnvironmentVariable("METSAVER_HOME") is { Length: > 0 } custom
        ? custom
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MetSaver");
    public static string Cache => Path.Combine(Home, "cache");
    public static string ConfigFile => Path.Combine(Home, "config.json");
    public static string HistoryFile => Path.Combine(Home, "seen.json");
    public static string LogFile => Path.Combine(Home, "log.txt");
    /// <summary>The copy of the app Windows launches as the screensaver.</summary>
    public static string InstalledScr => Path.Combine(Home, "app", "MetSaver.scr");

    public static void Ensure()
    {
        Directory.CreateDirectory(Home);
        Directory.CreateDirectory(Cache);
    }
}
