import Foundation
#if canImport(ImageIO)
import ImageIO
import UniformTypeIdentifiers
#endif

/// On-disk cache of recently shown paintings, so the saver works offline and starts instantly.
public enum Cache {
    static let keep = 60

    public static func save(_ a: Artwork) {
        Paths.ensure()
        try? a.jpeg.write(to: Paths.cache.appendingPathComponent("\(a.id).jpg"), options: .atomic)
        if let meta = try? JSONEncoder().encode(a.caption) { try? meta.write(to: Paths.cache.appendingPathComponent("\(a.id).json"), options: .atomic) }
        trim()
    }

    public static func pick() -> Artwork? {
        let files = (try? FileManager.default.contentsOfDirectory(at: Paths.cache, includingPropertiesForKeys: nil))?
            .filter { $0.pathExtension == "jpg" } ?? []
        guard let jpg = files.randomElement(), let data = try? Data(contentsOf: jpg) else { return nil }
        let id = Int(jpg.deletingPathExtension().lastPathComponent) ?? 0
        let metaURL = jpg.deletingPathExtension().appendingPathExtension("json")
        let caption = (try? Data(contentsOf: metaURL)).flatMap { try? JSONDecoder().decode(Caption.self, from: $0) }
            ?? Caption(title: "Untitled", artist: "", date: "", medium: "")
        return Artwork(id: id, caption: caption, jpeg: data)
    }

    public static var count: Int {
        ((try? FileManager.default.contentsOfDirectory(at: Paths.cache, includingPropertiesForKeys: nil)) ?? []).filter { $0.pathExtension == "jpg" }.count
    }

    private static func trim() {
        let fm = FileManager.default
        let files = ((try? fm.contentsOfDirectory(at: Paths.cache, includingPropertiesForKeys: [.contentModificationDateKey])) ?? [])
            .filter { $0.pathExtension == "jpg" }
            .sorted { (a, b) in
                let da = (try? a.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? .distantPast
                let db = (try? b.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? .distantPast
                return da > db
            }
        for old in files.dropFirst(keep) {
            try? fm.removeItem(at: old)
            try? fm.removeItem(at: old.deletingPathExtension().appendingPathExtension("json"))
        }
    }
}

/// Chooses paintings: picks a known-good one from the catalog for this screen, downloads just that, remembers what it has shown.
public actor Curator {
    private let settings: Settings
    private let prefs: Preferences?
    private let catalog: Catalog?
    private let met = MetClient()
    private var seen: Set<Int>
    private static let historyKeep = 4000

    public init(settings: Settings, prefs: Preferences? = nil, catalog: Catalog?) {
        self.settings = settings; self.prefs = prefs; self.catalog = catalog
        if let data = try? Data(contentsOf: Paths.history), let ids = try? JSONDecoder().decode([Int].self, from: data) { seen = Set(ids) }
        else { seen = [] }
    }

    private func saveHistory() {
        Paths.ensure()
        try? JSONEncoder().encode(Array(seen.suffix(Curator.historyKeep))).write(to: Paths.history, options: .atomic)
    }

    /// Find and download one painting for a screen of the given pixel size; nil if none could be found.
    public func next(screenW: Int, screenH: Int, report: @escaping (LoadStatus) -> Void) async -> Artwork? {
        guard let catalog = catalog else { Log.info("no catalog bundled"); return nil }
        report(LoadStatus("Choosing a painting for your \(screenW)×\(screenH) screen…"))
        var tries = 0
        while tries < 6 && !Task.isCancelled {
            tries += 1
            let prefs = self.prefs
            guard let entry = catalog.pick(screenW: screenW, screenH: screenH, settings: settings,
                                           seen: { self.seen.contains($0) },
                                           extraWeight: prefs.map { p in { p.weight($0) } }) else {
                if seen.isEmpty { return nil }
                Log.info("catalog exhausted - starting over"); seen.removeAll(); continue
            }
            seen.insert(entry.id)
            do {
                let obj = try await met.object(entry.id)
                guard obj.isPublicDomain, let url = URL(string: obj.primaryImage), !obj.primaryImage.isEmpty else {
                    Log.info("     [\(entry.id)] no public image - next"); continue
                }
                Log.info("catalog pick [\(entry.id)] \(entry.title)")
                if let art = try await download(obj, url: url, screenW: screenW, screenH: screenH, report: report) {
                    saveHistory()
                    return art
                }
            } catch { Log.error("object \(entry.id): \(error)") }
        }
        return nil
    }

    private func download(_ obj: MetObject, url: URL, screenW: Int, screenH: Int, report: @escaping (LoadStatus) -> Void) async throws -> Artwork? {
        let started = Date()
        var lastReport = Date.distantPast
        let title = obj.title.isEmpty ? "painting" : obj.title
        report(LoadStatus("Downloading “\(title)”", detail: obj.artistDisplayName))
        let s = settings
        let data = try await met.download(url, capBytes: s.maxDownloadMb * 1024 * 1024,
            accept: { w, h in !Vetting.tooSmall(w: w, h: h, screenW: screenW, screenH: screenH, settings: s) },
            progress: { got, total in
                guard Date().timeIntervalSince(lastReport) > 0.1 else { return }
                lastReport = Date()
                let frac = total.map { Double(got) / Double($0) }
                var eta: TimeInterval?
                let elapsed = Date().timeIntervalSince(started)
                if let t = total, got > 128 * 1024, elapsed > 0.4 { eta = elapsed * Double(t - got) / Double(got) }
                report(LoadStatus("Downloading “\(title)”", fraction: frac, eta: eta, detail: obj.artistDisplayName))
            })
        guard let bytes = data else { Log.info("     -> rejected before full download"); return nil }
        report(LoadStatus("Preparing the painting…", detail: obj.title))
        guard let jpeg = Curator.downscaleToJpeg(bytes, maxW: s.maxWidth, maxH: s.maxHeight) else { return nil }
        let art = Artwork(id: obj.objectID, caption: obj.caption, jpeg: jpeg)
        Cache.save(art)
        return art
    }

    /// Shrink to fit within maxW x maxH (the "2K" cap) and re-encode as JPEG for the cache.
    static func downscaleToJpeg(_ data: Data, maxW: Int, maxH: Int) -> Data? {
        #if canImport(ImageIO)
        guard let src = CGImageSourceCreateWithData(data as CFData, nil) else { return nil }
        let opts: [CFString: Any] = [
            kCGImageSourceCreateThumbnailFromImageAlways: true,
            kCGImageSourceCreateThumbnailWithTransform: true,
            kCGImageSourceThumbnailMaxPixelSize: max(maxW, maxH),
        ]
        guard let thumb = CGImageSourceCreateThumbnailAtIndex(src, 0, opts as CFDictionary) else { return nil }
        let out = NSMutableData()
        guard let dest = CGImageDestinationCreateWithData(out, "public.jpeg" as CFString, 1, nil) else { return nil }
        CGImageDestinationAddImage(dest, thumb, [kCGImageDestinationLossyCompressionQuality: 0.92] as CFDictionary)
        return CGImageDestinationFinalize(dest) ? out as Data : nil
        #else
        return data
        #endif
    }
}
