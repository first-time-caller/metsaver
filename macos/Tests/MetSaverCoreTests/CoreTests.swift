import XCTest
@testable import MetSaverCore

final class VettingTests: XCTestCase {
    func entry(_ id: Int, _ w: Double, _ h: Double, dept: Int = 11, pw: Int = 4000, ph: Int = 3000,
               title: String = "Untitled", artist: String = "A", date: String = "1880") -> CatalogEntry {
        CatalogEntry(id: id, title: title, artist: artist, date: date, medium: "Oil on canvas", dept: dept, url: "",
                     wCm: w, hCm: h, pxW: pw, pxH: ph, bytes: 5_000_000)
    }

    func testFillIsOneForSameShape() {
        XCTAssertEqual(Vetting.fill(wCm: 160, hCm: 90, screenW: 2560, screenH: 1440), 1.0, accuracy: 0.001)
    }

    func testPortraitPaintingDoesNotFitLandscapeScreen() {
        XCTAssertFalse(Vetting.fits(entry(1, 50, 70), screenW: 2560, screenH: 1440, settings: Settings()))
    }

    func testPortraitPaintingFitsPortraitMonitor() {
        XCTAssertTrue(Vetting.fits(entry(1, 54, 96), screenW: 1080, screenH: 1920, settings: Settings()))
    }

    func testTooSmallImagesRejectedForCover() {
        let s = Settings()
        XCTAssertTrue(Vetting.tooSmall(w: 800, h: 450, screenW: 2560, screenH: 1440, settings: s))
        XCTAssertFalse(Vetting.tooSmall(w: 2000, h: 1125, screenW: 2560, screenH: 1440, settings: s))
    }

    func testBetterShapedPaintingsAreChosenMoreOften() {
        let cat = Catalog(generated: Date(), entries: [entry(1, 160, 90), entry(2, 144, 90)])
        var s = Settings(); s.timeOfDay = false
        let first = (0..<2000).filter { _ in
            cat.pick(screenW: 2560, screenH: 1440, settings: s, seen: { _ in false })?.id == 1
        }.count
        XCTAssertTrue((1100...1500).contains(first), "got \(first)")      // weights 1.0 vs 0.41 => ~71%
    }

    func testSeenAndDepartmentAreExcluded() {
        let cat = Catalog(generated: Date(), entries: [entry(1, 160, 90), entry(2, 160, 90, dept: 21), entry(3, 160, 90)])
        var s = Settings(); s.departmentIds = [11]
        XCTAssertEqual(cat.pick(screenW: 2560, screenH: 1440, settings: s, seen: { $0 == 3 })?.id, 1)
        XCTAssertNil(cat.pick(screenW: 2560, screenH: 1440, settings: s, seen: { $0 == 1 || $0 == 3 }))
    }

    func testFavouritesAreBoosted() {
        let cat = Catalog(generated: Date(), entries: [entry(1, 160, 90), entry(2, 160, 90)])
        var s = Settings(); s.timeOfDay = false
        let hits = (0..<2000).filter { _ in
            cat.pick(screenW: 2560, screenH: 1440, settings: s, seen: { _ in false },
                     extraWeight: { $0 == 2 ? 3.0 : 1.0 })?.id == 2
        }.count
        XCTAssertTrue((1400...1600).contains(hits), "got \(hits)")        // 3:1 => 75%
    }
}

final class MoodTests: XCTestCase {
    func date(_ month: Int, _ hour: Int) -> Date {
        Calendar.current.date(from: DateComponents(year: 2026, month: month, day: 15, hour: hour))!
    }

    func testNightScenesPreferredAfterDark() {
        XCTAssertGreaterThan(Mood.weight(title: "Moonlit Harbor", medium: "Oil", now: date(1, 23)), 2)
        XCTAssertLessThan(Mood.weight(title: "Moonlit Harbor", medium: "Oil", now: date(7, 12)), 0.5)
    }

    func testSnowFavouredInWinter() {
        XCTAssertGreaterThan(Mood.weight(title: "Skating in the Snow", medium: "Oil", now: date(1, 23)),
                             Mood.weight(title: "Skating in the Snow", medium: "Oil", now: date(7, 12)))
    }

