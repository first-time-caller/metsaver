using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MetSaver;

/// <summary>One vetted painting. Short JSON names keep the shipped catalog small.</summary>
public sealed record CatalogEntry(
    [property: JsonPropertyName("i")] int Id,
    [property: JsonPropertyName("t")] string Title,
    [property: JsonPropertyName("a")] string Artist,
    [property: JsonPropertyName("d")] string Date,
    [property: JsonPropertyName("m")] string Medium,
    [property: JsonPropertyName("c")] int Dept,
    [property: JsonPropertyName("u")] string Url,
    [property: JsonPropertyName("w")] double WCm,
    [property: JsonPropertyName("h")] double HCm,
    [property: JsonPropertyName("pw")] int PxW,
    [property: JsonPropertyName("ph")] int PxH,
    [property: JsonPropertyName("b")] long Bytes)
{
    public Caption ToCaption() => new(
        string.IsNullOrWhiteSpace(Title) ? "Untitled" : Title,
        string.IsNullOrWhiteSpace(Artist) ? "Unknown artist" : Artist, Date, Medium);
}

/// <summary>
/// A pre-built, pre-vetted list of public-domain paintings (physical size, pixel size, file size, URL). With it the
/// saver skips searching and vetting at run time: it picks a known-good painting for the screen and downloads just that.
/// </summary>
public sealed class Catalog
{
    sealed record Doc(
        [property: JsonPropertyName("generated")] DateTime Generated,
        [property: JsonPropertyName("entries")] List<CatalogEntry> Entries);

    static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    public DateTime Generated { get; }
    public IReadOnlyList<CatalogEntry> Entries { get; }
    public Catalog(DateTime generated, IReadOnlyList<CatalogEntry> entries) { Generated = generated; Entries = entries; }

    public static Catalog Load(Stream gzip)
    {
        using var gz = new GZipStream(gzip, CompressionMode.Decompress);
        var doc = JsonSerializer.Deserialize<Doc>(gz, Json) ?? throw new InvalidDataException("empty catalog");
        return new Catalog(doc.Generated, doc.Entries);
    }

    public static Catalog? TryLoadFile(string path)
    {
        try { using var f = File.OpenRead(path); return Load(f); }
        catch (Exception e) { if (File.Exists(path)) Log.Error(e, $"catalog {path}"); return null; }
    }

    public void Save(Stream output)
    {
        using var gz = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true);
        JsonSerializer.Serialize(gz, new Doc(Generated, Entries.ToList()), Json);
    }

    /// <summary>Entries that suit this screen, the chosen collections, and haven't been shown.</summary>
    public IEnumerable<CatalogEntry> Candidates(int screenW, int screenH, Settings s, Func<int, bool> seen) =>
        Entries.Where(e => s.DepartmentIds.Contains(e.Dept) && !seen(e.Id) && Vetting.Fits(e, screenW, screenH, s));

    /// <summary>
    /// Weighted random pick among the candidates. Better-shaped paintings are more likely (fill^4, which is what makes
    /// a portrait monitor and an ultrawide each get paintings that suit them), then mood (hour/season) and favourites.
    /// </summary>
    public CatalogEntry? Pick(int screenW, int screenH, Settings s, Func<int, bool> seen, Random? rng = null,
        Func<int, double>? extraWeight = null, DateTime? now = null)
    {
        var list = Candidates(screenW, screenH, s, seen).ToList();
        if (list.Count == 0) return null;
        var when = now ?? DateTime.Now;
        double[] weights = list.Select(e =>
        {
            double w = Math.Pow(Vetting.Fill(e.WCm, e.HCm, screenW, screenH), 4);
            if (s.TimeOfDay) w *= Mood.Weight(e.Title, e.Medium, when);
            if (extraWeight is not null) w *= extraWeight(e.Id);
            return Math.Max(w, 1e-6);
        }).ToArray();
        double r = (rng ?? Random.Shared).NextDouble() * weights.Sum();
        for (int i = 0; i < list.Count; i++) { r -= weights[i]; if (r <= 0) return list[i]; }
        return list[^1];
    }
}
