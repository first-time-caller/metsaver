import Foundation

public enum Vetting {
    /// 0..1: how much of the screen's area the painting can fill when fitted without cropping (1 = same shape).
    public static func fill(wCm: Double, hCm: Double, screenW: Int, screenH: Int) -> Double {
        let pa = wCm / hCm, sa = Double(screenW) / Double(screenH)
        return min(pa / sa, sa / pa)
    }

    /// True if an image of w x h pixels would need too much upscaling for this screen.
    public static func tooSmall(w: Int, h: Int, screenW: Int, screenH: Int, settings s: Settings) -> Bool {
        let sw = Double(screenW), sh = Double(screenH)
        let need = s.cover ? max(sw / Double(w), sh / Double(h)) : min(sw / Double(w), sh / Double(h))
        return need > 1 / s.minPixelRatio
    }

    /// Does a catalogued painting suit this screen and these settings?
    public static func fits(_ e: CatalogEntry, screenW: Int, screenH: Int, settings s: Settings) -> Bool {
        if min(e.wCm, e.hCm) < s.minSideCm || e.hCm <= 0 { return false }
        if fill(wCm: e.wCm, hCm: e.hCm, screenW: screenW, screenH: screenH) < s.minScreenFill { return false }
        if e.pxW > 0 && e.pxH > 0 && tooSmall(w: e.pxW, h: e.pxH, screenW: screenW, screenH: screenH, settings: s) { return false }
        if e.bytes > Int64(s.maxDownloadMb) * 1024 * 1024 { return false }
        return true
    }
}

/// Cheap, explainable "does this suit right now?" scoring from a painting's title and medium.
public enum Mood {
    static let night = ["night", "moon", "nocturne", "candle", "lamp", "starry", "stars"]
    static let dusk = ["evening", "sunset", "dusk", "twilight", "sunrise", "dawn", "daybreak"]
    static let morning = ["morning", "dawn", "sunrise", "daybreak", "breakfast"]
    static let day = ["noon", "midday", "sunny", "sunlight", "beach"]
    static let winter = ["winter", "snow", "ice", "frost", "skating", "sleigh"]
    static let spring = ["spring", "blossom", "orchard", "may", "easter"]
    static let summer = ["summer", "bathing", "beach", "haymaking", "sunny", "harvest"]
    static let autumn = ["autumn", "fall", "harvest", "vintage", "grape"]

    public static func weight(title: String, medium: String, now: Date, calendar: Calendar = .current) -> Double {
        let text = "\(title) \(medium)".lowercased()
        func has(_ words: [String]) -> Bool { words.contains { text.contains($0) } }
        let h = calendar.component(.hour, from: now)
        let isNight = h >= 21 || h < 5, isMorning = h >= 5 && h < 10, isDay = h >= 10 && h < 17, isDusk = h >= 17 && h < 21
        var w = 1.0

        if has(night) { w *= isNight ? 3 : isDusk ? 1.3 : 0.35 }
        if has(dusk) { w *= isDusk ? 2.5 : (isMorning && has(morning) ? 2 : 0.8) }
        if has(morning) { w *= isMorning ? 2.5 : 1 }
        if has(day) { w *= isDay ? 1.6 : (isNight ? 0.8 : 1.2) }

        let m = calendar.component(.month, from: now)
        let season = (m == 12 || m <= 2) ? "winter" : m <= 5 ? "spring" : m <= 8 ? "summer" : "autumn"
        func applySeason(_ name: String, _ words: [String]) {
            if has(words) { w *= season == name ? 2.2 : (name == "winter" || season == "winter" ? 0.5 : 0.9) }
        }
        applySeason("winter", winter); applySeason("spring", spring); applySeason("summer", summer); applySeason("autumn", autumn)
        return w
    }
}

/// Reads pixel dimensions from the first bytes of a JPEG or PNG without decoding it.
public enum ImageHeader {
    public static func size(of d: [UInt8]) -> (width: Int, height: Int)? {
        if d.count >= 24, d[0] == 0x89, d[1] == 0x50, d[2] == 0x4E, d[3] == 0x47 {                    // PNG IHDR
            let w = Int(d[16]) << 24 | Int(d[17]) << 16 | Int(d[18]) << 8 | Int(d[19])
            let h = Int(d[20]) << 24 | Int(d[21]) << 16 | Int(d[22]) << 8 | Int(d[23])
            return w > 0 && h > 0 ? (w, h) : nil
        }
        guard d.count >= 4, d[0] == 0xFF, d[1] == 0xD8 else { return nil }
        var i = 2
        while i + 4 <= d.count {
            if d[i] != 0xFF { i += 1; continue }
            let marker = d[i + 1]
            if marker == 0xFF { i += 1; continue }
            if marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7) { i += 2; continue }
            let len = Int(d[i + 2]) << 8 | Int(d[i + 3])
            let sof = marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC
            if sof {
                guard i + 9 <= d.count else { return nil }                                          // need more bytes
                let h = Int(d[i + 5]) << 8 | Int(d[i + 6])
                let w = Int(d[i + 7]) << 8 | Int(d[i + 8])
                return w > 0 && h > 0 ? (w, h) : nil
            }
            i += 2 + len
        }
        return nil
    }
}

/// Paintings you've marked as favourites. They're shown more often.
public final class Preferences {
    public static let favouriteBoost = 3.0
    private let url: URL
    private var favourites: Set<Int>
    private let lock = NSLock()

    public init(url: URL = Paths.favourites) {
        self.url = url
        if let data = try? Data(contentsOf: url), let ids = try? JSONDecoder().decode([Int].self, from: data) { favourites = Set(ids) }
        else { favourites = [] }
    }

    public func isFavourite(_ id: Int) -> Bool { lock.lock(); defer { lock.unlock() }; return favourites.contains(id) }

    /// Flip the flag; returns the new state.
    @discardableResult public func toggle(_ id: Int) -> Bool {
        lock.lock(); defer { lock.unlock() }
        let now = favourites.insert(id).inserted
        if !now { favourites.remove(id) }
        try? FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try? JSONEncoder().encode(Array(favourites)).write(to: url, options: .atomic)
        return now
    }

    public func weight(_ id: Int) -> Double { isFavourite(id) ? Preferences.favouriteBoost : 1.0 }
}