    func testNeutralTitlesStayNeutral() {
        XCTAssertEqual(Mood.weight(title: "Portrait of a Man", medium: "Oil on canvas", now: date(7, 12)), 1.0)
    }
}

final class ImageHeaderTests: XCTestCase {
    func testPng() {
        var png = [UInt8](repeating: 0, count: 24)
        png.replaceSubrange(0..<8, with: [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])
        png[18] = 0x0A; png[19] = 0x00; png[22] = 0x05; png[23] = 0xA0           // 2560 x 1440
        let size = ImageHeader.size(of: png)
        XCTAssertEqual(size?.width, 2560); XCTAssertEqual(size?.height, 1440)
    }

    func testJpegAfterOtherSegments() {
        let jpg: [UInt8] = [0xFF, 0xD8, 0xFF, 0xE1, 0x00, 0x06, 1, 2, 3, 4,
                            0xFF, 0xC0, 0x00, 0x11, 8, 0x05, 0xA0, 0x0A, 0x00, 3]
        let size = ImageHeader.size(of: jpg)
        XCTAssertEqual(size?.width, 2560); XCTAssertEqual(size?.height, 1440)
    }

    func testNeedsMoreBytesAndRejectsNonImages() {
        XCTAssertNil(ImageHeader.size(of: [0xFF, 0xD8, 0xFF, 0xE1, 0x10, 0x00, 1, 2, 3]))
        XCTAssertNil(ImageHeader.size(of: [1, 2, 3, 4, 5]))
    }
}

final class CatalogLoadingTests: XCTestCase {
    func testLoadsGzipFixture() throws {
        let url = URL(fileURLWithPath: #filePath).deletingLastPathComponent().appendingPathComponent("fixture.b64")
        let b64 = try String(contentsOf: url).trimmingCharacters(in: .whitespacesAndNewlines)
        let cat = try Catalog.load(gzip: Data(base64Encoded: b64)!)
        XCTAssertEqual(cat.entries.count, 1)
        XCTAssertEqual(cat.entries[0].title, "Work")
        XCTAssertEqual(cat.entries[0].wCm, 160)
        XCTAssertGreaterThan(cat.generated.timeIntervalSince1970, 1_700_000_000)   // seven-digit fractional seconds parsed
    }

    /// The real catalog written by the Windows/.NET builder must load too.
    func testLoadsTheShippedCatalog() throws {
        let url = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("data/catalog.json.gz")
        guard FileManager.default.fileExists(atPath: url.path) else { throw XCTSkip("data/catalog.json.gz not present") }
        let cat = try XCTUnwrap(Catalog.load(file: url))
        XCTAssertGreaterThan(cat.entries.count, 3000)
        XCTAssertTrue(cat.entries.allSatisfy { $0.wCm > 0 && $0.hCm > 0 })
    }
}

final class SettingsTests: XCTestCase {
    func testMissingKeysFallBackToDefaultsAndClamp() throws {
        let json = #"{ "seconds_per_painting": 1, "motion": "ken_burns", "info_corner": "top-right" }"#
        let s = try JSONDecoder().decode(Settings.self, from: Data(json.utf8))
        XCTAssertEqual(s.secondsPerPainting, 5)              // clamped
        XCTAssertEqual(s.motion, "ken_burns"); XCTAssertEqual(s.drift, 0.15)
        XCTAssertEqual(s.infoCorner, "top-right")
        XCTAssertEqual(s.minScreenFill, 0.8)                  // default kept
    }

    func testRoundTripUsesSnakeCaseKeys() throws {
        var s = Settings(); s.secondsPerPainting = 45
        let text = String(decoding: try JSONEncoder().encode(s), as: UTF8.self)
        XCTAssertTrue(text.contains("\"seconds_per_painting\":45"))
        XCTAssertEqual(try JSONDecoder().decode(Settings.self, from: Data(text.utf8)).secondsPerPainting, 45)
    }
}

final class PreferencesTests: XCTestCase {
    func testTogglesAndPersists() {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("fav-\(UUID().uuidString).json")
        defer { try? FileManager.default.removeItem(at: url) }
        let p = Preferences(url: url)
        XCTAssertTrue(p.toggle(42))
        XCTAssertTrue(Preferences(url: url).isFavourite(42))
        XCTAssertFalse(p.toggle(42))
        XCTAssertEqual(p.weight(42), 1.0)
    }
}
