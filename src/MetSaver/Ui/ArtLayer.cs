using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace MetSaver;

/// <summary>
/// One painting on one screen: the picture (with a slow zoom/pan driven by WPF's GPU animation clock)
/// plus a frosted-glass info card that fades in, then out. Crossfading is done by fading whole layers.
/// </summary>
sealed class ArtLayer : Grid
{
    public const double CrossfadeSeconds = 2.0;

    readonly Prepared _art;
    readonly Settings _s;
    readonly double _w, _h;
    readonly Motion _motion;
    GlassCard? _card, _details;

    public ArtLayer(Prepared art, Settings s, double w, double h)
    {
        _art = art; _s = s; _w = w; _h = h;
        Width = w; Height = h; ClipToBounds = true; IsHitTestVisible = false;
        _motion = Motion.Plan(s, w, h);
        Children.Add(BuildPicture(art, s, w, h, _motion));
        if (s.ShowCaption && art.Caption is not null)
        {
            _card = new GlassCard(art, s, w, h, () => BuildPicture(art, s, w, h, _motion));
            Children.Add(_card);
        }
    }

    public bool DetailsOpen => _details is not null;

    /// <summary>Gallery mode: replace the small card with the full details panel (bio, credit line, gallery, hotkeys).</summary>
    public void ShowDetails(bool favourite)
    {
        if (_art.Caption is null) return;
        HideDetails();
        _card?.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromSeconds(0.2)));
        _details = new GlassCard(_art, _s, _w, _h, () => BuildPicture(_art, _s, _w, _h, _motion), details: true, favourite: favourite);
        Children.Add(_details);
    }

    public void HideDetails()
    {
        if (_details is null) return;
        Children.Remove(_details);
        _details = null;
        _card?.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromSeconds(0.2)));
    }

    /// <summary>The picture plus its animated transform. Called twice when a card is shown (the card blurs a synced copy).</summary>
    static FrameworkElement BuildPicture(Prepared art, Settings s, double w, double h, Motion m)
    {
        var image = new Image
        {
            Source = art.Image,
            Stretch = s.Cover ? Stretch.UniformToFill : Stretch.Uniform,
            Margin = s.Cover ? new Thickness(0) : new Thickness(w * 0.035, h * 0.035, w * 0.035, h * 0.035),
        };
        var scale = new ScaleTransform(m.ScaleFrom, m.ScaleFrom);
        var move = new TranslateTransform(m.XFrom, m.YFrom);
        m.Attach(scale, move);
        return new Grid
        {
            Width = w, Height = h,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new TransformGroup { Children = { scale, move } },
            Children = { image },
        };
    }

    /// <summary>Zoom and pan over the painting's time on screen; the animation clocks are shared so copies stay in sync.</summary>
    sealed class Motion
    {
        public double ScaleFrom, XFrom, YFrom;
        AnimationClock? _scale, _x, _y;

        public static Motion Plan(Settings s, double w, double h)
        {
            var m = new Motion { ScaleFrom = 1 };
            double drift = s.Drift;
            if (drift <= 0) return m;
            bool zoomIn = s.Motion != "ken_burns" || Random.Shared.Next(2) == 0;
            double sA = zoomIn ? 1 : 1 + drift, sB = zoomIn ? 1 + drift : 1;
            double dx = Random.Shared.Next(2) == 0 ? -1 : 1, dy = Random.Shared.Next(2) == 0 ? -0.5 : 0.5;
            // Pan stays inside the zoom's spare room: (scale - 1) / 2 of the screen, at 90%.
            double Room(double sc, double size) => (sc - 1) / 2 * size * 0.9;
            m.ScaleFrom = sA; m.XFrom = dx * Room(sA, w); m.YFrom = dy * Room(sA, h);
            var dur = new Duration(TimeSpan.FromSeconds(s.SecondsPerPainting + CrossfadeSeconds + 1));
            m._scale = new DoubleAnimation(sA, sB, dur).CreateClock();
            m._x = new DoubleAnimation(m.XFrom, -dx * Room(sB, w), dur).CreateClock();
            m._y = new DoubleAnimation(m.YFrom, -dy * Room(sB, h), dur).CreateClock();
            return m;
        }

        public void Attach(ScaleTransform scale, TranslateTransform move)
        {
            if (_scale is null) return;
            scale.ApplyAnimationClock(ScaleTransform.ScaleXProperty, _scale);
            scale.ApplyAnimationClock(ScaleTransform.ScaleYProperty, _scale);
            move.ApplyAnimationClock(TranslateTransform.XProperty, _x);
            move.ApplyAnimationClock(TranslateTransform.YProperty, _y);
        }
    }
}

