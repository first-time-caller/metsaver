import AppKit
import ImageIO
#if canImport(MetSaverCore)
import MetSaverCore
#endif

/// A painting ready to display: border-trimmed, cropped to the screen's shape, with a tint taken from its colours.
struct PreparedArt {
    let image: CGImage
    let tint: NSColor
    let caption: Caption
    let id: Int
}

enum ImagePrep {
    static func prepare(_ art: Artwork, settings s: Settings, screenW: Int, screenH: Int) -> PreparedArt? {
        guard let src = CGImageSourceCreateWithData(art.jpeg as CFData, nil),
              var image = CGImageSourceCreateImageAtIndex(src, 0, nil) else { return nil }
        image = trimBorders(image)
        if s.cover && s.smartCrop && screenW > 0 && screenH > 0 {
            image = smartCrop(image, aspect: Double(screenW) / Double(screenH))
        }
        return PreparedArt(image: image, tint: dominantColor(image), caption: art.caption, id: art.id)
    }

    // MARK: pixels

    /// A small 8-bit grayscale copy for analysis. Row 0 is the top of the picture.
    static func grayBitmap(_ image: CGImage, maxSide: Int) -> (px: [UInt8], w: Int, h: Int) {
        let k = min(1.0, Double(maxSide) / Double(max(image.width, image.height)))
        let w = max(1, Int(Double(image.width) * k)), h = max(1, Int(Double(image.height) * k))
        var px = [UInt8](repeating: 0, count: w * h)
        px.withUnsafeMutableBytes { buf in
            guard let ctx = CGContext(data: buf.baseAddress, width: w, height: h, bitsPerComponent: 8, bytesPerRow: w,
                                      space: CGColorSpaceCreateDeviceGray(), bitmapInfo: CGImageAlphaInfo.none.rawValue) else { return }
            ctx.interpolationQuality = .medium
            ctx.draw(image, in: CGRect(x: 0, y: 0, width: w, height: h))
        }
        return (px, w, h)
    }

    // MARK: border trim

    /// Cut near-black or near-white photographic borders (at most 5% per edge).
    static func trimBorders(_ image: CGImage) -> CGImage {
        let (g, w, h) = grayBitmap(image, maxSide: 500)
        func uniform(_ values: [UInt8]) -> Bool {
            let n = Double(values.count)
            let mean = values.reduce(0.0) { $0 + Double($1) } / n
            let sd = (values.reduce(0.0) { $0 + (Double($1) - mean) * (Double($1) - mean) } / n).squareRoot()
            return sd < 7 && (mean < 40 || mean > 225)
        }
        func col(_ x: Int) -> [UInt8] { (0..<h).map { g[$0 * w + x] } }
        func row(_ y: Int) -> [UInt8] { Array(g[(y * w)..<((y + 1) * w)]) }

        let limX = Int(Double(w) * 0.05), limY = Int(Double(h) * 0.05)
        var l = 0, r = 0, t = 0, b = 0
        while l < limX && uniform(col(l)) { l += 1 }
        while r < limX && uniform(col(w - 1 - r)) { r += 1 }
        while t < limY && uniform(row(t)) { t += 1 }
        while b < limY && uniform(row(h - 1 - b)) { b += 1 }
        if l + r + t + b == 0 { return image }

        let fx = Double(image.width) / Double(w), fy = Double(image.height) / Double(h)
        func pad(_ n: Int) -> Int { n == 0 ? 0 : n + 2 }                          // a little extra to clear soft edges
        let x0 = Int(Double(pad(l)) * fx), y0 = Int(Double(pad(t)) * fy)
        let x1 = image.width - Int(Double(pad(r)) * fx), y1 = image.height - Int(Double(pad(b)) * fy)
        guard x1 - x0 > 16, y1 - y0 > 16, let cropped = image.cropping(to: CGRect(x: x0, y: y0, width: x1 - x0, height: y1 - y0)) else { return image }
        return cropped
    }

    // MARK: smart crop

