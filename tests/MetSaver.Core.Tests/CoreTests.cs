using MetSaver;
using Xunit;

public class VettingTests
{
    static MetObject Painting(string medium = "Oil on canvas", double w = 100, double h = 60, string cls = "Paintings") => new()
    {
        ObjectID = 1, IsPublicDomain = true, PrimaryImage = "https://images.metmuseum.org/x.jpg",
        Title = "T", Medium = medium, Classification = cls,
        Measurements = new() { new() { ElementName = "Overall", ElementMeasurements = new() { ["Width"] = w, ["Height"] = h } } },
    };

    [Fact] public void AcceptsOilPaintingWithMatchingShape() =>
        Assert.True(Vetting.Vet(Painting(w: 160, h: 90), 2560, 1440, new Settings()).Ok);

    [Fact] public void RejectsWrongShapeForScreen() =>
        Assert.False(Vetting.Vet(Painting(w: 50, h: 70), 2560, 1440, new Settings()).Ok);

    [Fact] public void RejectsPrints() =>
        Assert.False(Vetting.Vet(Painting("Etching on paper", 160, 90), 2560, 1440, new Settings()).Ok);

    [Fact] public void RejectsNonPaintingClassification() =>
        Assert.False(Vetting.Vet(Painting(cls: "Sculpture"), 2560, 1440, new Settings()).Ok);

    [Fact] public void AcceptsBlankClassificationWhenMediumIsPaint() =>
        Assert.True(Vetting.Vet(Painting(w: 160, h: 90, cls: ""), 2560, 1440, new Settings()).Ok);

    [Fact] public void RejectsNotPublicDomain()
    {
        var o = Painting(w: 160, h: 90); o.IsPublicDomain = false;
        Assert.False(Vetting.Vet(o, 2560, 1440, new Settings()).Ok);
    }

    [Fact] public void RejectsTinyWorks() =>
        Assert.False(Vetting.Vet(Painting(w: 16, h: 9), 2560, 1440, new Settings()).Ok);

    [Fact] public void LooserShapeMatchAdmitsMore() =>
        Assert.True(Vetting.Vet(Painting(w: 100, h: 90), 2560, 1440, new Settings { MinScreenFill = 0.6 }).Ok);

    [Fact] public void PrefersOverallOverSheet()
    {
        var o = Painting(w: 160, h: 90);
        o.Measurements!.Insert(0, new() { ElementName = "Sheet", ElementMeasurements = new() { ["Width"] = 1, ["Height"] = 1 } });
        Assert.True(Vetting.TryGetSizeCm(o, out var w, out _));
        Assert.Equal(160, w);
    }

    [Fact] public void TooSmallImagesAreRejectedForCover()
    {
        var s = new Settings();                    // cover, min_pixel_ratio 0.6 => max 1.67x upscale
        Assert.True(Vetting.TooSmall(800, 450, 2560, 1440, s));
        Assert.False(Vetting.TooSmall(2000, 1125, 2560, 1440, s));
    }
}

public class ImageHeaderTests
{
    [Fact] public void ReadsPngSize()
    {
        var png = new byte[24];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(png, 0);
        png[18] = 0x0A; png[19] = 0x00;   // width 2560
        png[22] = 0x05; png[23] = 0xA0;   // height 1440
        Assert.True(ImageHeader.TryGetSize(png, out var w, out var h));
        Assert.Equal((2560, 1440), (w, h));
    }

    [Fact] public void ReadsJpegSizeAfterOtherSegments()
    {
        var jpg = new List<byte> { 0xFF, 0xD8, 0xFF, 0xE1, 0x00, 0x06, 1, 2, 3, 4 };            // APP1 segment to skip
        jpg.AddRange(new byte[] { 0xFF, 0xC0, 0x00, 0x11, 8, 0x05, 0xA0, 0x0A, 0x00, 3 });      // SOF0: h=1440 w=2560
        Assert.True(ImageHeader.TryGetSize(jpg.ToArray(), out var w, out var h));
        Assert.Equal((2560, 1440), (w, h));
    }

    [Fact] public void ReportsFalseUntilHeaderArrives()
    {
        var jpg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE1, 0x10, 0x00, 1, 2, 3 };                 // segment longer than data so far
        Assert.False(ImageHeader.TryGetSize(jpg, out _, out _));
    }

    [Fact] public void RejectsNonImages() =>
        Assert.False(ImageHeader.TryGetSize(new byte[] { 1, 2, 3, 4, 5 }, out _, out _));
}

public class SettingsTests
{
    [Fact] public void RoundTripsAndKeepsPythonCompatibleKeys()
    {
        var path = Path.Combine(Path.GetTempPath(), $"metsaver-{Guid.NewGuid():N}.json");
        try
        {
            new Settings { SecondsPerPainting = 45, Motion = "ken_burns" }.Save(path);
            Assert.Contains("\"seconds_per_painting\": 45", File.ReadAllText(path));
            var back = Settings.Load(path);
            Assert.Equal(45, back.SecondsPerPainting);
            Assert.Equal("ken_burns", back.Motion);
            Assert.Equal(0.15, back.Drift);
        }
        finally { File.Delete(path); }
    }

