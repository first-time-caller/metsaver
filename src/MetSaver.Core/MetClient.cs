using System.Net.Http.Json;
using System.Text.Json;

namespace MetSaver;

/// <summary>Thin client for The Met's Open Access API (https://metmuseum.github.io/).</summary>
public sealed class MetClient : IDisposable
{
    const string Api = "https://collectionapi.metmuseum.org/public/collection/v1";
    // v1/search was retired on 2026-10-01; v1.1 is paginated with offset/limit.
    const string Search = "https://collectionapi.metmuseum.org/public/collection/v1.1/search";

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    readonly HttpClient _http;

    public MetClient(HttpMessageHandler? handler = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("MetSaver/1.0 (personal screensaver; Met Open Access API)");
    }

    sealed record SearchResult(int Total, List<int>? ObjectIDs);

    /// <summary>A random page of up to 100 matching painting ids for the given theme and department.</summary>
    public async Task<List<int>> SearchAsync(string term, int departmentId, CancellationToken ct)
    {
        var q = $"?q={Uri.EscapeDataString(term)}&departmentId={departmentId}&hasImages=true&isPublicDomain=true&medium=Paintings";
        var head = await _http.GetFromJsonAsync<SearchResult>($"{Search}{q}&limit=1", Json, ct);
        int total = head?.Total ?? 0;
        int offset = total > 100 ? Random.Shared.Next(0, total - 100 + 1) : 0;
        var page = await _http.GetFromJsonAsync<SearchResult>($"{Search}{q}&limit=100&offset={offset}", Json, ct);
        return page?.ObjectIDs ?? new();
    }

    /// <summary>Every matching painting id for a term and department (pages of 100). Used by the catalog builder.</summary>
    public async Task<List<int>> SearchAllAsync(string term, int departmentId, CancellationToken ct, TimeSpan? pageDelay = null)
    {
        var q = $"?q={Uri.EscapeDataString(term)}&departmentId={departmentId}&hasImages=true&isPublicDomain=true&medium=Paintings";
        var all = new List<int>();
        for (int offset = 0; ; offset += 100)
        {
            if (pageDelay is { } d && offset > 0) await Task.Delay(d, ct);
            var page = await _http.GetFromJsonAsync<SearchResult>($"{Search}{q}&limit=100&offset={offset}", Json, ct);
            if (page?.ObjectIDs is not { Count: > 0 } ids) break;
            all.AddRange(ids);
            if (offset + 100 >= page.Total) break;
        }
        return all;
    }

    public Task<MetObject?> GetObjectAsync(int id, CancellationToken ct) =>
        _http.GetFromJsonAsync<MetObject>($"{Api}/objects/{id}", Json, ct);

    /// <summary>
    /// Streams an image. Once the file header reveals the pixel size, <paramref name="accept"/> decides whether
    /// to carry on; returning false abandons the download early. Returns null if rejected or over the size cap.
    /// </summary>
    public async Task<byte[]?> DownloadImageAsync(string url, Func<int, int, bool> accept, long capBytes, CancellationToken ct,
        Action<long, long?>? onProgress = null)
    {
        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        if (resp.Content.Headers.ContentLength > capBytes) return null;

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        var ms = new MemoryStream();
        var buf = new byte[64 * 1024];
        bool sized = false;
        int n;
        while ((n = await stream.ReadAsync(buf, ct)) > 0)
        {
            ms.Write(buf, 0, n);
            onProgress?.Invoke(ms.Length, resp.Content.Headers.ContentLength);
            if (ms.Length > capBytes) return null;
            if (!sized && ms.Length <= 2 * 1024 * 1024
                && ImageHeader.TryGetSize(ms.GetBuffer().AsSpan(0, (int)ms.Length), out var w, out var h))
            {
                Log.Info($"     header says {w}x{h}px");
                if (!accept(w, h)) return null;
                sized = true;
            }
        }
        return ms.ToArray();
    }

    public void Dispose() => _http.Dispose();
}
