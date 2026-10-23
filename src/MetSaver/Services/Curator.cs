using System.Diagnostics;
using System.Text.Json;

namespace MetSaver;

/// <summary>
/// Chooses paintings: searches the Met at random, vets candidates from metadata (eight at a time), downloads only
/// the ones that suit the screen, and remembers what it has shown so you don't see repeats.
/// </summary>
public sealed class Curator : IDisposable
{
    const int HistoryKeep = 4000;
    const int Batch = 8;
    readonly Settings _s;
    readonly MetClient _met = new();
    readonly SemaphoreSlim _gate = new(1, 1);
    readonly Queue<int> _ids = new();
    readonly HashSet<int> _seen;
    readonly Catalog? _catalog;
    readonly Preferences? _prefs;

    public Curator(Settings s, Preferences? prefs = null)
    {
        _s = s;
        _prefs = prefs;
        _catalog = CatalogSource.Load();
        _ = CatalogSource.RefreshAsync(s, _catalog);
        try { _seen = JsonSerializer.Deserialize<HashSet<int>>(File.ReadAllText(Paths.HistoryFile)) ?? new(); }
        catch { _seen = new(); }
    }

    public void ClearHistory() { lock (_seen) _seen.Clear(); try { File.Delete(Paths.HistoryFile); } catch { } }

    void SaveHistory()
    {
        try
        {
            int[] ids; lock (_seen) ids = _seen.TakeLast(HistoryKeep).ToArray();
            Paths.Ensure();
            File.WriteAllText(Paths.HistoryFile, JsonSerializer.Serialize(ids));
        }
        catch { }
    }

    async Task<int?> PopIdAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            for (int attempt = 0; attempt < 8 && _ids.Count == 0; attempt++)
            {
                if (attempt == 6) { Log.Info("history exhausted - starting over"); lock (_seen) _seen.Clear(); }
                var term = _s.SearchTerms[Random.Shared.Next(_s.SearchTerms.Count)];
                var dept = _s.DepartmentIds[Random.Shared.Next(_s.DepartmentIds.Count)];
                var ids = await _met.SearchAsync(term, dept, ct);
                bool Fresh(int id) { lock (_seen) return !_seen.Contains(id); }
                foreach (var id in ids.Where(Fresh).OrderBy(_ => Random.Shared.Next())) _ids.Enqueue(id);
            }
            if (_ids.Count == 0) return null;
            var next = _ids.Dequeue();
            lock (_seen) _seen.Add(next);
            return next;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Fast path: choose a pre-vetted painting for this screen and download only that one.</summary>
    async Task<Artwork?> NextFromCatalogAsync(int screenW, int screenH, CancellationToken ct, Action<LoadStatus>? report)
    {
        report?.Invoke(new LoadStatus($"Choosing a painting for your {screenW}×{screenH} screen…"));
        for (int tries = 0; tries < 6; tries++)
        {
            CatalogEntry? e;
            lock (_seen) e = _catalog!.Pick(screenW, screenH, _s, id => _seen.Contains(id), extraWeight: _prefs is null ? null : _prefs.Weight);
            if (e is null)
            {
                lock (_seen) { if (_seen.Count == 0) return null; Log.Info("catalog exhausted - starting over"); _seen.Clear(); }
                continue;
            }
            lock (_seen) _seen.Add(e.Id);
            var obj = new MetObject { ObjectID = e.Id, PrimaryImage = e.Url, Title = e.Title, ArtistDisplayName = e.Artist, ObjectDate = e.Date, Medium = e.Medium };
            if (string.IsNullOrEmpty(e.Url))
            {
                // Compact catalog: no image URL stored, so look up just this one object (one cheap request).
                try { obj = await _met.GetObjectAsync(e.Id, ct); }
                catch (Exception ex) when (!ct.IsCancellationRequested) { Log.Error(ex, $"lookup {e.Id}"); continue; }
                if (obj is null || string.IsNullOrEmpty(obj.PrimaryImage) || !obj.IsPublicDomain) { Log.Info($"     [{e.Id}] no public image - next"); continue; }
            }
            Log.Info($"catalog pick [{e.Id}] {e.Title}");
            var art = await TryDownloadAsync(obj, screenW, screenH, ct, report, e.Bytes);
            if (art is not null) return art;
        }
        return null;
    }

