using System.Diagnostics;

namespace MetSaver;

/// <summary>Tiny file logger. Useful when someone says "it doesn't show anything": send log.txt.</summary>
public static class Log
{
    static readonly object Gate = new();
    public static void Info(string message) => Write("INFO ", message);
    public static void Error(Exception e, string? context = null) => Write("ERROR", $"{context} {e}");

    static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}";
        Debug.WriteLine(line);
        try
        {
            lock (Gate)
            {
                Paths.Ensure();
                var f = new FileInfo(Paths.LogFile);
                if (f.Exists && f.Length > 512 * 1024) f.Delete();
                File.AppendAllText(Paths.LogFile, line + Environment.NewLine);
            }
        }
        catch { /* logging must never break the screensaver */ }
    }
}
