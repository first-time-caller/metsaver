import Foundation
#if canImport(Compression)
import Compression
#endif

/// One vetted painting. Short JSON keys keep the shipped catalog small (same file as the Windows app uses).
public struct CatalogEntry: Codable, Hashable {
    public var id: Int
    public var title: String
    public var artist: String
    public var date: String
    public var medium: String
    public var dept: Int
    public var url: String
    public var wCm: Double
    public var hCm: Double
    public var pxW: Int
    public var pxH: Int
    public var bytes: Int64

    enum CodingKeys: String, CodingKey {
        case id = "i", title = "t", artist = "a", date = "d", medium = "m", dept = "c", url = "u"
        case wCm = "w", hCm = "h", pxW = "pw", pxH = "ph", bytes = "b"
    }

    public init(id: Int, title: String, artist: String, date: String, medium: String, dept: Int, url: String,
                wCm: Double, hCm: Double, pxW: Int, pxH: Int, bytes: Int64) {
        self.id = id; self.title = title; self.artist = artist; self.date = date; self.medium = medium
        self.dept = dept; self.url = url; self.wCm = wCm; self.hCm = hCm; self.pxW = pxW; self.pxH = pxH; self.bytes = bytes
    }
}

public enum CatalogError: Error { case notGzip, corrupt }

/// A pre-built, pre-vetted list of public-domain paintings: pick a known-good one for this screen and download just that.
public struct Catalog {
    public var generated: Date
    public var entries: [CatalogEntry]

    public init(generated: Date, entries: [CatalogEntry]) { self.generated = generated; self.entries = entries }

    private struct Doc: Decodable { var generated: String; var entries: [CatalogEntry] }

    public static func load(gzip data: Data) throws -> Catalog {
        let json = try Gzip.decompress(data)
        let doc = try JSONDecoder().decode(Doc.self, from: json)
        return Catalog(generated: Gzip.parseDate(doc.generated) ?? .distantPast, entries: doc.entries)
    }

    public static func load(file url: URL) -> Catalog? {
        guard let data = try? Data(contentsOf: url) else { return nil }
        do { return try load(gzip: data) } catch { Log.error("catalog \(url.lastPathComponent): \(error)"); return nil }
    }

    /// Entries that suit this screen, the chosen collections, and haven't been shown.
    public func candidates(screenW: Int, screenH: Int, settings: Settings, seen: (Int) -> Bool) -> [CatalogEntry] {
        entries.filter { settings.departmentIds.contains($0.dept) && !seen($0.id) && Vetting.fits($0, screenW: screenW, screenH: screenH, settings: settings) }
    }

    /// Weighted random pick. Better-shaped paintings are likelier (fill^4), then mood (hour/season), then extra weight (favourites).
    /// `random` returns a value in 0..<1 and is injectable for tests.
    public func pick(screenW: Int, screenH: Int, settings: Settings, seen: (Int) -> Bool,
                     extraWeight: ((Int) -> Double)? = nil, now: Date = Date(),
                     random: () -> Double = { Double.random(in: 0..<1) }) -> CatalogEntry? {
        let list = candidates(screenW: screenW, screenH: screenH, settings: settings, seen: seen)
        if list.isEmpty { return nil }
        let weights: [Double] = list.map { e in
            var w = pow(Vetting.fill(wCm: e.wCm, hCm: e.hCm, screenW: screenW, screenH: screenH), 4)
            if settings.timeOfDay { w *= Mood.weight(title: e.title, medium: e.medium, now: now) }
            if let extra = extraWeight { w *= extra(e.id) }
            return max(w, 1e-6)
        }
        var r = random() * weights.reduce(0, +)
        for (i, w) in weights.enumerated() { r -= w; if r <= 0 { return list[i] } }
        return list.last
    }
}

/// Minimal gzip reader: skips the header and inflates the raw DEFLATE payload with Apple's Compression framework.
enum Gzip {
    static func decompress(_ data: Data) throws -> Data {
        let b = [UInt8](data)
        guard b.count > 18, b[0] == 0x1f, b[1] == 0x8b, b[2] == 8 else { throw CatalogError.notGzip }
        let flags = b[3]
        var i = 10
        if flags & 4 != 0 { guard i + 2 <= b.count else { throw CatalogError.corrupt }; i += 2 + (Int(b[i]) | Int(b[i + 1]) << 8) }
        if flags & 8 != 0 { while i < b.count && b[i] != 0 { i += 1 }; i += 1 }
        if flags & 16 != 0 { while i < b.count && b[i] != 0 { i += 1 }; i += 1 }
        if flags & 2 != 0 { i += 2 }
        guard i < b.count - 8 else { throw CatalogError.corrupt }
        let n = b.count
        let isize = Int(b[n - 4]) | Int(b[n - 3]) << 8 | Int(b[n - 2]) << 16 | Int(b[n - 1]) << 24
        let capacity = max(isize, 1) + 64
        #if canImport(Compression)
        var out = [UInt8](repeating: 0, count: capacity)
        let written = b.withUnsafeBufferPointer { src -> Int in
            out.withUnsafeMutableBufferPointer { dst -> Int in
                compression_decode_buffer(dst.baseAddress!, capacity, src.baseAddress! + i, n - 8 - i, nil, COMPRESSION_ZLIB)
            }
        }
        guard written > 0 else { throw CatalogError.corrupt }
        return Data(out[0..<written])
        #else
        throw CatalogError.corrupt
        #endif
    }

    /// The Windows builder writes ISO-8601 with fractional seconds ("2026-10-23T16:34:10.1234567Z").
    static func parseDate(_ s: String) -> Date? {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        if let d = f.date(from: s) { return d }
        f.formatOptions = [.withInternetDateTime]
        if let d = f.date(from: s) { return d }
        // Seven fractional digits are more than the formatter accepts: trim to three.
        if let dot = s.firstIndex(of: "."), let z = s.firstIndex(where: { $0 == "Z" || $0 == "+" }) {
            let frac = s[s.index(after: dot)..<z].prefix(3)
            f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
            return f.date(from: String(s[..<dot]) + "." + String(frac) + String(s[z...]))
        }
        return nil
    }
}
