// swift-tools-version:5.9
import PackageDescription

// MetSaverCore holds everything that isn't UI: settings, catalog, vetting, mood, the Met API client and the curator.
// The screensaver bundle itself (Sources/MetSaverSaver) is compiled by scripts/build-saver.sh, because a .saver is a
// loadable bundle that SwiftPM can't produce on its own.
let package = Package(
    name: "MetSaver",
    platforms: [.macOS(.v12)],
    products: [
        .library(name: "MetSaverCore", targets: ["MetSaverCore"]),
    ],
    targets: [
        .target(name: "MetSaverCore"),
        .testTarget(name: "MetSaverCoreTests", dependencies: ["MetSaverCore"], exclude: ["fixture.b64"]),
    ]
)
