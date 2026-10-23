using System.Text.Json;

namespace MetSaver;

/// <summary>A downloaded painting: JPEG bytes plus what to show in the info card.</summary>
public sealed record Artwork(int Id, Caption Caption, byte[] Jpeg);

/// <summary>On-disk cache of recently shown paintings, so the saver works offline and starts instantly.</summary>
public static class Cache
{
    const int Keep = 60;
    static readonly Random Rng = new();

    public static int Count => Directory.Exists(Paths.Cache) ? Directory.GetFiles(Paths.Cache, "*.jpg").Length : 0;

    public static void Save(Artwork a)
    {
        Paths.Ensure();
        File.WriteAllBytes(Path.Combine(Paths.Cache, $"{a.Id}.jpg"), a.Jpeg);
        File.WriteAllText(Path.Combine(Paths.Cache, $"{a.Id}.json"), JsonSerializer.Serialize(a.Caption));
        Trim();
    }

    public static Artwork? Pick()
    {
        try
        {
            if (!Directory.Exists(Paths.Cache)) return null;
            var files = Directory.GetFiles(Paths.Cache, "*.jpg");
            if (files.Length == 0) return null;
            var jpg = files[Rng.Next(files.Length)];
            var meta = Path.ChangeExtension(jpg, ".json");
            var caption = File.Exists(meta) ? JsonSerializer.Deserialize<Caption>(File.ReadAllText(meta)) : null;
            _ = int.TryParse(Path.GetFileNameWithoutExtension(jpg), out var id);
            return new Artwork(id, caption ?? new Caption("Untitled", "", "", ""), File.ReadAllBytes(jpg));
        }
        catch (Exception e) { Log.Error(e, "reading cache"); return null; }
    }

    public static void Clear()
    {
        if (Directory.Exists(Paths.Cache)) Directory.Delete(Paths.Cache, true);
        Paths.Ensure();
    }

    static void Trim()
    {
        var old = new DirectoryInfo(Paths.Cache).GetFiles("*.jpg").OrderByDescending(f => f.LastWriteTimeUtc).Skip(Keep);
        foreach (var f in old)
        {
            f.Delete();
            var meta = Path.ChangeExtension(f.FullName, ".json");
            if (File.Exists(meta)) File.Delete(meta);
        }
    }
}
