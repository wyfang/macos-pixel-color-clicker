// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "PixelWatcher",
    platforms: [.macOS(.v13)],
    products: [
        .executable(name: "PixelWatcher", targets: ["PixelWatcher"])
    ],
    targets: [
        .executableTarget(name: "PixelWatcher")
    ],
    swiftLanguageModes: [.v5]
)
