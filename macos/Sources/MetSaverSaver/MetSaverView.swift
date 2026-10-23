import AppKit
import IOKit.ps
import QuartzCore
import ScreenSaver
#if canImport(MetSaverCore)
import MetSaverCore
#endif

/// One painting on one screen: the picture (slow zoom and pan driven by Core Animation) plus the glass info card.
final class ArtView: NSView {
    static let crossfadeSeconds: TimeInterval = 2
    private var cardTask: Task<Void, Never>?

    init(frame: NSRect, art: PreparedArt, settings s: Settings, calmer: Bool) {
        super.init(frame: frame)
        wantsLayer = true
        layer?.masksToBounds = true

        let picture = CALayer()
        let inset = s.cover ? CGSize.zero : CGSize(width: frame.width * 0.035, height: frame.height * 0.035)
        picture.frame = bounds.insetBy(dx: inset.width, dy: inset.height)
        picture.contents = art.image
        picture.contentsGravity = s.cover ? .resizeAspectFill : .resizeAspect
        layer?.addSublayer(picture)
        addMotion(to: picture, settings: s, calmer: calmer)

        if s.showCaption {
            let card = GlassCard(art: art, settings: s, sceneSize: frame.size)
            addSubview(card)
            cardTask = card.runTimeline(infoSeconds: s.infoSeconds)
        }
    }

    required init?(coder: NSCoder) { fatalError("not used") }
    deinit { cardTask?.cancel() }

    /// On battery the motion is dialled down a notch (Ken Burns becomes a subtle drift, a drift becomes still).
    private func addMotion(to picture: CALayer, settings s: Settings, calmer: Bool) {
        var drift = s.drift
        var kenBurns = s.motion == "ken_burns"
        if calmer { drift = kenBurns ? 0.05 : 0; kenBurns = false }
        guard drift > 0 else { return }
        let zoomIn = !kenBurns || Bool.random()
        let a = zoomIn ? 1.0 : 1 + drift, b = zoomIn ? 1 + drift : 1.0
        let dx: CGFloat = Bool.random() ? -1 : 1, dy: CGFloat = Bool.random() ? -0.5 : 0.5
        let w = bounds.width, h = bounds.height
        func room(_ scale: Double, _ size: CGFloat) -> CGFloat { CGFloat((scale - 1) / 2) * size * 0.9 }     // spare room the zoom gives
        func transform(_ scale: Double, _ x: CGFloat, _ y: CGFloat) -> CATransform3D {
            CATransform3DScale(CATransform3DMakeTranslation(x, y, 0), CGFloat(scale), CGFloat(scale), 1)
        }
        let anim = CABasicAnimation(keyPath: "transform")
        anim.fromValue = NSValue(caTransform3D: transform(a, dx * room(a, w), dy * room(a, h)))
        anim.toValue = NSValue(caTransform3D: transform(b, -dx * room(b, w), -dy * room(b, h)))
        anim.duration = Double(s.secondsPerPainting) + ArtView.crossfadeSeconds + 1
        anim.timingFunction = CAMediaTimingFunction(name: .linear)
        anim.fillMode = .forwards
        anim.isRemovedOnCompletion = false
        picture.add(anim, forKey: "motion")
        picture.transform = transform(b, -dx * room(b, w), -dy * room(b, h))
    }
}

/// Process-wide curator, so two monitors never show the same painting at once. Rebuilt if the settings change.
private enum Shared {
    static let lock = NSLock()
    static var curator: Curator?
    static var settings: Settings?
    static var catalog: Catalog?
    static let prefs = Preferences()

    static func instance(for s: Settings, catalog loader: () -> Catalog?) -> Curator {
        lock.lock(); defer { lock.unlock() }
        if let c = curator, settings == s { return c }
        if catalog == nil { catalog = loader() }
        settings = s
        let c = Curator(settings: s, prefs: prefs, catalog: catalog)
        curator = c
        return c
    }
}

@objc(MetSaverView)
final class MetSaverView: ScreenSaverView {
    private var settings = Settings.load()
    private var runner: Task<Void, Never>?
    private var loading: LoadingOverlay?
    private let dimLayer = NSView()
    private var ticks = 0
    private var config: ConfigController?

    override init?(frame: NSRect, isPreview: Bool) {
        super.init(frame: frame, isPreview: isPreview)
        setup()
    }

    required init?(coder: NSCoder) {
        super.init(coder: coder)
        setup()
    }

    private func setup() {
        animationTimeInterval = 1          // only used to refresh ambient dimming; the motion is Core Animation
        wantsLayer = true
        layer?.backgroundColor = Self.color(settings.matColor).cgColor
        dimLayer.wantsLayer = true
        dimLayer.layer?.backgroundColor = NSColor.black.cgColor
        dimLayer.alphaValue = 0
    }

    static func color(_ hex: String) -> NSColor {
        var h = hex.trimmingCharacters(in: CharacterSet(charactersIn: "#"))
        if h.count == 3 { h = h.map { "\($0)\($0)" }.joined() }
        guard h.count == 6, let v = UInt32(h, radix: 16) else { return NSColor(srgbRed: 0.09, green: 0.08, blue: 0.06, alpha: 1) }
        return NSColor(srgbRed: CGFloat((v >> 16) & 255) / 255, green: CGFloat((v >> 8) & 255) / 255, blue: CGFloat(v & 255) / 255, alpha: 1)
    }

