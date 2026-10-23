using System.Text.RegularExpressions;

namespace MetSaver;

/// <summary>Why a set of paintings belongs together on a multi-monitor desk.</summary>
public enum PairKind { SameArtist, SameEra }

/// <summary>One painting per monitor (in monitor order), chosen to belong together.</summary>
public sealed record PairSet(PairKind Kind, string Theme, IReadOnlyList<CatalogEntry> Works, double Score);

/// <summary>
/// Picks related paintings for several monitors at once: the same artist if the catalogue has enough of their work that
/// suits every screen's shape, otherwise paintings from the same few decades and collection.
/// </summary>
public static class Pairing
{
    static readonly Regex Year = new(@"(?<!\d)(1[0-9]{3}|20[0-2][0-9])(?!\d)", RegexOptions.Compiled);

    /// <summary>First four-digit year in a free-text date such as "ca. 1740" or "1883-85".</summary>
    public static int? YearOf(string date) => Year.Match(date ?? "") is { Success: true } m ? int.Parse(m.Value) : null;

    public static bool KnownArtist(string artist) =>
        !string.IsNullOrWhiteSpace(artist) && !artist.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// All sets that fit the given screens (pixel sizes in monitor order). <paramref name="seen"/> excludes shown works.
    /// Sorted best first: tighter relationship, then better-fitting shapes.
    /// </summary>
    public static List<PairSet> Find(Catalog catalog, IReadOnlyList<(int W, int H)> screens, Settings s, Func<int, bool> seen,
        int eraYears = 25)
    {
        int n = screens.Count;
        var fits = Enumerable.Range(0, n)
            .Select(i => catalog.Candidates(screens[i].W, screens[i].H, s, seen).ToList()).ToList();
        var sets = new List<PairSet>();
        if (n < 2) return sets;

        // Same artist: every monitor must be able to take a (different) work by them.
        var artists = fits[0].Where(e => KnownArtist(e.Artist)).Select(e => e.Artist).Distinct();
        foreach (var artist in artists)
        {
            var pool = fits.Select(f => f.Where(e => e.Artist == artist).ToList()).ToList();
            if (pool.Any(p => p.Count == 0)) continue;
            var works = Assign(pool, screens);
            if (works is not null) sets.Add(new PairSet(PairKind.SameArtist, artist, works, Score(works, screens, bonus: 1.0)));
        }

        // Same era and collection: within `eraYears` of each other.
        var byBucket = fits[0].Select(e => (e, y: YearOf(e.Date))).Where(t => t.y is not null)
            .GroupBy(t => (t.e.Dept, bucket: t.y!.Value / eraYears));
        foreach (var g in byBucket)
        {
            int lo = g.Key.bucket * eraYears, hi = lo + eraYears;
            var pool = fits.Select(f => f.Where(e => e.Dept == g.Key.Dept && YearOf(e.Date) is { } y && y >= lo && y < hi).ToList()).ToList();
            if (pool.Any(p => p.Count == 0)) continue;
            var works = Assign(pool, screens);
            if (works is null || works.Select(w => w.Artist).Distinct().Count() < works.Count) continue;   // era sets should be different artists
            sets.Add(new PairSet(PairKind.SameEra, $"{lo}-{hi - 1}", works, Score(works, screens, bonus: 0.5)));
        }
        return sets.OrderByDescending(x => x.Score).ToList();
    }

    /// <summary>Greedy: each monitor in turn takes the best-fitting work not already used.</summary>
    static List<CatalogEntry>? Assign(List<List<CatalogEntry>> pool, IReadOnlyList<(int W, int H)> screens)
    {
        var used = new HashSet<int>();
        var result = new List<CatalogEntry>();
        // Most constrained monitor (fewest options) first, then restore monitor order.
        var order = Enumerable.Range(0, pool.Count).OrderBy(i => pool[i].Count).ToList();
        var chosen = new CatalogEntry?[pool.Count];
        foreach (int i in order)
        {
            var best = pool[i].Where(e => !used.Contains(e.Id))
                .OrderByDescending(e => Vetting.Fill(e.WCm, e.HCm, screens[i].W, screens[i].H)).FirstOrDefault();
            if (best is null) return null;
            used.Add(best.Id); chosen[i] = best;
        }
        result.AddRange(chosen!);
        return result;
    }

    static double Score(List<CatalogEntry> works, IReadOnlyList<(int W, int H)> screens, double bonus)
    {
        double fill = works.Select((e, i) => Vetting.Fill(e.WCm, e.HCm, screens[i].W, screens[i].H)).Average();
        bool sameMedium = works.Select(e => e.Medium.Split(' ')[0].ToLowerInvariant()).Distinct().Count() == 1;
        return bonus + fill + (sameMedium ? 0.15 : 0);
    }
}
