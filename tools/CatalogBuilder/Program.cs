using System.Collections.Concurrent;
using System.Diagnostics;
using MetSaver;

// Crawls the Met Open Access API once and writes a vetted catalog of public-domain paintings.
//   dotnet run --project tools/CatalogBuilder -- data/catalog.json.gz [--limit 300]
// For each painting it records physical size, pixel size and file size (read from the first bytes of the image,
// never the whole file) so the screensaver can choose a suitable painting without any run-time vetting.
//
// Be a good API citizen: the Met asks for at most 80 requests/second and its firewall throttles bursts, so this
// stays around 8/second in total and backs off when it sees 403/429/timeouts.

var output = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : "catalog.json.gz";

// Experiment: how well could related paintings be paired across several monitors?   --pairs catalog.json.gz
if (args.Contains("--pairs"))
{
    var cat = Catalog.TryLoadFile(args[Array.IndexOf(args, "--pairs") + 1]) ?? throw new FileNotFoundException("catalog");
    var s = new Settings { DepartmentIds = new() { 11, 1, 21 } };
    var configs = new (string Name, (int, int)[] Screens)[]
    {
        ("2 x 1920x1080 (your desk)", new[] { (1920, 1080), (1920, 1080) }),
        ("3 x 1920x1080", new[] { (1920, 1080), (1920, 1080), (1920, 1080) }),
        ("landscape + portrait", new[] { (2560, 1440), (1080, 1920) }),
    };
    foreach (var (name, screens) in configs)
    {
        var sets = Pairing.Find(cat, screens, s, _ => false);
        Console.WriteLine();
        Console.WriteLine($"== {name}: {sets.Count(x => x.Kind == PairKind.SameArtist)} same-artist sets, {sets.Count(x => x.Kind == PairKind.SameEra)} same-era sets");
        foreach (var set in sets.Where(x => x.Kind == PairKind.SameArtist).Take(3).Concat(sets.Where(x => x.Kind == PairKind.SameEra).Take(3)))
            Console.WriteLine($"  {set.Kind,-10} {set.Theme,-28} score {set.Score:0.00}: " +
                string.Join(" | ", set.Works.Select(w => $"{w.Id} {w.Title[..Math.Min(34, w.Title.Length)]} ({w.Date}, {w.WCm:0}x{w.HCm:0})")));
    }
    return;
}

// Preferred: build from the Met's Open Access CSV (no API crawling).  --csv path/to/MetObjects.csv
if (args.Contains("--csv"))
{
    var csvPath = args[Array.IndexOf(args, "--csv") + 1];
    var sw = Stopwatch.StartNew();
    var imported = MetSaver.CatalogBuilder.CsvImport.Import(csvPath, new Settings { MinSideCm = 20 }, out var why);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
    using (var f = File.Create(output)) new Catalog(DateTime.UtcNow, imported).Save(f);
    Console.WriteLine($"wrote {output}: {imported.Count} paintings, {new FileInfo(output).Length / 1024} KB ({sw.Elapsed.TotalSeconds:0}s)");
    foreach (var d in imported.GroupBy(e => e.Dept)) Console.WriteLine($"  dept {d.Key}: {d.Count()}");
    foreach (var kv in why.OrderByDescending(k => k.Value).Take(8)) Console.WriteLine($"  rejected {kv.Value,5}  {kv.Key}");
    return;
}
int limit = args.Contains("--limit") ? int.Parse(args[Array.IndexOf(args, "--limit") + 1]) : int.MaxValue;

// Departments: 11 European Paintings, 1 American Paintings, 21 Modern & Contemporary.
int[] departments = { 11, 1, 21 };
// Broad terms; the union of their results covers most paintings. Duplicates are merged.
string[] terms =
{
    "oil on canvas", "oil on wood", "oil on panel", "tempera", "acrylic", "watercolor",
    "landscape", "portrait", "still life", "figures", "sea", "flowers",
};

// The API crawl below is slow and the Met throttles it, so it only runs when asked for explicitly.
if (!args.Contains("--crawl"))
{
    Console.WriteLine("usage:");
    Console.WriteLine("  CatalogBuilder <out.json.gz> --csv MetObjects.csv   build the catalog from the Met Open Access CSV (recommended, ~3 s)");
    Console.WriteLine("  CatalogBuilder <out.json.gz> --crawl [--limit N]    build by crawling the API (slow; throttled by the Met)");
    Console.WriteLine("  CatalogBuilder --pairs <catalog.json.gz>            experiment: related paintings across several monitors");
    return;
}

