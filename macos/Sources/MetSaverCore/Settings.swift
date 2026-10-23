import Foundation

/// User settings, stored as config.json. Keys are snake_case and match the Windows app's config, so the same
/// file means the same thing on both.
public struct Settings: Codable, Equatable {
    public static let currentVersion = 3

    public var version = Settings.currentVersion
    public var secondsPerPainting = 90
    /// Met department ids: 11 European Paintings, 1 American Paintings, 21 Modern & Contemporary, 6 Asian Art.
    public var departmentIds = [11, 1]
    /// "cover" = art fills the screen (cropped); "fit" = whole painting on a mat.
    public var fillMode = "cover"
    /// A painting's shape must be able to fill at least this fraction of the screen's area.
    public var minScreenFill = 0.8
    public var minSideCm = 20.0
    /// Reject images that would need more than 1/this upscaling to cover the screen.
    public var minPixelRatio = 0.6
    public var maxDownloadMb = 40
    public var showCaption = true
    /// top-left, top-right, bottom-left, bottom-right.
    public var infoCorner = "bottom-left"
    /// The info card fades out after this many seconds; 0 = always visible.
    public var infoSeconds = 15
    /// "off", "subtle" (5% drift) or "ken_burns" (15% zoom in or out plus pan).
    public var motion = "subtle"
    public var maxWidth = 2560
    public var maxHeight = 1440
    public var matColor = "#17150f"
    public var timeOfDay = true
    public var ambientAware = true
    public var smartCrop = true
    public var catalogUrl = ""

    public init() {}

    public var cover: Bool { fillMode.lowercased() != "fit" }
    public var drift: Double {
        switch motion { case "off": return 0; case "ken_burns": return 0.15; default: return 0.05 }
    }

    enum CodingKeys: String, CodingKey {
        case version = "_v", secondsPerPainting = "seconds_per_painting", departmentIds = "department_ids"
        case fillMode = "fill_mode", minScreenFill = "min_screen_fill", minSideCm = "min_side_cm"
        case minPixelRatio = "min_pixel_ratio", maxDownloadMb = "max_download_mb", showCaption = "show_caption"
        case infoCorner = "info_corner", infoSeconds = "info_seconds", motion
        case maxWidth = "max_width", maxHeight = "max_height", matColor = "mat_color"
        case timeOfDay = "time_of_day", ambientAware = "ambient_aware", smartCrop = "smart_crop", catalogUrl = "catalog_url"
    }

    /// Missing keys fall back to defaults, so old or hand-trimmed files keep working.
    public init(from decoder: Decoder) throws {
        self.init()
        let c = try decoder.container(keyedBy: CodingKeys.self)
        func get<T: Decodable>(_ key: CodingKeys, _ current: inout T) { if let v = try? c.decodeIfPresent(T.self, forKey: key) { current = v } }
        get(.version, &version); get(.secondsPerPainting, &secondsPerPainting); get(.departmentIds, &departmentIds)
        get(.fillMode, &fillMode); get(.minScreenFill, &minScreenFill); get(.minSideCm, &minSideCm)
        get(.minPixelRatio, &minPixelRatio); get(.maxDownloadMb, &maxDownloadMb); get(.showCaption, &showCaption)
        get(.infoCorner, &infoCorner); get(.infoSeconds, &infoSeconds); get(.motion, &motion)
        get(.maxWidth, &maxWidth); get(.maxHeight, &maxHeight); get(.matColor, &matColor)
        get(.timeOfDay, &timeOfDay); get(.ambientAware, &ambientAware); get(.smartCrop, &smartCrop); get(.catalogUrl, &catalogUrl)
        normalize()
    }

    /// Clamp hand-edited values into sane ranges.
    public mutating func normalize() {
        secondsPerPainting = min(max(secondsPerPainting, 5), 3600)
        minScreenFill = min(max(minScreenFill, 0.3), 1.0)
        minPixelRatio = min(max(minPixelRatio, 0.2), 1.0)
        infoSeconds = max(infoSeconds, 0)
        maxWidth = max(maxWidth, 640); maxHeight = max(maxHeight, 360)
        if departmentIds.isEmpty { departmentIds = [11] }
    }

    public static func load(from url: URL = Paths.config) -> Settings {
        if let data = try? Data(contentsOf: url), let s = try? JSONDecoder().decode(Settings.self, from: data) { return s }
        let fresh = Settings()
        fresh.save(to: url)
        return fresh
    }

    public func save(to url: URL = Paths.config) {
        do {
            try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
            let enc = JSONEncoder(); enc.outputFormatting = [.prettyPrinted, .sortedKeys]
            try enc.encode(self).write(to: url, options: .atomic)
        } catch { Log.error("saving config: \(error)") }
    }
}