    sealed record Vetted(MetObject? Obj, VetResult Result);

    /// <summary>
    /// Find, vet and download one painting for a screen of the given pixel size; null if none found.
    /// <paramref name="report"/> is told what's happening so the UI can show progress.
    /// </summary>
    public async Task<Artwork?> NextAsync(int screenW, int screenH, CancellationToken ct, Action<LoadStatus>? report = null)
    {
        if (_catalog is not null)
        {
            var viaCatalog = await NextFromCatalogAsync(screenW, screenH, ct, report);
            if (viaCatalog is not null) return viaCatalog;
            Log.Info("catalog had nothing suitable - falling back to live search");
        }
        report?.Invoke(new LoadStatus("Searching the Met's collection…"));
        int failures = 0, checkedCount = 0;
        for (int round = 0; round < 40 && failures < 5; round++)
        {
            var batch = new List<int>();
            try
            {
                for (int i = 0; i < Batch; i++)
                {
                    var id = await PopIdAsync(ct);
                    if (id is null) break;
                    batch.Add(id.Value);
                }
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested) { failures++; continue; }
            if (batch.Count == 0) return null;

            var vetted = await Task.WhenAll(batch.Select(async id =>
            {
                try
                {
                    var obj = await _met.GetObjectAsync(id, ct);
                    if (obj is null) return new Vetted(null, default);
                    var r = Vetting.Vet(obj, screenW, screenH, _s);
                    Log.Info($"{(r.Ok ? "ok  " : "skip")} [{id}] {obj.Title} - {r.Reason}");
                    return new Vetted(obj, r);
                }
                catch (Exception e) when (!ct.IsCancellationRequested)
                {
                    Interlocked.Increment(ref failures);
                    Log.Error(e, $"object {id}");
                    return new Vetted(null, default);
                }
            }));
            checkedCount += batch.Count;
            report?.Invoke(new LoadStatus($"Looking for a painting that fits your {screenW}×{screenH} screen",
                Detail: $"Checked {checkedCount} so far"));

            foreach (var v in vetted.Where(v => v.Obj is not null && v.Result.Ok))
            {
                var art = await TryDownloadAsync(v.Obj!, screenW, screenH, ct, report);
                if (art is not null) return art;
            }
        }
        return null;
    }

    async Task<Artwork?> TryDownloadAsync(MetObject obj, int screenW, int screenH, CancellationToken ct, Action<LoadStatus>? report, long expectedBytes = 0)
    {
        try
        {
            var watch = Stopwatch.StartNew();
            long lastReport = 0;
            string title = obj.Title ?? "painting", artist = obj.ArtistDisplayName ?? "";
            report?.Invoke(new LoadStatus($"Downloading “{title}”", Detail: artist));

            var bytes = await _met.DownloadImageAsync(obj.PrimaryImage!,
                (w, h) => !Vetting.TooSmall(w, h, screenW, screenH, _s),
                _s.MaxDownloadMb * 1024L * 1024L, ct,
                (got, total) =>
                {
                    if (report is null || watch.ElapsedMilliseconds - lastReport < 100) return;
                    lastReport = watch.ElapsedMilliseconds;
                    total ??= expectedBytes > 0 ? expectedBytes : null;
                    double? frac = total > 0 ? (double)got / total : null;
                    TimeSpan? eta = null;
                    if (total > 0 && got > 128 * 1024 && watch.Elapsed.TotalSeconds > 0.4)
                        eta = TimeSpan.FromSeconds(watch.Elapsed.TotalSeconds * (total.Value - got) / got);
                    report(new LoadStatus($"Downloading “{title}”", frac, eta, artist));
                });
            if (bytes is null) { Log.Info("     -> rejected before full download"); return null; }

            report?.Invoke(new LoadStatus("Preparing the painting…", Detail: obj.Title));
            var art = new Artwork(obj.ObjectID, Caption.From(obj), ImagePrep.DownscaleToJpeg(bytes, _s.MaxWidth, _s.MaxHeight));
            Cache.Save(art);
            SaveHistory();
            return art;
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            Log.Error(e, $"download {obj.ObjectID}");
            return null;
        }
    }

    public void Dispose() => _met.Dispose();
}
