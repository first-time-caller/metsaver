import AppKit
#if canImport(MetSaverCore)
import MetSaverCore
#endif

/// Frosted-glass card: a blurred, tinted backdrop behind the title, artist, date and medium.
final class GlassCard: NSView {
    init(art: PreparedArt, settings s: Settings, sceneSize: CGSize, details: Bool = false, favourite: Bool = false) {
        super.init(frame: .zero)
        wantsLayer = true
        let c = art.caption
        let u = max(sceneSize.height / 66, 11)
        let pad = u * 1.4, radius = u * 1.2, margin = u * 2.2
        let cardW = details ? min(max(sceneSize.width * 0.36, 380), 800) : min(max(sceneSize.width * 0.27, 280), 640)
        let accent = NSColor(srgbRed: min(1, art.tint.redComponent * 0.5 + 0.55), green: min(1, art.tint.greenComponent * 0.5 + 0.55),
                             blue: min(1, art.tint.blueComponent * 0.5 + 0.55), alpha: 1)

        func label(_ text: String, size: CGFloat, color: NSColor, weight: NSFont.Weight = .regular, lines: Int = 3) -> NSTextField {
            let t = NSTextField(wrappingLabelWithString: text)
            t.font = NSFont.systemFont(ofSize: size, weight: weight)
            t.textColor = color
            t.maximumNumberOfLines = lines
            t.lineBreakMode = .byTruncatingTail
            t.preferredMaxLayoutWidth = cardW - 2 * pad
            t.isSelectable = false
            return t
        }
        func white(_ a: CGFloat) -> NSColor { NSColor(white: a, alpha: 1) }

        var rows: [NSView] = [label(c.title, size: u * 1.5, color: .white, weight: .semibold)]
        let rule = NSView(); rule.wantsLayer = true; rule.layer?.backgroundColor = accent.cgColor
        rule.translatesAutoresizingMaskIntoConstraints = false
        rule.widthAnchor.constraint(equalToConstant: u * 3).isActive = true
        rule.heightAnchor.constraint(equalToConstant: max(2, u / 7)).isActive = true
        rows.append(rule)
        rows.append(label(c.artist, size: u * 1.1, color: white(0.92), lines: 2))
        if details && !c.bio.isEmpty { rows.append(label(c.bio, size: u * 1.0, color: white(0.73), lines: 2)) }
        let detail = [c.date, c.medium].filter { !$0.isEmpty }.joined(separator: " · ")
        if !detail.isEmpty { rows.append(label(detail, size: u * 1.1, color: white(0.8), lines: details ? 4 : 3)) }
        if details {
            if !c.dimensions.isEmpty { rows.append(label("Size  \(c.dimensions)", size: u, color: white(0.73))) }
            if !c.credit.isEmpty { rows.append(label("Credit  \(c.credit)", size: u, color: white(0.73))) }
            rows.append(label(c.gallery.isEmpty ? "Not currently on view" : "On view in Gallery \(c.gallery)", size: u, color: white(0.8), lines: 1))
        }
        rows.append(label("The Metropolitan Museum of Art · Public domain", size: u * 1.1, color: white(0.73), lines: 2))
        if details {
            rows.append(label(favourite ? "★ In your favourites" : "☆ Not a favourite yet", size: u * 1.1,
                              color: NSColor(srgbRed: 0.92, green: 0.88, blue: 0.75, alpha: 1), weight: .semibold, lines: 1))
        }

        let stack = NSStackView(views: rows)
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = u * 0.35
        stack.edgeInsets = NSEdgeInsets(top: pad, left: pad, bottom: pad, right: pad)
        stack.translatesAutoresizingMaskIntoConstraints = false
        stack.widthAnchor.constraint(equalToConstant: cardW).isActive = true
        let cardH = ceil(stack.fittingSize.height)

        let blur = NSVisualEffectView(frame: NSRect(x: 0, y: 0, width: cardW, height: cardH))
        blur.material = .hudWindow
        blur.blendingMode = .withinWindow
        blur.state = .active
        let tint = NSView(frame: blur.frame)
        tint.wantsLayer = true
        tint.layer?.backgroundColor = art.tint.withAlphaComponent(0.55).cgColor

        // Place in the chosen corner (AppKit's origin is bottom-left).
        let left = s.infoCorner.contains("left"), top = s.infoCorner.contains("top")
        let x = left ? margin : sceneSize.width - margin - cardW
        let y = top ? sceneSize.height - margin - cardH : margin
        frame = NSRect(x: x, y: y, width: cardW, height: cardH)
        layer?.cornerRadius = radius
        layer?.masksToBounds = true
        layer?.borderWidth = max(1, u / 10)
        layer?.borderColor = accent.withAlphaComponent(0.47).cgColor

        addSubview(blur); addSubview(tint); addSubview(stack)
        stack.leadingAnchor.constraint(equalTo: leadingAnchor).isActive = true
        stack.topAnchor.constraint(equalTo: topAnchor).isActive = true
        alphaValue = 0
    }

