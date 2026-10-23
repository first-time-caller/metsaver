using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MetSaver;

/// <summary>A painting ready to display: border-trimmed bitmap, dominant colour, caption. Safe to build off the UI thread.</summary>
public sealed record Prepared(BitmapSource Image, Color Tint, Caption? Caption, int Id = 0);

static class ImagePrep
{
    /// <summary>Shrink to fit within maxW x maxH (the "2K" cap) and re-encode as JPEG for the cache.</summary>
    public static byte[] DownscaleToJpeg(byte[] data, int maxW, int maxH)
    {
        var bmp = new BitmapImage();
        using (var ms = new MemoryStream(data))
        {
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
        }
        double scale = Math.Min(1.0, Math.Min((double)maxW / bmp.PixelWidth, (double)maxH / bmp.PixelHeight));
        BitmapSource src = bmp;
        if (scale < 1.0) src = new TransformedBitmap(bmp, new ScaleTransform(scale, scale));
        var enc = new JpegBitmapEncoder { QualityLevel = 92 };
        enc.Frames.Add(BitmapFrame.Create(src));
        using var outMs = new MemoryStream();
        enc.Save(outMs);
        return outMs.ToArray();
    }

    static BitmapImage Decode(byte[] jpeg, int decodeWidth = 0)
    {
        var bmp = new BitmapImage();
        using var ms = new MemoryStream(jpeg);
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = ms;
        if (decodeWidth > 0) bmp.DecodePixelWidth = decodeWidth;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    /// <param name="screenW">Pixel size of the screen this is for; used to crop toward the interesting part of the painting.</param>
    public static Prepared Prepare(Artwork art, Settings s, int screenW = 0, int screenH = 0)
    {
        var full = Decode(art.Jpeg);
        var small = Decode(art.Jpeg, 500);
        int w = small.PixelWidth, h = small.PixelHeight, stride = w * 4;
        var px = new byte[stride * h];
        new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0).CopyPixels(px, stride, 0);

        var gray = new double[w * h];
        for (int i = 0; i < gray.Length; i++)
            gray[i] = 0.114 * px[i * 4] + 0.587 * px[i * 4 + 1] + 0.299 * px[i * 4 + 2];

        BitmapSource image = TrimBorders(full, gray, w, h);
        if (s.Cover && s.SmartCrop && screenW > 0 && screenH > 0) image = SmartCrop(image, (double)screenW / screenH);
        return new Prepared(image, DominantColor(px), art.Caption, art.Id);
    }

    /// <summary>
    /// "Cover" fitting has to throw some of the picture away. Instead of always trimming equally from both sides, slide the
    /// kept window along the spare axis to where the picture is busiest (edge energy), with a mild pull toward the middle
    /// so a blank sky or a plain wall is the part that gets cut.
    /// </summary>
    static BitmapSource SmartCrop(BitmapSource img, double aspect)
    {
        int W = img.PixelWidth, H = img.PixelHeight;
        double imageAspect = (double)W / H;
        if (Math.Abs(imageAspect - aspect) / aspect < 0.02) return img;
        bool cropX = imageAspect > aspect;                       // too wide: choose which columns to keep; too tall: which rows

        double k = 400.0 / Math.Max(W, H);
        var gray = new FormatConvertedBitmap(new TransformedBitmap(img, new ScaleTransform(k, k)), PixelFormats.Gray8, null, 0);
        int sw = gray.PixelWidth, sh = gray.PixelHeight;
        var px = new byte[sw * sh];
        gray.CopyPixels(px, sw, 0);

        var energy = new double[cropX ? sw : sh];
        for (int y = 1; y < sh; y++)
            for (int x = 1; x < sw; x++)
            {
                double e = Math.Abs(px[y * sw + x] - px[y * sw + x - 1]) + Math.Abs(px[y * sw + x] - px[(y - 1) * sw + x]);
                energy[cropX ? x : y] += e;
            }

        int len = energy.Length;
        int win = cropX ? (int)Math.Round(sh * aspect) : (int)Math.Round(sw / aspect);
        win = Math.Clamp(win, 1, len);
        var prefix = new double[len + 1];
        for (int i = 0; i < len; i++) prefix[i + 1] = prefix[i] + energy[i];

        int best = (len - win) / 2;
        double bestScore = double.MinValue;
        for (int off = 0; off <= len - win; off++)
        {
            double centre = (off + win / 2.0) / len;             // 0..1; 0.5 = dead centre
            double score = (prefix[off + win] - prefix[off]) * (1 - 0.35 * Math.Abs(centre - 0.5) * 2);
            if (score > bestScore) { bestScore = score; best = off; }
        }

        var rect = cropX
            ? new Int32Rect(Math.Clamp((int)Math.Round(best / k), 0, W - 1), 0, Math.Min(W, (int)Math.Round(H * aspect)), H)
            : new Int32Rect(0, Math.Clamp((int)Math.Round(best / k), 0, H - 1), W, Math.Min(H, (int)Math.Round(W / aspect)));
        rect.Width = Math.Min(rect.Width, W - rect.X);
        rect.Height = Math.Min(rect.Height, H - rect.Y);
        if (rect.Width < 16 || rect.Height < 16) return img;
        var cropped = new CroppedBitmap(img, rect);
        cropped.Freeze();
        return cropped;
    }

