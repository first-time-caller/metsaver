import Foundation

/// What the info card shows. The extra fields feed the details panel.
public struct Caption: Codable, Equatable {
    public var title: String, artist: String, date: String, medium: String
    public var bio = "", credit = "", dimensions = "", gallery = "", url = ""
    public init(title: String, artist: String, date: String, medium: String,
                bio: String = "", credit: String = "", dimensions: String = "", gallery: String = "", url: String = "") {
        self.title = title; self.artist = artist; self.date = date; self.medium = medium
        self.bio = bio; self.credit = credit; self.dimensions = dimensions; self.gallery = gallery; self.url = url
    }
}

/// A downloaded painting: JPEG bytes plus what to show in the info card.
public struct Artwork {
    public var id: Int
    public var caption: Caption
    public var jpeg: Data
    public init(id: Int, caption: Caption, jpeg: Data) { self.id = id; self.caption = caption; self.jpeg = jpeg }
}

/// What the loader is doing right now, for the "finding a painting" overlay.
public struct LoadStatus {
    public var message: String
    public var fraction: Double?
    public var eta: TimeInterval?
    public var detail: String?
    public init(_ message: String, fraction: Double? = nil, eta: TimeInterval? = nil, detail: String? = nil) {
        self.message = message; self.fraction = fraction; self.eta = eta; self.detail = detail
    }
}

/// The subset of a Met collection object that MetSaver needs.
public struct MetObject: Decodable {
    public var objectID: Int
    public var isPublicDomain: Bool
    public var primaryImage: String
    public var title: String
    public var artistDisplayName: String
    public var objectDate: String
    public var medium: String
    public var artistDisplayBio: String
    public var creditLine: String
    public var dimensions: String
    public var galleryNumber: String
    public var objectURL: String

    enum CodingKeys: String, CodingKey {
        case objectID, isPublicDomain, primaryImage, title, artistDisplayName, objectDate, medium
        case artistDisplayBio, creditLine, dimensions, galleryNumber = "GalleryNumber", objectURL
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        objectID = (try? c.decode(Int.self, forKey: .objectID)) ?? 0
        isPublicDomain = (try? c.decode(Bool.self, forKey: .isPublicDomain)) ?? false
        func s(_ k: CodingKeys) -> String { (try? c.decodeIfPresent(String.self, forKey: k)) ?? "" }
        primaryImage = s(.primaryImage); title = s(.title); artistDisplayName = s(.artistDisplayName)
        objectDate = s(.objectDate); medium = s(.medium); artistDisplayBio = s(.artistDisplayBio)
        creditLine = s(.creditLine); dimensions = s(.dimensions); galleryNumber = s(.galleryNumber); objectURL = s(.objectURL)
    }

    public var caption: Caption {
        Caption(title: title.isEmpty ? "Untitled" : title,
                artist: artistDisplayName.isEmpty ? "Unknown artist" : artistDisplayName,
                date: objectDate, medium: medium, bio: artistDisplayBio, credit: creditLine,
                dimensions: dimensions.split(separator: "\n", omittingEmptySubsequences: true).first.map { String($0).trimmingCharacters(in: .whitespaces) } ?? "",
                gallery: galleryNumber, url: objectURL)
    }
}

/// Thin client for The Met's Open Access API (https://metmuseum.github.io/).
public final class MetClient {
    static let api = "https://collectionapi.metmuseum.org/public/collection/v1"
    private let session: URLSession

    public init() {
        let cfg = URLSessionConfiguration.ephemeral
        cfg.timeoutIntervalForRequest = 30
        cfg.httpAdditionalHeaders = ["User-Agent": "MetSaver/1.0 (personal screensaver; Met Open Access API)"]
        session = URLSession(configuration: cfg)
    }

    public func object(_ id: Int) async throws -> MetObject {
        let (data, response) = try await session.data(from: URL(string: "\(MetClient.api)/objects/\(id)")!)
        if let http = response as? HTTPURLResponse, http.statusCode >= 400 { throw URLError(.badServerResponse) }
        return try JSONDecoder().decode(MetObject.self, from: data)
    }

    /// Streams an image. Once the header reveals the pixel size, `accept` decides whether to carry on; returning false abandons
    /// the download early. Returns nil if rejected or over the size cap.
    public func download(_ url: URL, capBytes: Int, accept: @escaping (Int, Int) -> Bool,
                         progress: @escaping (Int, Int?) -> Void) async throws -> Data? {
        let d = Downloader(capBytes: capBytes, accept: accept, progress: progress, headers: session.configuration.httpAdditionalHeaders)
        return try await d.run(url)
    }
}

/// Delegate-based downloader: chunk callbacks are far cheaper than iterating a byte stream one byte at a time.
private final class Downloader: NSObject, URLSessionDataDelegate {
    private var buffer = Data()
    private var expected: Int?
    private var sized = false
    private var finished = false
    private var continuation: CheckedContinuation<Data?, Error>?
    private let capBytes: Int
    private let accept: (Int, Int) -> Bool
    private let progress: (Int, Int?) -> Void
    private let headers: [AnyHashable: Any]?
    private var session: URLSession?

    init(capBytes: Int, accept: @escaping (Int, Int) -> Bool, progress: @escaping (Int, Int?) -> Void, headers: [AnyHashable: Any]?) {
        self.capBytes = capBytes; self.accept = accept; self.progress = progress; self.headers = headers
    }

    func run(_ url: URL) async throws -> Data? {
        try await withCheckedThrowingContinuation { cont in
            self.continuation = cont
            let cfg = URLSessionConfiguration.ephemeral
            cfg.timeoutIntervalForRequest = 30
            cfg.httpAdditionalHeaders = headers
            let s = URLSession(configuration: cfg, delegate: self, delegateQueue: nil)   // serial delegate queue
            self.session = s
            s.dataTask(with: url).resume()
        }
    }

    private func finish(_ result: Result<Data?, Error>) {
        guard !finished else { return }
        finished = true
        session?.invalidateAndCancel()
        switch result {
        case .success(let d): continuation?.resume(returning: d)
        case .failure(let e): continuation?.resume(throwing: e)
        }
        continuation = nil
    }

    func urlSession(_ session: URLSession, dataTask: URLSessionDataTask, didReceive response: URLResponse,
                    completionHandler: @escaping (URLSession.ResponseDisposition) -> Void) {
        if let http = response as? HTTPURLResponse, http.statusCode >= 400 {
            completionHandler(.cancel); finish(.failure(URLError(.badServerResponse))); return
        }
        if response.expectedContentLength > 0 { expected = Int(response.expectedContentLength) }
        if let e = expected, e > capBytes { completionHandler(.cancel); finish(.success(nil)); return }
        completionHandler(.allow)
    }

    func urlSession(_ session: URLSession, dataTask: URLSessionDataTask, didReceive data: Data) {
        guard !finished else { return }
        buffer.append(data)
        progress(buffer.count, expected)
        if buffer.count > capBytes { dataTask.cancel(); finish(.success(nil)); return }
        if !sized && buffer.count <= 2 * 1024 * 1024, let size = ImageHeader.size(of: [UInt8](buffer)) {
            Log.info("     header says \(size.width)x\(size.height)px")
            if !accept(size.width, size.height) { dataTask.cancel(); finish(.success(nil)); return }
            sized = true
        }
    }

    func urlSession(_ session: URLSession, task: URLSessionTask, didCompleteWithError error: Error?) {
        if let error = error { finish(.failure(error)) } else { finish(.success(buffer)) }
    }
}
