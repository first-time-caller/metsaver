using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace MetSaver;

/// <summary>
/// A borderless full-screen window covering one monitor that crossfades paintings. As a screensaver any input exits;
/// in gallery mode only Esc does, and the keyboard drives it (I details, F favourite, O open on the Met's site, N next).
/// </summary>
sealed class ScreenWindow : Window
{
    readonly Native.Monitor _mon;
    readonly Settings _s;
    readonly Ambient? _ambient;
    readonly Preferences? _prefs;
    readonly bool _gallery;
    readonly Grid _stage = new();
    readonly Rectangle _dim = new() { Fill = Brushes.Black, Opacity = 0, IsHitTestVisible = false };
    readonly TaskCompletionSource _ready = new();
    readonly DateTime _started = DateTime.UtcNow;
    Native.POINT _origin;
    LoadingOverlay? _loading;
    ArtLayer? _current;
    TaskCompletionSource _next = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Ready => _ready.Task;
    public Prepared? Current { get; private set; }
    /// <summary>Completes when the viewer asks for the next painting (gallery mode).</summary>
    public Task NextRequested => _next.Task;
    public void ResetNext() => _next = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ScreenWindow(Native.Monitor mon, Settings s, bool test, Ambient? ambient = null, Preferences? prefs = null, bool gallery = false)
    {
        _mon = mon; _s = s; _ambient = ambient; _prefs = prefs; _gallery = gallery;
        var mat = (Color)ColorConverter.ConvertFromString(s.MatColor);
        Background = new SolidColorBrush(mat);
        _stage.Background = Background;
        var root = new Grid();
        root.Children.Add(_stage);
        root.Children.Add(_dim);                   // ambient dimming sits over everything
        Content = root;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Title = "MetSaver";
        Cursor = Cursors.None;
        UseLayoutRounding = true;

        SourceInitialized += (_, _) => Place();
        DpiChanged += (_, _) => Place();
        // Layout can finish before or after Loaded, so check now and on every size change.
        _stage.SizeChanged += (_, _) => InitStage();
        Loaded += (_, _) => InitStage();

        if (ambient is not null)
        {
            ambient.Changed += ApplyDim;
            Loaded += (_, _) => ApplyDim();
        }
        if (test) ScheduleSnapshots();

        if (gallery)
        {
            PreviewKeyDown += OnGalleryKey;
            return;                                // no exit-on-input: Esc only
        }
        Native.GetCursorPos(out _origin);
        PreviewKeyDown += (_, _) => Quit();
        PreviewMouseDown += (_, _) => Quit();
        PreviewMouseWheel += (_, _) => Quit();
        PreviewTouchDown += (_, _) => Quit();
        var poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        poll.Tick += (_, _) =>
        {
            Native.GetCursorPos(out var p);
            if (Math.Abs(p.X - _origin.X) + Math.Abs(p.Y - _origin.Y) > 20) Quit();
        };
        poll.Start();
    }

    void ApplyDim()
    {
        double target = _ambient is null ? 0 : 1 - _ambient.Dim;
        _dim.BeginAnimation(OpacityProperty, new DoubleAnimation(target, TimeSpan.FromSeconds(3)));   // slow, so it's never noticed
    }

