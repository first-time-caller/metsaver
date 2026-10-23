using System.Text.RegularExpressions;

namespace MetSaver;

public readonly record struct VetResult(bool Ok, string Reason);

/// <summary>
/// Decides from metadata alone - before any image bytes are downloaded - whether a Met object is a
/// painting that will suit a given screen.
/// </summary>
public static class Vetting
{
    static readonly Regex Good = new("oil|tempera|acrylic|watercolor|gouache|encaustic|fresco|ink and color",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex Bad = new("print|etching|engraving|lithograph|drawing|photograph|pencil|chalk|charcoal|pastel|miniature|sculpture|bronze|ceramic|textile|silk thread",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Screen-independent checks: is this a public-domain painting of reasonable size?</summary>
    public static VetResult VetWork(MetObject o, Settings s)
    {
        if (!o.IsPublicDomain || string.IsNullOrEmpty(o.PrimaryImage))
            return new(false, "not public domain / no image");

        var cls = o.Classification ?? "";
        // Blank classification is common in the collection; the medium check below covers it.
        if (cls.Length > 0 && !cls.Contains("paint", StringComparison.OrdinalIgnoreCase))
            return new(false, $"classification: {cls}");

        var medium = o.Medium ?? "";
        if (Bad.IsMatch(medium) || !Good.IsMatch(medium))
            return new(false, $"medium: {medium}");

        if (!TryGetSizeCm(o, out var wCm, out var hCm) || Math.Min(wCm, hCm) < s.MinSideCm)
            return new(false, "size too small or unknown");

        return new(true, $"{medium}; {wCm:0}x{hCm:0} cm");
    }

    public static VetResult Vet(MetObject o, int screenW, int screenH, Settings s)
    {
        var work = VetWork(o, s);
        if (!work.Ok) return work;
        TryGetSizeCm(o, out var wCm, out var hCm);

        double pa = wCm / hCm, sa = (double)screenW / screenH;
        double fill = Math.Min(pa / sa, sa / pa);
        if (fill < s.MinScreenFill)
            return new(false, $"shape {pa:0.00} vs screen {sa:0.00} (fill {fill:P0})");

        return new(true, $"{work.Reason}; fill {fill:P0}");
    }

    /// <summary>0..1: how much of the screen's area the painting can fill when fitted without cropping (1 = same shape).</summary>
    public static double Fill(double wCm, double hCm, int screenW, int screenH)
    {
        double pa = wCm / hCm, sa = (double)screenW / screenH;
        return Math.Min(pa / sa, sa / pa);
    }

    /// <summary>Does a catalogued painting (physical and pixel size known) suit this screen and these settings?</summary>
    public static bool Fits(CatalogEntry e, int screenW, int screenH, Settings s)
    {
        if (Math.Min(e.WCm, e.HCm) < s.MinSideCm || e.HCm <= 0) return false;
        double pa = e.WCm / e.HCm, sa = (double)screenW / screenH;
        if (Math.Min(pa / sa, sa / pa) < s.MinScreenFill) return false;
        if (e.PxW > 0 && e.PxH > 0 && TooSmall(e.PxW, e.PxH, screenW, screenH, s)) return false;
        if (e.Bytes > s.MaxDownloadMb * 1024L * 1024L) return false;
        return true;
    }

    /// <summary>Physical size from the Overall measurement, else Sheet or Image.</summary>
    public static bool TryGetSizeCm(MetObject o, out double w, out double h)
    {
        w = h = 0;
        MetMeasurement? pick = null;
        foreach (var m in o.Measurements ?? new())
        {
            if (m.ElementName is not ("Overall" or "Sheet" or "Image")) continue;
            var e = m.ElementMeasurements;
            if (e is null || !(e.GetValueOrDefault("Width") > 0) || !(e.GetValueOrDefault("Height") > 0)) continue;
            if (m.ElementName == "Overall") { pick = m; break; }
            pick ??= m;
        }
        if (pick is null) return false;
        w = pick.ElementMeasurements!["Width"]!.Value;
        h = pick.ElementMeasurements["Height"]!.Value;
        return true;
    }

    /// <summary>True if an image of w x h pixels would need too much upscaling for this screen.</summary>
    public static bool TooSmall(int w, int h, int screenW, int screenH, Settings s)
    {
        double need = s.Cover
            ? Math.Max((double)screenW / w, (double)screenH / h)
            : Math.Min((double)screenW / w, (double)screenH / h);
        return need > 1 / s.MinPixelRatio;
    }
}