    // MARK: lifecycle

    override func startAnimation() {
        super.startAnimation()
        settings = Settings.load()
        layer?.backgroundColor = Self.color(settings.matColor).cgColor
        runner?.cancel()
        if dimLayer.superview == nil { dimLayer.frame = bounds; dimLayer.autoresizingMask = [.width, .height]; addSubview(dimLayer) }
        updateDim()
        let s = settings
        let curator = isPreview ? nil : Shared.instance(for: s, catalog: Self.loadCatalog)
        runner = Task { [weak self] in await self?.run(curator: curator, settings: s) }
    }

    override func stopAnimation() {
        super.stopAnimation()
        runner?.cancel()
        runner = nil
        loading?.stop()
    }

    override func animateOneFrame() {
        ticks += 1
        if ticks % 30 == 0 { updateDim() }
    }

    // MARK: settings sheet

    override var hasConfigureSheet: Bool { true }
    override var configureSheet: NSWindow? {
        let c = ConfigController()
        config = c
        return c.window
    }

    // MARK: ambient

    /// The Mac has no public light-sensor API, so dimming follows the clock: a touch dimmer late at night.
    private func updateDim() {
        var dim = 1.0
        if settings.ambientAware && !isPreview {
            let h = Calendar.current.component(.hour, from: Date())
            dim = (h >= 23 || h < 6) ? 0.7 : (h >= 21 || h < 7) ? 0.85 : 1.0
        }
        NSAnimationContext.runAnimationGroup { ctx in
            ctx.duration = 3
            dimLayer.animator().alphaValue = CGFloat(1 - dim)
        }
    }

    static var onBattery: Bool {
        guard let blob = IOPSCopyPowerSourcesInfo()?.takeRetainedValue(),
              let type = IOPSGetProvidingPowerSourceType(blob)?.takeUnretainedValue() as String? else { return false }
        return type == "Battery Power"
    }

    // MARK: catalog

    private static func loadCatalog() -> Catalog? {
        let bundle = Bundle(for: MetSaverView.self)
        if let path = bundle.path(forResource: "catalog.json", ofType: "gz"), let cat = Catalog.load(file: URL(fileURLWithPath: path)) {
            Log.info("catalog: \(cat.entries.count) paintings")
            return cat
        }
        Log.error("catalog.json.gz not found in the bundle")
        return nil
    }

    // MARK: the show

    private func pixelSize() -> (Int, Int) {
        let scale = window?.backingScaleFactor ?? NSScreen.main?.backingScaleFactor ?? 2
        return (max(1, Int(bounds.width * scale)), max(1, Int(bounds.height * scale)))
    }

    private func run(curator: Curator?, settings s: Settings) async {
        let (pw, ph) = await MainActor.run { pixelSize() }
        if loading == nil && !isPreview {
            await MainActor.run {
                let overlay = LoadingOverlay(sceneSize: bounds.size)
                overlay.frame = bounds; overlay.autoresizingMask = [.width, .height]
                addSubview(overlay, positioned: .below, relativeTo: dimLayer)
                loading = overlay
            }
        }

        func makeNext(first: Bool) -> Task<PreparedArt?, Never> {
            Task.detached(priority: .utility) { [weak self] in
                var art: Artwork? = (first || curator == nil) ? Cache.pick() : nil
                if art == nil, let curator = curator {
                    art = await curator.next(screenW: pw, screenH: ph) { st in
                        DispatchQueue.main.async { self?.loading?.update(st) }
                    } ?? Cache.pick()
                }
                guard let a = art else { return nil }
                return ImagePrep.prepare(a, settings: s, screenW: pw, screenH: ph)
            }
        }

        var next = makeNext(first: true)
        while !Task.isCancelled {
            let prepared = await next.value
            if Task.isCancelled { break }
            next = makeNext(first: false)                       // prefetch the following painting while this one is shown
            guard let p = prepared else { try? await Task.sleep(nanoseconds: 10_000_000_000); continue }
            await MainActor.run { show(p, settings: s) }
            try? await Task.sleep(nanoseconds: UInt64(s.secondsPerPainting) * 1_000_000_000)
        }
    }

    @MainActor private func show(_ art: PreparedArt, settings s: Settings) {
        let view = ArtView(frame: bounds, art: art, settings: isPreview ? previewSettings(s) : s, calmer: s.ambientAware && Self.onBattery)
        view.autoresizingMask = [.width, .height]
        view.alphaValue = 0
        addSubview(view, positioned: .below, relativeTo: dimLayer)
        if let overlay = loading {
            loading = nil
            overlay.stop()
            NSAnimationContext.runAnimationGroup({ ctx in ctx.duration = ArtView.crossfadeSeconds; overlay.animator().alphaValue = 0 },
                                                 completionHandler: { overlay.removeFromSuperview() })
        }
        NSAnimationContext.runAnimationGroup({ ctx in
            ctx.duration = ArtView.crossfadeSeconds
            view.animator().alphaValue = 1
        }, completionHandler: { [weak self, weak view] in
            guard let self = self, let view = view else { return }
            for sub in self.subviews where sub is ArtView && sub !== view { sub.removeFromSuperview() }    // drop old paintings underneath
        })
    }

    private func previewSettings(_ s: Settings) -> Settings {
        var p = s; p.showCaption = false; p.motion = "off"; return p
    }
}
