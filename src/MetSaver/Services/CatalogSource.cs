using System.Reflection;

namespace MetSaver;

/// <summary>
/// Finds the vetted catalog: the newest of the copy built into the app and a downloaded refresh in
/// %LOCALAPPDATA%\MetSaver\catalog.json.gz. Returns null if neither exists (the app then searches live).
/// </summary>
static class CatalogSource
{
    static string Downloaded => Path.Combine(Paths.Home, "catalog.json.gz");

    public static Catalog? Load()
    {
        Catalog? built = null;
        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("catalog.json.gz"))
            if (s is not null) { try { built = Catalog.Load(s); } catch (Exception e) { Log.Error(e, "built-in catalog"); } }
        var fresh = File.Exists(Downloaded) ? Catalog.TryLoadFile(Downloaded) : null;
        var best = fresh is not null && (built is null || fresh.Generated > built.Generated) ? fresh : built;
        if (best is not null) Log.Info($"catalog: {best.Entries.Count} paintings, built {best.Generated:yyyy-MM-dd}");
        else Log.Info("catalog: none - using live search");
        return best;
    }

    /// <summary>Quietly fetch a newer catalog if settings point at one and ours is over a week old. Never throws.</summary>
    public static async Task RefreshAsync(Settings s, Catalog? current)
    {
        if (string.IsNullOrWhiteSpace(s.CatalogUrl)) return;
        if (current is not null && DateTime.UtcNow - current.Generated < TimeSpan.FromDays(7)) return;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MetSaver/1.0");
            var bytes = await http.GetByteArrayAsync(s.CatalogUrl);
            using var ms = new MemoryStream(bytes);
            var fetched = Catalog.Load(ms);                 // validate before replacing
            if (fetched.Entries.Count < 50) return;
            Paths.Ensure();
            File.WriteAllBytes(Downloaded + ".tmp", bytes);
            File.Move(Downloaded + ".tmp", Downloaded, overwrite: true);
            Log.Info($"catalog refreshed: {fetched.Entries.Count} paintings");
        }
        catch (Exception e) { Log.Error(e, "catalog refresh"); }
    }
}
