using System.Threading.Channels;
using System.Windows;

namespace MetSaver;

enum SaverMode
{
    /// <summary>The real screensaver: any input exits.</summary>
    Saver,
    /// <summary>Same, but rotates quickly so you can see a whole cycle in under a minute.</summary>
    Test,
    /// <summary>Full-screen gallery you drive from the keyboard (I details, F favourite, O open, N next, Esc exit).</summary>
    Gallery,
}

/// <summary>
/// Runs the show: one window per monitor, and per monitor a background producer that keeps the next two
/// paintings fully prepared so the crossfade never waits on the network.
/// </summary>
sealed class SaverRunner
{
    readonly Settings _s;
    readonly SaverMode _mode;
    readonly CancellationTokenSource _cts = new();
    Curator? _curator;
    Ambient? _ambient;
    readonly Preferences _prefs = new();

    public SaverRunner(Settings s, SaverMode mode)
    {
        _mode = mode;
        if (mode == SaverMode.Test)
        {
            s = s.Clone();
            s.SecondsPerPainting = 20; s.InfoSeconds = 8;
        }
        _s = s;
    }

    public void Start()
    {
        _curator = new Curator(_s, _prefs);
        _ambient = new Ambient(_s);
        var monitors = Native.GetMonitors();
        Log.Info($"starting {_mode} on {monitors.Count} monitor(s): {string.Join(", ", monitors.Select(m => $"{m.Width}x{m.Height}"))}");
        Application.Current.Exit += (_, _) => { _cts.Cancel(); _ambient?.Dispose(); };
        foreach (var m in monitors)
        {
            var window = new ScreenWindow(m, _s, _mode == SaverMode.Test, _ambient, _prefs, _mode == SaverMode.Gallery);
            window.Show();
            _ = RunScreenAsync(window, m);
        }
    }

    async Task RunScreenAsync(ScreenWindow window, Native.Monitor mon)
    {
        var ct = _cts.Token;
        var queue = Channel.CreateBounded<Prepared>(2);   // prefetch depth
        _ = Task.Run(async () =>
        {
            bool first = true;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    Artwork? art = null;
                    if (first) { first = false; art = Cache.Pick(); }          // something on screen straight away
                    art ??= await _curator!.NextAsync(mon.Width, mon.Height, ct,
                        st => window.Dispatcher.BeginInvoke(() => window.SetStatus(st))) ?? Cache.Pick();
                    if (art is null) { await Task.Delay(10_000, ct); continue; }
                    await queue.Writer.WriteAsync(ImagePrep.Prepare(art, _s, mon.Width, mon.Height), ct);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception e) { Log.Error(e, "producer"); await Task.Delay(5_000, CancellationToken.None); }
            }
        }, ct);

        try
        {
            await window.Ready;
            while (!ct.IsCancellationRequested)
            {
                var next = await queue.Reader.ReadAsync(ct);
                window.ShowArt(next);
                // Wait out the painting's time, or until the viewer presses N in gallery mode.
                await Task.WhenAny(Task.Delay(TimeSpan.FromSeconds(_s.SecondsPerPainting), ct), window.NextRequested);
                window.ResetNext();
            }
        }
        catch (OperationCanceledException) { }
    }
}