    [Fact] public void OldConfigGetsRetunedShapeFilter()
    {
        var path = Path.Combine(Path.GetTempPath(), $"metsaver-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{ \"min_screen_fill\": 0.55 }");   // pre-versioning file
            Assert.Equal(0.8, Settings.Load(path).MinScreenFill);
        }
        finally { File.Delete(path); }
    }

    [Fact] public void NormalizeClampsNonsense()
    {
        var s = new Settings { SecondsPerPainting = 0, DepartmentIds = new(), IdleSeconds = 5 };
        s.Normalize();
        Assert.Equal(5, s.SecondsPerPainting);
        Assert.NotEmpty(s.DepartmentIds);
        Assert.Equal(60, s.IdleSeconds);
    }
}

public class CatalogTests
{
    static CatalogEntry E(int id, double w, double h, int dept = 11, int pw = 4000, int ph = 3000) =>
        new(id, $"Work {id}", "Artist", "1880", "Oil on canvas", dept, $"https://images.metmuseum.org/{id}.jpg", w, h, pw, ph, 5_000_000);

    [Fact] public void RoundTripsThroughGzip()
    {
        var cat = new Catalog(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), new[] { E(1, 160, 90), E(2, 50, 70) });
        using var ms = new MemoryStream();
        cat.Save(ms);
        ms.Position = 0;
        var back = Catalog.Load(ms);
        Assert.Equal(2, back.Entries.Count);
        Assert.Equal("Work 1", back.Entries[0].Title);
        Assert.Equal(cat.Generated, back.Generated);
    }

    [Fact] public void PickFiltersByShapeSeenAndDepartment()
    {
        var cat = new Catalog(DateTime.UtcNow, new[]
        {
            E(1, 160, 90),                 // fits 16:9
            E(2, 50, 70),                  // portrait: wrong shape
            E(3, 160, 90, dept: 21),       // fits but department not selected
            E(4, 160, 90),                 // fits but already seen
            E(5, 160, 90, pw: 600, ph: 340) // fits shape but pixels too small
        });
        var s = new Settings { DepartmentIds = new() { 11 } };
        var pick = cat.Pick(2560, 1440, s, id => id == 4);
        Assert.Equal(1, pick?.Id);
        Assert.Null(cat.Pick(2560, 1440, s, id => id is 1 or 4));
    }

    [Fact] public void ShapeMatchesTheScreenItIsAskedAbout()
    {
        var cat = new Catalog(DateTime.UtcNow, new[] { E(1, 54, 96) });            // tall painting (9:16)
        Assert.Null(cat.Pick(2560, 1440, new Settings(), _ => false));
        Assert.NotNull(cat.Pick(1080, 1920, new Settings(), _ => false));         // portrait monitor
    }
}

public class DimensionParserTests
{
    [Theory]
    [InlineData("32 1/4 x 46 1/4 in. (81.9 x 117.5 cm)", 117.5, 81.9)]
    [InlineData("77 1/2 x 58 1/4 in. (196.9 x 148 cm)", 148, 196.9)]
    [InlineData("45 7/8 � 35 1/4 in. (116.5 � 89.5 cm)\r\n", 89.5, 116.5)]
    [InlineData("H. 28 3/4 in. (73 cm); W. 24 in. (61 cm)", 61, 73)]
    [InlineData("Image: 28 3/8 x 21 1/4 in. (72.1 x 54 cm)\r\nOverall with mounting: 65 5/8 x 26 1/2 in. (166.7 x 67.3 cm)", 54, 72.1)]
    [InlineData("10 x 20 x 5 in. (25.4 x 50.8 x 12.7 cm)", 50.8, 25.4)]
    public void ParsesHeightByWidthInCentimetres(string text, double w, double h)
    {
        Assert.True(DimensionParser.TryParseCm(text, out var pw, out var ph));
        Assert.Equal(w, pw, 1);
        Assert.Equal(h, ph, 1);
    }

    [Theory]
    [InlineData("Dimensions unavailable")]
    [InlineData("Diam. 10 1/4 in. (26 cm)")]
    [InlineData("")]
    public void RejectsUnusableText(string text) => Assert.False(DimensionParser.TryParseCm(text, out _, out _));
}

public class MoodTests
{
    static readonly DateTime Midnight = new(2026, 1, 15, 23, 0, 0);
    static readonly DateTime Noon = new(2026, 7, 15, 12, 0, 0);

    [Fact] public void NightScenesPreferredAfterDarkAndDemotedAtNoon()
    {
        Assert.True(Mood.Weight("Moonlit Harbor", "Oil on canvas", Midnight) > 2);
        Assert.True(Mood.Weight("Moonlit Harbor", "Oil on canvas", Noon) < 0.5);
    }

    [Fact] public void SnowIsFavouredInWinterAndNotInSummer() =>
        Assert.True(Mood.Weight("Skating in the Snow", "Oil", Midnight) > Mood.Weight("Skating in the Snow", "Oil", Noon));

    [Fact] public void NeutralTitlesStayNeutral() =>
        Assert.Equal(1.0, Mood.Weight("Portrait of a Man", "Oil on canvas", Noon));
}