var settings = new Settings { MinSideCm = 20 };          // screen-independent vetting only
using var met = new MetClient();
var ct = CancellationToken.None;
var watch = Stopwatch.StartNew();
var pace = new Pacer(TimeSpan.FromMilliseconds(125));          // objects and images
var searchPace = new Pacer(TimeSpan.FromMilliseconds(1100));   // search is Elastic-backed and throttled harder

/// <summary>Run one HTTP call at a polite pace, retrying with growing delays when the server pushes back.</summary>
async Task<T?> Polite<T>(Func<Task<T>> call, string what, Pacer? pacer = null)
{
    int[] waits = { 5, 20, 60 };
    for (int attempt = 0; ; attempt++)
    {
        await (pacer ?? pace).WaitTurnAsync();
        try { return await call(); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            bool notFound = e is HttpRequestException { StatusCode: System.Net.HttpStatusCode.NotFound };
            if (notFound || attempt >= waits.Length) { Console.Error.WriteLine($"{what}: {e.Message}"); return default; }
            Console.Error.WriteLine($"{what}: {e.Message} - waiting {waits[attempt]}s");
            await Task.Delay(TimeSpan.FromSeconds(waits[attempt]));
        }
    }
}

// 1. Gather candidate ids per department.
var idDept = new ConcurrentDictionary<int, int>();
foreach (var dept in departments)
{
    foreach (var term in terms)
    {
        var found = await Polite(() => met.SearchAllAsync(term, dept, ct, TimeSpan.FromMilliseconds(1100)), $"search '{term}' dept {dept}", searchPace);
        foreach (var id in found ?? new()) idDept.TryAdd(id, dept);
    }
    Console.WriteLine($"dept {dept}: {idDept.Count(kv => kv.Value == dept)} candidate ids ({watch.Elapsed.TotalSeconds:0}s)");
}

// 2. Fetch + vet each object, then peek at the image header.
var ids = idDept.Keys.OrderBy(i => i).Take(limit).ToList();
var entries = new ConcurrentBag<CatalogEntry>();
int done = 0, skipped = 0, failed = 0;
using var gate = new SemaphoreSlim(3);
await Task.WhenAll(ids.Select(async id =>
{
    await gate.WaitAsync();
    try
    {
        var obj = await Polite(() => met.GetObjectAsync(id, ct), $"object {id}");
        if (obj is null) { Interlocked.Increment(ref failed); return; }
        if (!Vetting.VetWork(obj, settings).Ok) { Interlocked.Increment(ref skipped); return; }
        Vetting.TryGetSizeCm(obj, out var w, out var h);

        int pw = 0, ph = 0; long bytes = 0;
        await Polite<byte[]?>(() => met.DownloadImageAsync(obj.PrimaryImage!, (iw, ih) => { pw = iw; ph = ih; return false; },
            capBytes: 200L * 1024 * 1024, ct, (_, total) => { if (total.HasValue) bytes = total.Value; }), $"image {id}");
        if (pw == 0 && bytes == 0) { Interlocked.Increment(ref failed); return; }   // image unreachable: don't catalogue it

        entries.Add(new CatalogEntry(id, obj.Title ?? "", obj.ArtistDisplayName ?? "", obj.ObjectDate ?? "",
            obj.Medium ?? "", idDept[id], obj.PrimaryImage!, Math.Round(w, 1), Math.Round(h, 1), pw, ph, bytes));
    }
    finally
    {
        gate.Release();
        int n = Interlocked.Increment(ref done);
        if (n % 100 == 0)
        {
            Console.WriteLine($"{n}/{ids.Count} checked, {entries.Count} kept ({watch.Elapsed.TotalSeconds:0}s)");
            if (n % 500 == 0) WriteCatalog(output, entries);   // checkpoint
        }
    }
}));

WriteCatalog(output, entries);
Console.WriteLine($"wrote {output}: {entries.Count} paintings ({new FileInfo(output).Length / 1024} KB), {skipped} skipped, {failed} failed, {watch.Elapsed.TotalSeconds:0}s");

static void WriteCatalog(string path, IEnumerable<CatalogEntry> entries)
{
    var sorted = entries.OrderBy(e => e.Dept).ThenBy(e => e.Id).ToList();
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
    using var f = File.Create(path);
    new Catalog(DateTime.UtcNow, sorted).Save(f);
}

/// <summary>Spaces request starts at least <c>interval</c> apart, across all workers.</summary>
sealed class Pacer
{
    readonly TimeSpan _interval;
    readonly SemaphoreSlim _lock = new(1, 1);
    DateTime _next = DateTime.MinValue;
    public Pacer(TimeSpan interval) => _interval = interval;
    public async Task WaitTurnAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var wait = _next - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait);
            _next = DateTime.UtcNow + _interval;
        }
        finally { _lock.Release(); }
    }
}