    /// "Cover" fitting throws some of the picture away. Instead of trimming equally from both sides, slide the kept window
    /// along the spare axis to where the picture is busiest (edge energy), with a mild pull toward the middle so a blank sky
    /// or plain wall is what gets cut.
    static func smartCrop(_ image: CGImage, aspect: Double) -> CGImage {
        let W = image.width, H = image.height
        let imageAspect = Double(W) / Double(H)
        if abs(imageAspect - aspect) / aspect < 0.02 { return image }
        let cropX = imageAspect > aspect                      // too wide: choose which columns to keep; too tall: which rows

        let (px, sw, sh) = grayBitmap(image, maxSide: 400)
        let k = Double(sw) / Double(W)
        var energy = [Double](repeating: 0, count: cropX ? sw : sh)
        if sw > 1 && sh > 1 {
            for y in 1..<sh {
                for x in 1..<sw {
                    let v = Int(px[y * sw + x])
                    let e = abs(v - Int(px[y * sw + x - 1])) + abs(v - Int(px[(y - 1) * sw + x]))
                    energy[cropX ? x : y] += Double(e)
                }
            }
        }
        let len = energy.count
        var win = cropX ? Int((Double(sh) * aspect).rounded()) : Int((Double(sw) / aspect).rounded())
        win = min(max(win, 1), len)
        var prefix = [Double](repeating: 0, count: len + 1)
        for i in 0..<len { prefix[i + 1] = prefix[i] + energy[i] }

        var best = (len - win) / 2
        var bestScore = -Double.infinity
        for off in 0...(len - win) {
            let centre = (Double(off) + Double(win) / 2) / Double(len)         // 0..1; 0.5 = dead centre
            let score = (prefix[off + win] - prefix[off]) * (1 - 0.35 * abs(centre - 0.5) * 2)
            if score > bestScore { bestScore = score; best = off }
        }

        let rect: CGRect
        if cropX {
            let x = min(max(Int((Double(best) / k).rounded()), 0), W - 1)
            rect = CGRect(x: x, y: 0, width: min(W - x, Int((Double(H) * aspect).rounded())), height: H)
        } else {
            let y = min(max(Int((Double(best) / k).rounded()), 0), H - 1)
            rect = CGRect(x: 0, y: y, width: W, height: min(H - y, Int((Double(W) / aspect).rounded())))
        }
        guard rect.width >= 16, rect.height >= 16, let cropped = image.cropping(to: rect) else { return image }
        return cropped
    }

    // MARK: colour

    /// Most prominent saturated colour, darkened so white text stays readable on top of it.
    static func dominantColor(_ image: CGImage) -> NSColor {
        let n = 48
        var px = [UInt8](repeating: 0, count: n * n * 4)
        px.withUnsafeMutableBytes { buf in
            guard let ctx = CGContext(data: buf.baseAddress, width: n, height: n, bitsPerComponent: 8, bytesPerRow: n * 4,
                                      space: CGColorSpaceCreateDeviceRGB(),
                                      bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return }
            ctx.draw(image, in: CGRect(x: 0, y: 0, width: n, height: n))
        }
        var count = [Int](repeating: 0, count: 4096)
        var sum = [Double](repeating: 0, count: 4096 * 3)
        for i in stride(from: 0, to: px.count, by: 4) {
            let r = Int(px[i]), g = Int(px[i + 1]), b = Int(px[i + 2])
            let k = (r >> 4) << 8 | (g >> 4) << 4 | (b >> 4)
            count[k] += 1; sum[k * 3] += Double(r); sum[k * 3 + 1] += Double(g); sum[k * 3 + 2] += Double(b)
        }
        var best = -1, bestScore = -1.0
        for k in 0..<4096 where count[k] > 0 {
            let c = Double(count[k])
            let r = sum[k * 3] / c, g = sum[k * 3 + 1] / c, b = sum[k * 3 + 2] / c
            let mx = max(r, g, b), mn = min(r, g, b)
            let score = c * ((mx - mn) / max(mx, 1) + 0.15)
            if score > bestScore { bestScore = score; best = k }
        }
        guard best >= 0 else { return NSColor(srgbRed: 0.16, green: 0.16, blue: 0.19, alpha: 1) }
        let c = Double(count[best])
        let r = sum[best * 3] / c, g = sum[best * 3 + 1] / c, b = sum[best * 3 + 2] / c
        let f = min(1.0, 105 / max(max(r, g, b), 1))
        return NSColor(srgbRed: r * f / 255, green: g * f / 255, blue: b * f / 255, alpha: 1)
    }
}
