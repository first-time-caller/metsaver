using System.Text;
using MetSaver;

namespace MetSaver.CatalogBuilder;

/// <summary>
/// Builds the catalog from the Met's Open Access CSV (https://github.com/metmuseum/openaccess) instead of crawling the API.
/// The ~340 MB CSV is only a build-time input; the shipped catalog is a few hundred KB of compact records.
/// Image URL and pixel size are not in the CSV, so entries leave them empty: the app looks up the one chosen
/// object at run time and checks the image header before downloading the picture.
/// </summary>
static class CsvImport
{
    // Met department names (CSV) -> the department ids used by the API and by settings.
    static readonly Dictionary<string, int> Departments = new(StringComparer.OrdinalIgnoreCase)
    {
        ["European Paintings"] = 11,
        ["Robert Lehman Collection"] = 11,   // Lehman paintings are European; no separate switch in settings
        ["The American Wing"] = 1,
        ["Modern and Contemporary Art"] = 21,
        ["Asian Art"] = 6,
    };

    // Shapes no real screen could use are dropped: keeps the file small (e.g. tall hanging scrolls).
    const double MinAspect = 0.45, MaxAspect = 2.6;

    public static List<CatalogEntry> Import(string csvPath, Settings settings, out Dictionary<string, int> rejected)
    {
        var counts = new Dictionary<string, int>();
        rejected = counts;
        var entries = new List<CatalogEntry>();
        using var reader = new StreamReader(csvPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1 << 20);
        var header = ReadRecord(reader) ?? throw new InvalidDataException("empty CSV");
        int Col(string name) { int i = Array.IndexOf(header, name); return i >= 0 ? i : throw new InvalidDataException($"column '{name}' missing"); }
        int cPd = Col("Is Public Domain"), cId = Col("Object ID"), cDept = Col("Department"), cTitle = Col("Title"),
            cArtist = Col("Artist Display Name"), cDate = Col("Object Date"), cMedium = Col("Medium"),
            cDims = Col("Dimensions"), cClass = Col("Classification");

        void Reject(string why) => counts[why] = counts.GetValueOrDefault(why) + 1;

        string[]? r;
        while ((r = ReadRecord(reader)) is not null)
        {
            if (r.Length <= cClass) continue;
            if (!string.Equals(r[cPd], "True", StringComparison.OrdinalIgnoreCase)) continue;       // silently: most of the museum
            if (!Departments.TryGetValue(r[cDept].Trim(), out var dept)) continue;
            if (!int.TryParse(r[cId], out var id)) continue;

            if (!DimensionParser.TryParseCm(r[cDims], out var w, out var h)) { Reject("no usable dimensions"); continue; }

            // Reuse the app's own vetting so the catalog and the live path agree on what a "painting" is.
            var asObject = new MetObject
            {
                ObjectID = id, IsPublicDomain = true, PrimaryImage = "pending",
                Classification = r[cClass], Medium = r[cMedium],
                Measurements = new() { new MetMeasurement { ElementName = "Overall", ElementMeasurements = new() { ["Width"] = w, ["Height"] = h } } },
            };
            var vet = Vetting.VetWork(asObject, settings);
            if (!vet.Ok) { Reject(vet.Reason.Split(':')[0].Split(';')[0]); continue; }

            // "Paintings" classification, or blank (the American Wing leaves it blank). Anything else is a different object type.
            var cls = r[cClass];
            if (cls.Length > 0 && !cls.StartsWith("Paintings", StringComparison.OrdinalIgnoreCase)) { Reject("classification"); continue; }

            double aspect = w / h;
            if (aspect < MinAspect || aspect > MaxAspect) { Reject("shape unusable on any screen"); continue; }

            entries.Add(new CatalogEntry(id, Trim(r[cTitle], 90), Trim(r[cArtist], 60), Trim(r[cDate], 30), Trim(FirstLine(r[cMedium]), 70),
                dept, "", Math.Round(w, 1), Math.Round(h, 1), 0, 0, 0));
        }
        return entries.OrderBy(e => e.Dept).ThenBy(e => e.Id).ToList();
    }

    static string FirstLine(string s) => s.Split('\n')[0].Trim();
    static string Trim(string s, int max) { s = s.Replace("\r", " ").Replace("\n", " ").Trim(); return s.Length <= max ? s : s[..(max - 1)] + "…"; }

    /// <summary>Minimal streaming CSV reader: RFC 4180 quoting, quoted fields may contain commas and newlines.</summary>
    static string[]? ReadRecord(StreamReader reader)
    {
        if (reader.EndOfStream) return null;
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool quoted = false;
        int c;
        while ((c = reader.Read()) >= 0)
        {
            char ch = (char)c;
            if (quoted)
            {
                if (ch == '"') { if (reader.Peek() == '"') { reader.Read(); sb.Append('"'); } else quoted = false; }
                else sb.Append(ch);
            }
            else if (ch == '"') quoted = true;
            else if (ch == ',') { fields.Add(sb.ToString()); sb.Clear(); }
            else if (ch == '\n') { fields.Add(sb.ToString().TrimEnd('\r')); return fields.ToArray(); }
            else sb.Append(ch);
        }
        fields.Add(sb.ToString());
        return fields.Count == 1 && fields[0].Length == 0 ? null : fields.ToArray();
    }
}
