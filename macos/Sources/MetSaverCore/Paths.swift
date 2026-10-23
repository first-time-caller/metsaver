import Foundation

/// Everything MetSaver stores lives in Application Support/MetSaver. (Inside the screensaver host's container
/// that resolves to ~/Library/Containers/com.apple.ScreenSaver.Engine.legacyScreenSaver/..., which is fine.)
public enum Paths {
    /// Override with the METSAVER_HOME environment variable (tests, portable use).
    public static let home: URL = {
        if let custom = ProcessInfo.processInfo.environment["METSAVER_HOME"], !custom.isEmpty {
            return URL(fileURLWithPath: custom, isDirectory: true)
        }
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        return base.appendingPathComponent("MetSaver", isDirectory: true)
    }()
    public static var cache: URL { home.appendingPathComponent("cache", isDirectory: true) }
    public static var config: URL { home.appendingPathComponent("config.json") }
    public static var history: URL { home.appendingPathComponent("seen.json") }
    public static var favourites: URL { home.appendingPathComponent("favourites.json") }
    public static var log: URL { home.appendingPathComponent("log.txt") }

    public static func ensure() {
        try? FileManager.default.createDirectory(at: cache, withIntermediateDirectories: true)
    }
}

/// Tiny file logger: when someone says "it shows nothing", send log.txt.
public enum Log {
    private static let queue = DispatchQueue(label: "metsaver.log")
    private static let stamp: ISO8601DateFormatter = ISO8601DateFormatter()

    public static func info(_ message: String) { write("INFO ", message) }
    public static func error(_ message: String) { write("ERROR", message) }

    private static func write(_ level: String, _ message: String) {
        let line = "\(stamp.string(from: Date())) \(level) \(message)\n"
        queue.async {
            Paths.ensure()
            let url = Paths.log
            if let size = (try? FileManager.default.attributesOfItem(atPath: url.path))?[.size] as? Int, size > 512 * 1024 {
                try? FileManager.default.removeItem(at: url)
            }
            if let handle = try? FileHandle(forWritingTo: url) {
                handle.seekToEndOfFile(); handle.write(Data(line.utf8)); try? handle.close()
            } else {
                try? Data(line.utf8).write(to: url)
            }
        }
    }
}
