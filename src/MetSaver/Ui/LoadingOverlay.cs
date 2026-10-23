using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace MetSaver;

/// <summary>
/// Shown on an empty screen while the first painting is found: a spinner, what's happening, a real progress bar
/// during the download, and a time estimate. Fades away when the first painting arrives.
/// </summary>
sealed class LoadingOverlay : StackPanel
{
    readonly TextBlock _message, _detail, _eta;
    readonly ProgressBar _bar;
    readonly DateTime _started = DateTime.UtcNow;
    LoadStatus _last = new("Starting…");

    public LoadingOverlay(double screenHeight)
    {
        double u = Math.Max(screenHeight / 66, 11);
        var soft = new SolidColorBrush(Color.FromRgb(214, 208, 192));
        var dim = new SolidColorBrush(Color.FromRgb(150, 145, 132));
        var font = new FontFamily("Segoe UI Variable Display, Segoe UI");
        HorizontalAlignment = HorizontalAlignment.Center; VerticalAlignment = VerticalAlignment.Center;
        IsHitTestVisible = false;

        var rotate = new RotateTransform();
        rotate.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.3)) { RepeatBehavior = RepeatBehavior.Forever });
        Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 20,2 A 18,18 0 1 1 2,20"),   // three-quarter ring
            Stroke = soft, StrokeThickness = Math.Max(2, u / 7), StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Width = 40, Height = 40, Stretch = Stretch.None,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = rotate,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, u * 1.2),
            Effect = null,
        });

        _message = new TextBlock { FontFamily = font, FontSize = u * 1.5, Foreground = soft, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = u * 34, Text = "Starting…" };
        _detail = new TextBlock { FontFamily = font, FontSize = u * 1.1, Foreground = dim, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, u * 0.3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = u * 34 };
        _bar = new ProgressBar
        {
            Width = u * 22, Height = Math.Max(3, u / 6), Minimum = 0, Maximum = 1, IsIndeterminate = true,
            Foreground = soft, Background = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)), BorderThickness = new Thickness(0),
            Margin = new Thickness(0, u * 1.0, 0, u * 0.6),
        };
        _eta = new TextBlock { FontFamily = font, FontSize = u * 1.0, Foreground = dim, TextAlignment = TextAlignment.Center };
        Children.Add(_message); Children.Add(_detail); Children.Add(_bar); Children.Add(_eta);

        // Keep the "N s so far" line ticking even when the loader has nothing new to say.
        var tick = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        tick.Tick += (_, _) => Update(_last);
        Loaded += (_, _) => tick.Start();
        Unloaded += (_, _) => tick.Stop();
    }

    public void Update(LoadStatus st)
    {
        _last = st;
        _message.Text = st.Message;
        _detail.Text = st.Detail ?? "";
        _detail.Visibility = string.IsNullOrEmpty(st.Detail) ? Visibility.Collapsed : Visibility.Visible;
        if (st.Fraction is { } f) { _bar.IsIndeterminate = false; _bar.Value = Math.Clamp(f, 0, 1); }
        else _bar.IsIndeterminate = true;

        var elapsed = (int)(DateTime.UtcNow - _started).TotalSeconds;
        _eta.Text = st.Eta is { } eta
            ? $"About {Math.Max(1, (int)Math.Ceiling(eta.TotalSeconds))} s left"
            : elapsed < 4 ? "" : $"{elapsed} s so far · first painting usually takes 10–30 s";
    }
}
