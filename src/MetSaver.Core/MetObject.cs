namespace MetSaver;

/// <summary>The subset of a Met collection object (api /objects/{id}) that MetSaver needs.</summary>
public sealed class MetObject
{
    public int ObjectID { get; set; }
    public bool IsPublicDomain { get; set; }
    public string? PrimaryImage { get; set; }
    public string? Title { get; set; }
    public string? ArtistDisplayName { get; set; }
    public string? ObjectDate { get; set; }
    public string? Medium { get; set; }
    public string? Classification { get; set; }
    public string? ArtistDisplayBio { get; set; }
    public string? CreditLine { get; set; }
    public string? Dimensions { get; set; }
    public string? GalleryNumber { get; set; }
    public string? ObjectURL { get; set; }
    public List<MetMeasurement>? Measurements { get; set; }
}

public sealed class MetMeasurement
{
    public string? ElementName { get; set; }
    public Dictionary<string, double?>? ElementMeasurements { get; set; }
}

/// <summary>What the info card shows. The extra fields feed the expanded "details" panel in gallery mode.</summary>
public sealed record Caption(string Title, string Artist, string Date, string Medium,
    string Bio = "", string Credit = "", string Dimensions = "", string Gallery = "", string Url = "")
{
    public static Caption From(MetObject o) => new(
        string.IsNullOrWhiteSpace(o.Title) ? "Untitled" : o.Title!,
        string.IsNullOrWhiteSpace(o.ArtistDisplayName) ? "Unknown artist" : o.ArtistDisplayName!,
        o.ObjectDate ?? "", o.Medium ?? "",
        Bio: o.ArtistDisplayBio ?? "", Credit: o.CreditLine ?? "",
        Dimensions: FirstLine(o.Dimensions), Gallery: o.GalleryNumber ?? "", Url: o.ObjectURL ?? "");

    static string FirstLine(string? s) => (s ?? "").Split('\n')[0].Trim();
}