    required init?(coder: NSCoder) { fatalError("not used") }

    /// Fade in at 1.5 s, hold, fade out after `infoSeconds` (0 = stay). Tasks are cancelled when the owner goes away.
    func runTimeline(infoSeconds: Int) -> Task<Void, Never> {
        Task { @MainActor [weak self] in
            try? await Task.sleep(nanoseconds: 1_500_000_000)
            guard !Task.isCancelled else { return }
            self?.fade(to: 1, seconds: 1)
            guard infoSeconds > 0 else { return }
            try? await Task.sleep(nanoseconds: UInt64(max(3, infoSeconds) * 1_000_000_000))
            guard !Task.isCancelled else { return }
            self?.fade(to: 0, seconds: 1.5)
        }
    }

    func fade(to value: CGFloat, seconds: TimeInterval) {
        NSAnimationContext.runAnimationGroup { ctx in
            ctx.duration = seconds
            animator().alphaValue = value
        }
    }
}

/// Shown on an empty screen while the first painting is found: a spinner, what's happening, a progress bar and a time estimate.
final class LoadingOverlay: NSView {
    private let message = NSTextField(labelWithString: "Starting…")
    private let detail = NSTextField(labelWithString: "")
    private let eta = NSTextField(labelWithString: "")
    private let bar = NSProgressIndicator()
    private let spinner = NSProgressIndicator()
    private let started = Date()
    private var last = LoadStatus("Starting…")
    private var timer: Timer?

    init(sceneSize: CGSize) {
        super.init(frame: NSRect(origin: .zero, size: sceneSize))
        let u = max(sceneSize.height / 66, 11)
        let soft = NSColor(srgbRed: 0.84, green: 0.82, blue: 0.75, alpha: 1)
        let dim = NSColor(srgbRed: 0.59, green: 0.57, blue: 0.52, alpha: 1)

        spinner.style = .spinning; spinner.isIndeterminate = true; spinner.controlSize = .regular; spinner.startAnimation(nil)
        message.font = .systemFont(ofSize: u * 1.5); message.textColor = soft; message.alignment = .center
        detail.font = .systemFont(ofSize: u * 1.1); detail.textColor = dim; detail.alignment = .center; detail.lineBreakMode = .byTruncatingTail
        eta.font = .systemFont(ofSize: u); eta.textColor = dim; eta.alignment = .center
        bar.style = .bar; bar.isIndeterminate = true; bar.minValue = 0; bar.maxValue = 1; bar.startAnimation(nil)
        bar.translatesAutoresizingMaskIntoConstraints = false
        bar.widthAnchor.constraint(equalToConstant: u * 22).isActive = true

        let stack = NSStackView(views: [spinner, message, detail, bar, eta])
        stack.orientation = .vertical; stack.alignment = .centerX; stack.spacing = u * 0.6
        stack.translatesAutoresizingMaskIntoConstraints = false
        addSubview(stack)
        stack.centerXAnchor.constraint(equalTo: centerXAnchor).isActive = true
        stack.centerYAnchor.constraint(equalTo: centerYAnchor).isActive = true
        stack.widthAnchor.constraint(lessThanOrEqualToConstant: u * 36).isActive = true

        // Keep the "N s so far" line ticking even when the loader has nothing new to say.
        timer = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { [weak self] _ in
            guard let self = self else { return }
            self.update(self.last)
        }
    }

    required init?(coder: NSCoder) { fatalError("not used") }
    deinit { timer?.invalidate() }

    func update(_ st: LoadStatus) {
        last = st
        message.stringValue = st.message
        detail.stringValue = st.detail ?? ""
        detail.isHidden = (st.detail ?? "").isEmpty
        if let f = st.fraction { bar.isIndeterminate = false; bar.doubleValue = min(max(f, 0), 1) }
        else { bar.isIndeterminate = true; bar.startAnimation(nil) }
        let elapsed = Int(Date().timeIntervalSince(started))
        if let e = st.eta { eta.stringValue = "About \(max(1, Int(e.rounded(.up)))) s left" }
        else { eta.stringValue = elapsed < 4 ? "" : "\(elapsed) s so far · first painting usually takes a few seconds" }
    }

    func stop() { timer?.invalidate(); timer = nil }
}