public class WeightedPickTests
{
    static CatalogEntry E(int id, double w, double h, string title = "Untitled") =>
        new(id, title, "A", "1880", "Oil on canvas", 11, "", w, h, 0, 0, 0);

    [Fact] public void BetterShapedPaintingsAreChosenMoreOften()
    {
        var cat = new Catalog(DateTime.UtcNow, new[] { E(1, 160, 90), E(2, 144, 90) });   // 16:9 vs 16:10 on a 16:9 screen
        var s = new Settings { TimeOfDay = false };
        var rng = new Random(7);
        int first = Enumerable.Range(0, 2000).Count(_ => cat.Pick(2560, 1440, s, _ => false, rng)!.Id == 1);
        Assert.InRange(first, 1100, 1500);   // weights 1.0 vs 0.41 => ~71%
    }

    [Fact] public void FavouritesAreBoosted()
    {
        var cat = new Catalog(DateTime.UtcNow, new[] { E(1, 160, 90), E(2, 160, 90) });
        var s = new Settings { TimeOfDay = false };
        var rng = new Random(3);
        int fav = Enumerable.Range(0, 2000).Count(_ => cat.Pick(2560, 1440, s, _ => false, rng, id => id == 2 ? 3.0 : 1.0)!.Id == 2);
        Assert.InRange(fav, 1400, 1600);     // 3:1 => 75%
    }

    [Fact] public void MoodSteersTheChoice()
    {
        var cat = new Catalog(DateTime.UtcNow, new[] { E(1, 160, 90, "Moonlit Night"), E(2, 160, 90, "Sunny Garden") });
        var rng = new Random(5);
        var night = new DateTime(2026, 1, 15, 23, 0, 0);
        int moon = Enumerable.Range(0, 1000).Count(_ => cat.Pick(2560, 1440, new Settings(), _ => false, rng, null, night)!.Id == 1);
        Assert.True(moon > 700);
    }
}

public class PreferencesTests
{
    [Fact] public void TogglesAndPersists()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fav-{Guid.NewGuid():N}.json");
        try
        {
            var p = new Preferences(path);
            Assert.True(p.Toggle(42));
            Assert.True(new Preferences(path).IsFavourite(42));
            Assert.False(p.Toggle(42));
            Assert.False(new Preferences(path).IsFavourite(42));
            Assert.Equal(1.0, p.Weight(42));
        }
        finally { File.Delete(path); }
    }
}

public class PairingTests
{
    static CatalogEntry E(int id, string artist, string date, double w = 160, double h = 90, int dept = 11) =>
        new(id, $"Work {id}", artist, date, "Oil on canvas", dept, "", w, h, 0, 0, 0);

    static readonly (int, int)[] TwoScreens = { (1920, 1080), (1920, 1080) };

    [Theory]
    [InlineData("ca. 1740", 1740)]
    [InlineData("1883-85", 1883)]
    [InlineData("early 1780s", 1780)]
    [InlineData("", null)]
    public void ParsesYears(string date, int? year) => Assert.Equal(year, Pairing.YearOf(date));

    [Fact] public void SameArtistNeedsOneFittingWorkPerMonitor()
    {
        var cat = new Catalog(DateTime.UtcNow, new[] { E(1, "Daubigny", "1863"), E(2, "Daubigny", "1874"), E(3, "Solo", "1800") });
        var sets = Pairing.Find(cat, TwoScreens, new Settings(), _ => false);
        var set = Assert.Single(sets, x => x.Kind == PairKind.SameArtist);
        Assert.Equal("Daubigny", set.Theme);
        Assert.Equal(new[] { 1, 2 }, set.Works.Select(w => w.Id).OrderBy(i => i));
    }

    [Fact] public void EachMonitorGetsAWorkThatSuitsItsShape()
    {
        var cat = new Catalog(DateTime.UtcNow, new[] { E(1, "A", "1860", 160, 90), E(2, "A", "1861", 54, 96) });
        var sets = Pairing.Find(cat, new[] { (2560, 1440), (1080, 1920) }, new Settings(), _ => false);
        var set = Assert.Single(sets, x => x.Kind == PairKind.SameArtist);
        Assert.Equal(1, set.Works[0].Id);   // landscape monitor
        Assert.Equal(2, set.Works[1].Id);   // portrait monitor
    }

    [Fact] public void SameEraSetsUseDifferentArtistsFromTheSameDecades()
    {
        var cat = new Catalog(DateTime.UtcNow, new[] { E(1, "A", "1862"), E(2, "B", "1868"), E(3, "C", "1620") });
        var set = Assert.Single(Pairing.Find(cat, TwoScreens, new Settings(), _ => false), x => x.Kind == PairKind.SameEra);
        Assert.DoesNotContain(set.Works, w => w.Id == 3);
    }

    [Fact] public void SeenWorksAreExcluded()
    {
        var cat = new Catalog(DateTime.UtcNow, new[] { E(1, "A", "1860"), E(2, "A", "1861") });
        Assert.Empty(Pairing.Find(cat, TwoScreens, new Settings(), id => id == 1));
    }
}
