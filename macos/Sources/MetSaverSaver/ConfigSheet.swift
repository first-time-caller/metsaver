import AppKit
#if canImport(MetSaverCore)
import MetSaverCore
#endif

/// The "Screen Saver Options…" sheet in System Settings. Built in code so the bundle needs no nib.
final class ConfigController: NSObject {
    let window: NSWindow
    private var settings = Settings.load()

    private let seconds = NSTextField()
    private let hideAfter = NSTextField()
    private let motion = NSPopUpButton()
    private let fill = NSPopUpButton()
    private let corner = NSPopUpButton()
    private let strict = NSSlider(value: 0.8, minValue: 0.5, maxValue: 0.95, target: nil, action: nil)
    private let caption = NSButton(checkboxWithTitle: "Show title, artist and medium", target: nil, action: nil)
    private let mood = NSButton(checkboxWithTitle: "Match the hour and season", target: nil, action: nil)
    private let crop = NSButton(checkboxWithTitle: "Crop toward the interesting part of each painting", target: nil, action: nil)
    private let ambient = NSButton(checkboxWithTitle: "Dim late at night; calmer motion on battery", target: nil, action: nil)
    private var depts: [(Int, NSButton)] = [
        (11, NSButton(checkboxWithTitle: "European Paintings", target: nil, action: nil)),
        (1, NSButton(checkboxWithTitle: "American Paintings", target: nil, action: nil)),
        (21, NSButton(checkboxWithTitle: "Modern & Contemporary (few)", target: nil, action: nil)),
        (6, NSButton(checkboxWithTitle: "Asian Art (scrolls and screens; a narrow match)", target: nil, action: nil)),
    ]

    override init() {
        window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 520, height: 520), styleMask: [.titled], backing: .buffered, defer: false)
        super.init()
        window.title = "MetSaver"

        motion.addItems(withTitles: ["Off (still)", "Subtle drift", "Ken Burns (zoom + pan)"])
        fill.addItems(withTitles: ["Fill the screen (crops edges)", "Whole painting on a mat"])
        corner.addItems(withTitles: ["top-left", "top-right", "bottom-left", "bottom-right"])

        seconds.stringValue = String(settings.secondsPerPainting)
        hideAfter.stringValue = String(settings.infoSeconds)
        motion.selectItem(at: ["off", "subtle", "ken_burns"].firstIndex(of: settings.motion) ?? 1)
        fill.selectItem(at: settings.cover ? 0 : 1)
        corner.selectItem(withTitle: settings.infoCorner)
        strict.doubleValue = settings.minScreenFill
        caption.state = settings.showCaption ? .on : .off
        mood.state = settings.timeOfDay ? .on : .off
        crop.state = settings.smartCrop ? .on : .off
        ambient.state = settings.ambientAware ? .on : .off
        for (id, button) in depts { button.state = settings.departmentIds.contains(id) ? .on : .off }

        func row(_ title: String, _ control: NSView) -> NSStackView {
            let l = NSTextField(labelWithString: title)
            l.alignment = .right
            l.translatesAutoresizingMaskIntoConstraints = false
            l.widthAnchor.constraint(equalToConstant: 190).isActive = true
            let r = NSStackView(views: [l, control]); r.orientation = .horizontal; r.spacing = 12
            return r
        }
        for f in [seconds, hideAfter] { f.translatesAutoresizingMaskIntoConstraints = false; f.widthAnchor.constraint(equalToConstant: 70).isActive = true }
        let deptStack = NSStackView(views: depts.map { $0.1 }); deptStack.orientation = .vertical; deptStack.alignment = .leading; deptStack.spacing = 4
        let smart = NSStackView(views: [caption, mood, crop, ambient]); smart.orientation = .vertical; smart.alignment = .leading; smart.spacing = 4

        let ok = NSButton(title: "OK", target: self, action: #selector(save)); ok.keyEquivalent = "\r"
        let cancel = NSButton(title: "Cancel", target: self, action: #selector(close))
        let buttons = NSStackView(views: [cancel, ok]); buttons.orientation = .horizontal; buttons.spacing = 10

        let note = NSTextField(wrappingLabelWithString: "Changes apply the next time the screensaver starts. Photos are public-domain paintings from The Metropolitan Museum of Art.")
        note.font = .systemFont(ofSize: 11); note.textColor = .secondaryLabelColor

        let stack = NSStackView(views: [
            row("Seconds per painting", seconds), row("Painting size", fill), row("Shape match (higher = less cropping)", strict),
            row("Motion", motion), row("Info card", smart), row("Info card corner", corner), row("Hide card after (s, 0 = never)", hideAfter),
            row("Collections", deptStack), note, buttons,
        ])
        stack.orientation = .vertical; stack.alignment = .leading; stack.spacing = 12
        stack.edgeInsets = NSEdgeInsets(top: 20, left: 20, bottom: 20, right: 20)
        stack.translatesAutoresizingMaskIntoConstraints = false
        window.contentView = NSView()
        window.contentView!.addSubview(stack)
        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: window.contentView!.leadingAnchor),
            stack.topAnchor.constraint(equalTo: window.contentView!.topAnchor),
            note.widthAnchor.constraint(equalToConstant: 470),
        ])
        window.contentView!.layoutSubtreeIfNeeded()
        window.setContentSize(stack.fittingSize)
    }

    @objc private func save() {
        var s = settings
        s.secondsPerPainting = Int(seconds.stringValue) ?? s.secondsPerPainting
        s.infoSeconds = Int(hideAfter.stringValue) ?? s.infoSeconds
        s.motion = ["off", "subtle", "ken_burns"][max(0, motion.indexOfSelectedItem)]
        s.fillMode = fill.indexOfSelectedItem == 0 ? "cover" : "fit"
        s.infoCorner = corner.titleOfSelectedItem ?? s.infoCorner
        s.minScreenFill = (strict.doubleValue * 100).rounded() / 100
        s.showCaption = caption.state == .on
        s.timeOfDay = mood.state == .on
        s.smartCrop = crop.state == .on
        s.ambientAware = ambient.state == .on
        s.departmentIds = depts.filter { $0.1.state == .on }.map { $0.0 }
        s.normalize()
        s.save()
        close()
    }

    @objc private func close() {
        if let parent = window.sheetParent { parent.endSheet(window) } else { NSApp.endSheet(window) }
        window.orderOut(nil)
    }
}
