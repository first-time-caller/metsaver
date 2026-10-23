using System.Text.Json;
using System.Text.Json.Serialization;

namespace MetSaver;

/// <summary>User settings, stored as config.json (keys are snake_case and match the earlier Python version).</summary>
public sealed class Settings
{
    public const int CurrentVersion = 3;

    [JsonPropertyName("_v")] public int Version { get; set; } = CurrentVersion;
    [JsonPropertyName("seconds_per_painting")] public int SecondsPerPainting { get; set; } = 90;
    /// <summary>Met department ids: 11 European Paintings, 1 American Paintings, 21 Modern &amp; Contemporary.</summary>
    [JsonPropertyName("department_ids")] public List<int> DepartmentIds { get; set; } = new() { 11, 1 };
    [JsonPropertyName("search_terms")] public List<string> SearchTerms { get; set; } =
        new() { "landscape", "portrait", "still life", "river", "garden", "sea", "figures", "flowers" };
    /// <summary>"cover" = art fills the screen (centre-cropped); "fit" = whole painting on a mat.</summary>
    [JsonPropertyName("fill_mode")] public string FillMode { get; set; } = "cover";
    /// <summary>A painting's shape must fill at least this fraction of the screen's area.</summary>
    [JsonPropertyName("min_screen_fill")] public double MinScreenFill { get; set; } = 0.8;
    [JsonPropertyName("min_side_cm")] public double MinSideCm { get; set; } = 20;
    /// <summary>Reject images that would need more than 1/this upscaling to cover the screen.</summary>
    [JsonPropertyName("min_pixel_ratio")] public double MinPixelRatio { get; set; } = 0.6;
    [JsonPropertyName("max_download_mb")] public int MaxDownloadMb { get; set; } = 40;
    [JsonPropertyName("show_caption")] public bool ShowCaption { get; set; } = true;
    /// <summary>top-left, top-right, bottom-left, bottom-right.</summary>
    [JsonPropertyName("info_corner")] public string InfoCorner { get; set; } = "bottom-left";
    /// <summary>The info card fades out after this many seconds; 0 = always visible.</summary>
    [JsonPropertyName("info_seconds")] public int InfoSeconds { get; set; } = 15;
    /// <summary>"off", "subtle" (5% drift) or "ken_burns" (15% zoom in or out plus pan).</summary>
    [JsonPropertyName("motion")] public string Motion { get; set; } = "subtle";
    [JsonPropertyName("max_width")] public int MaxWidth { get; set; } = 2560;
    [JsonPropertyName("max_height")] public int MaxHeight { get; set; } = 1440;
    [JsonPropertyName("mat_color")] public string MatColor { get; set; } = "#17150f";
    /// <summary>Idle time before Windows starts the screensaver (applied by "Set as my screensaver").</summary>
    [JsonPropertyName("idle_seconds")] public int IdleSeconds { get; set; } = 180;
    /// <summary>Where to fetch a fresher vetted catalog (a catalog.json.gz URL). Empty = use only the built-in one.</summary>
    /// <summary>Favour paintings that suit the hour and the season (night scenes at night, snow in winter...).</summary>
    [JsonPropertyName("time_of_day")] public bool TimeOfDay { get; set; } = true;
    /// <summary>Dim with the ambient light sensor (or the clock at night); calm the motion on battery.</summary>
    [JsonPropertyName("ambient_aware")] public bool AmbientAware { get; set; } = true;
    /// <summary>Crop toward the busiest part of a painting instead of dead centre.</summary>
    [JsonPropertyName("smart_crop")] public bool SmartCrop { get; set; } = true;
    [JsonPropertyName("catalog_url")] public string CatalogUrl { get; set; } = "";
    [JsonPropertyName("previous_screensaver")] public string? PreviousScreensaver { get; set; }

    [JsonIgnore] public bool Cover => !string.Equals(FillMode, "fit", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore] public double Drift => Motion switch { "off" => 0, "ken_burns" => 0.15, _ => 0.05 };

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    public Settings Clone() => JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(this, Json), Json)!;

    public static Settings Load(string? path = null)
    {
        path ??= Paths.ConfigFile;
        try
        {
            if (File.Exists(path))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Json) ?? new Settings();
                var oldVersion = s.Version;
                // The version defaults to current when the key is absent, so detect "v1" files by reading the raw value.
                using (var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }))
                    oldVersion = doc.RootElement.TryGetProperty("_v", out var v) && v.TryGetInt32(out var n) ? n : 1;
                if (oldVersion < 2) s.MinScreenFill = 0.8;  // v1 files predate the retuned shape filter
                s.Normalize();
                if (oldVersion < CurrentVersion) { s.Version = CurrentVersion; s.Save(path); }
                return s;
            }
        }
        catch (Exception e) { Log.Error(e, "config unreadable, using defaults"); }
        var fresh = new Settings();
        fresh.Save(path);
        return fresh;
    }

    public void Save(string? path = null)
    {
        path ??= Paths.ConfigFile;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception e) { Log.Error(e, "saving config"); }
    }

    /// <summary>Clamp hand-edited values into sane ranges.</summary>
    public void Normalize()
    {
        SecondsPerPainting = Math.Clamp(SecondsPerPainting, 5, 3600);
        MinScreenFill = Math.Clamp(MinScreenFill, 0.3, 1.0);
        MinPixelRatio = Math.Clamp(MinPixelRatio, 0.2, 1.0);
        InfoSeconds = Math.Max(0, InfoSeconds);
        IdleSeconds = Math.Clamp(IdleSeconds, 60, 36000);
        MaxWidth = Math.Max(640, MaxWidth);
        MaxHeight = Math.Max(360, MaxHeight);
        if (DepartmentIds.Count == 0) DepartmentIds = new() { 11 };
        if (SearchTerms.Count == 0) SearchTerms = new Settings().SearchTerms;
    }
}