/// <summary>Frosted-glass card: a blurred, tinted copy of the picture behind the title, artist, date and medium.</summary>
sealed class GlassCard : Border
{
    public GlassCard(Prepared art, Settings s, double w, double h, Func<FrameworkElement> makePictureCopy,
        bool details = false, bool favourite = false)
    {
        var c = art.Caption!;
        double u = Math.Max(h / 66, 11);
        double pad = u * 1.4, cardW = details ? Math.Clamp(w * 0.36, 380, 800) : Math.Clamp(w * 0.27, 280, 640);
        double margin = u * 2.2, radius = u * 1.2;
        var tint = art.Tint;
        var accent = Color.FromRgb((byte)Math.Min(255, tint.R * 0.5 + 140), (byte)Math.Min(255, tint.G * 0.5 + 140), (byte)Math.Min(255, tint.B * 0.5 + 140));
        var font = new FontFamily("Segoe UI Variable Display, Segoe UI");

        TextBlock Line(string text, double size, Brush color, FontWeight weight, int maxLines = 3) => new()
        {
            Text = text, FontFamily = font, FontSize = size, Foreground = color, FontWeight = weight,
            TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = maxLines * size * 1.45,
        };
        var text = new StackPanel { Margin = new Thickness(pad), Width = cardW - 2 * pad };
        text.Children.Add(Line(c.Title, u * 1.5, Brushes.White, FontWeights.SemiBold));
        text.Children.Add(new Rectangle { Width = u * 3, Height = Math.Max(2, u / 7), Fill = new SolidColorBrush(accent), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, u * 0.35, 0, u * 0.35) });
        text.Children.Add(Line(c.Artist, u * 1.1, new SolidColorBrush(Color.FromRgb(235, 235, 235)), FontWeights.Normal, 2));
        var soft = new SolidColorBrush(Color.FromRgb(205, 205, 205));
        var faint = new SolidColorBrush(Color.FromRgb(185, 185, 185));
        if (details && !string.IsNullOrWhiteSpace(c.Bio)) text.Children.Add(Line(c.Bio, u * 1.0, faint, FontWeights.Normal, 2));
        var detail = string.Join(" · ", new[] { c.Date, c.Medium }.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (detail.Length > 0) text.Children.Add(Line(detail, u * 1.1, soft, FontWeights.Normal, details ? 4 : 3));
        if (details)
        {
            void Fact(string label, string? value) { if (!string.IsNullOrWhiteSpace(value)) text.Children.Add(Line($"{label}  {value}", u * 1.0, faint, FontWeights.Normal, 3)); }
            Fact("Size", c.Dimensions);
            Fact("Credit", c.Credit);
            text.Children.Add(Line(string.IsNullOrWhiteSpace(c.Gallery) ? "Not currently on view" : $"On view in Gallery {c.Gallery}", u * 1.0, soft, FontWeights.Normal, 1));
        }
        text.Children.Add(Line("The Metropolitan Museum of Art · Public domain", u * 1.1, faint, FontWeights.Normal, 2));
        if (details)
        {
            var hint = new SolidColorBrush(Color.FromRgb(235, 225, 190));
            text.Children.Add(new Rectangle { Height = 1, Fill = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), Margin = new Thickness(0, u * 0.6, 0, u * 0.6) });
            text.Children.Add(Line(favourite ? "★ In your favourites" : "☆ Not a favourite yet", u * 1.1, hint, FontWeights.SemiBold, 1));
            text.Children.Add(Line("F favourite   O open on metmuseum.org   N next   I close   Esc exit", u * 0.95, faint, FontWeights.Normal, 2));
        }

        text.Measure(new Size(cardW - 2 * pad, double.PositiveInfinity));
        double cardH = text.DesiredSize.Height;   // already includes the panel's own margin (the padding)
        bool left = s.InfoCorner.Contains("left"), top = s.InfoCorner.Contains("top");
        double cx = left ? margin : w - margin - cardW, cy = top ? margin : h - margin - cardH;

        // Blurred backdrop: a synced copy of the picture, shifted so it lines up with the screen behind the card.
        double bleed = u * 4;  // extra around the card so the blur doesn't fade at its edges
        var copy = makePictureCopy();
        copy.HorizontalAlignment = HorizontalAlignment.Left; copy.VerticalAlignment = VerticalAlignment.Top;
        copy.Margin = new Thickness(-cx + bleed, -cy + bleed, 0, 0);
        var backdrop = new Grid
        {
            Width = cardW + 2 * bleed, Height = cardH + 2 * bleed,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(-bleed), ClipToBounds = false,
            Effect = new BlurEffect { Radius = u * 1.8, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance },
            Children = { copy },
        };
        var glass = new Grid
        {
            Width = cardW, Height = cardH, Clip = new RectangleGeometry(new Rect(0, 0, cardW, cardH), radius, radius),
            Children =
            {
                backdrop,
                new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(155, tint.R, tint.G, tint.B)) },
                new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)) },
                text,
            },
        };

        Width = cardW; Height = cardH; CornerRadius = new CornerRadius(radius);
        BorderBrush = new SolidColorBrush(Color.FromArgb(120, accent.R, accent.G, accent.B)); BorderThickness = new Thickness(Math.Max(1, u / 10));
        Child = glass; Opacity = 0; IsHitTestVisible = false;
        HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        Margin = new Thickness(left ? margin : 0, top ? margin : 0, left ? 0 : margin, top ? 0 : margin);

        if (details) { BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.25))); return; }

        // Fade in at 1.5s, hold, fade out after InfoSeconds (0 = stay).
        var fade = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.HoldEnd };
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.5))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.5))));
        if (s.InfoSeconds > 0)
        {
            double hold = Math.Max(3, s.InfoSeconds);
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(hold))));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(hold + 1.5))));
        }
        BeginAnimation(OpacityProperty, fade);
    }
}