    /// <summary>Cut near-black or near-white photographic borders (at most 5% per edge).</summary>
    static BitmapSource TrimBorders(BitmapSource full, double[] g, int w, int h)
    {
        bool Uniform(IEnumerable<double> line)
        {
            var a = line.ToArray();
            double mean = a.Average();
            double sd = Math.Sqrt(a.Sum(v => (v - mean) * (v - mean)) / a.Length);
            return sd < 7 && (mean < 40 || mean > 225);
        }
        IEnumerable<double> Col(int x) { for (int y = 0; y < h; y++) yield return g[y * w + x]; }
        IEnumerable<double> Row(int y) { for (int x = 0; x < w; x++) yield return g[y * w + x]; }

        int limX = (int)(w * 0.05), limY = (int)(h * 0.05);
        int l = 0, r = 0, t = 0, b = 0;
        while (l < limX && Uniform(Col(l))) l++;
        while (r < limX && Uniform(Col(w - 1 - r))) r++;
        while (t < limY && Uniform(Row(t))) t++;
        while (b < limY && Uniform(Row(h - 1 - b))) b++;
        if (l + r + t + b == 0) return full;

        double fx = (double)full.PixelWidth / w, fy = (double)full.PixelHeight / h;
        int Pad(int n) => n == 0 ? 0 : n + 2;  // a little extra to clear soft edges
        int x0 = (int)(Pad(l) * fx), y0 = (int)(Pad(t) * fy);
        int x1 = full.PixelWidth - (int)(Pad(r) * fx), y1 = full.PixelHeight - (int)(Pad(b) * fy);
        if (x1 - x0 < 16 || y1 - y0 < 16) return full;
        var cropped = new CroppedBitmap(full, new Int32Rect(x0, y0, x1 - x0, y1 - y0));
        cropped.Freeze();
        return cropped;
    }

    /// <summary>Most prominent saturated colour, darkened so white text stays readable on top of it.</summary>
    static Color DominantColor(byte[] bgra)
    {
        var count = new int[4096];
        var sum = new long[4096 * 3];
        for (int i = 0; i + 3 < bgra.Length; i += 4)
        {
            int b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];
            int k = (r >> 4) << 8 | (g >> 4) << 4 | (b >> 4);
            count[k]++; sum[k * 3] += r; sum[k * 3 + 1] += g; sum[k * 3 + 2] += b;
        }
        int best = -1; double bestScore = -1;
        for (int k = 0; k < 4096; k++)
        {
            if (count[k] == 0) continue;
            double r = (double)sum[k * 3] / count[k], g = (double)sum[k * 3 + 1] / count[k], bl = (double)sum[k * 3 + 2] / count[k];
            double mx = Math.Max(r, Math.Max(g, bl)), mn = Math.Min(r, Math.Min(g, bl));
            double score = count[k] * ((mx - mn) / Math.Max(mx, 1) + 0.15);
            if (score > bestScore) { bestScore = score; best = k; }
        }
        if (best < 0) return Color.FromRgb(40, 40, 48);
        double R = (double)sum[best * 3] / count[best], G = (double)sum[best * 3 + 1] / count[best], B = (double)sum[best * 3 + 2] / count[best];
        double f = Math.Min(1.0, 105 / Math.Max(Math.Max(R, Math.Max(G, B)), 1));
        return Color.FromRgb((byte)(R * f), (byte)(G * f), (byte)(B * f));
    }
}
