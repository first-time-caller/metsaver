using System.Globalization;
using System.Text.RegularExpressions;

namespace MetSaver;

/// <summary>Reads the Met's free-text "Dimensions" field (as found in the Open Access CSV) into centimetres.</summary>
public static class DimensionParser
{
    // "32 1/4 x 46 1/4 in. (81.9 x 117.5 cm)"  - height x width (x depth)
    static readonly Regex HxW = new(@"\(\s*(?<h>[\d.]+)\s*[x×�]\s*(?<w>[\d.]+)(?:\s*[x×�]\s*[\d.]+)?\s*cm\s*\)", RegexOptions.Compiled);
    // "H. 28 3/4 in. (73 cm); W. 24 in. (61 cm)"
    static readonly Regex Labelled = new(@"H\.\s*[^()]*\(\s*(?<h>[\d.]+)\s*cm\s*\)\s*;?\s*W\.\s*[^()]*\(\s*(?<w>[\d.]+)\s*cm\s*\)", RegexOptions.Compiled);

    /// <summary>Uses the first line that yields a size (the main work, before "Framed:" or "mounting" lines).</summary>
    public static bool TryParseCm(string? text, out double widthCm, out double heightCm)
    {
        widthCm = heightCm = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var m = HxW.Match(line);
            if (!m.Success) m = Labelled.Match(line);
            if (!m.Success) continue;
            if (double.TryParse(m.Groups["h"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var h)
                && double.TryParse(m.Groups["w"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var w)
                && h > 0 && w > 0)
            {
                heightCm = h; widthCm = w;
                return true;
            }
        }
        return false;
    }
}