    void OnGalleryKey(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: Application.Current.Shutdown(); break;
            case Key.I:
                if (_current is null) break;
                if (_current.DetailsOpen) _current.HideDetails();
                else _current.ShowDetails(IsFavourite());
                break;
            case Key.F:
                if (Current is null || _prefs is null) break;
                _prefs.Toggle(Current.Id);
                if (_current?.DetailsOpen == true) _current.ShowDetails(IsFavourite());   // refresh the star
                break;
            case Key.O:
                var url = Current?.Caption?.Url;
                if (url is not null && url.StartsWith("https://www.metmuseum.org/", StringComparison.OrdinalIgnoreCase))
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                break;
            case Key.N: case Key.Right: case Key.Space:
                _next.TrySetResult();
                break;
        }
        e.Handled = true;
    }

    bool IsFavourite() => Current is not null && _prefs?.IsFavourite(Current.Id) == true;

    /// <summary>
    /// Test aid: METSAVER_SNAP="C:\some\dir|3,8,15" makes the window render itself to PNG files at those seconds,
    /// so the UI can be inspected without screen capture.
    /// </summary>
    void ScheduleSnapshots()
    {
        var spec = Environment.GetEnvironmentVariable("METSAVER_SNAP");
        if (string.IsNullOrWhiteSpace(spec) || !spec.Contains('|')) return;
        var parts = spec.Split('|');
        Directory.CreateDirectory(parts[0]);
        foreach (var t in parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            bool openDetails = t.StartsWith('d');                       // "d16" = open the details panel, then capture at 16 s
            if (!double.TryParse(t.TrimStart('d'), out var seconds)) continue;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            timer.Tick += async (_, _) =>
            {
                timer.Stop();
                if (openDetails) { _current?.ShowDetails(false); await Task.Delay(500); }
                var w = (int)_stage.ActualWidth; var h = (int)_stage.ActualHeight;
                if (w <= 0 || h <= 0) return;
                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(_stage);
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                using var f = File.Create(System.IO.Path.Combine(parts[0], $"{(openDetails ? "details" : "t")}{seconds:0}.png"));
                enc.Save(f);
            };
            timer.Start();
        }
    }

    /// <summary>Position in physical pixels (monitor coordinates), independent of per-monitor DPI scaling.</summary>
    void Place()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, _mon.X, _mon.Y, _mon.Width, _mon.Height, Native.SWP_SHOWWINDOW);
    }

    void Quit()
    {
        if (Environment.GetEnvironmentVariable("METSAVER_NOEXIT") == "1") return;   // test hook: unattended runs ignore input
        if ((DateTime.UtcNow - _started).TotalSeconds < 1) return;  // ignore the input burst that launched us
        Application.Current.Shutdown();
    }

    void InitStage()
    {
        if (_stage.ActualWidth <= 0 || _stage.ActualHeight <= 0 || _ready.Task.IsCompleted) return;
        _loading = new LoadingOverlay(_stage.ActualHeight);   // visible until the first painting arrives
        _stage.Children.Add(_loading);
        _ready.TrySetResult();
    }

    /// <summary>Progress from the loader; only shown while the screen is still empty.</summary>
    public void SetStatus(LoadStatus st) => _loading?.Update(st);

    /// <summary>On battery, motion is dialled down a notch (Ken Burns becomes a subtle drift, a drift becomes still).</summary>
    Settings EffectiveSettings()
    {
        if (_ambient?.OnBattery != true || _s.Motion == "off") return _s;
        var calmer = _s.Clone();
        calmer.Motion = _s.Motion == "ken_burns" ? "subtle" : "off";
        return calmer;
    }

    public void ShowArt(Prepared art)
    {
        if (_loading is { } overlay)
        {
            _loading = null;
            var gone = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(ArtLayer.CrossfadeSeconds));
            gone.Completed += (_, _) => _stage.Children.Remove(overlay);
            overlay.BeginAnimation(OpacityProperty, gone);
        }
        var layer = new ArtLayer(art, EffectiveSettings(), _stage.ActualWidth, _stage.ActualHeight) { Opacity = 0 };
        _stage.Children.Add(layer);
        _current = layer;
        Current = art;
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(ArtLayer.CrossfadeSeconds))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        fade.Completed += (_, _) =>
        {
            layer.BeginAnimation(OpacityProperty, null);
            layer.Opacity = 1;
            for (int i = _stage.Children.IndexOf(layer) - 1; i >= 0; i--) if (_stage.Children[i] is ArtLayer) _stage.Children.RemoveAt(i);  // drop old paintings underneath
        };
        layer.BeginAnimation(OpacityProperty, fade);
    }
}
