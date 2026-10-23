#!/usr/bin/env bash
# Builds build/MetSaver.saver (universal: Apple silicon + Intel) and a zip + dmg to ship.
#   macos/scripts/build-saver.sh                      ad-hoc signed (fine for local use; Gatekeeper will ask on other Macs)
#   SIGN_IDENTITY="Developer ID Application: ..." macos/scripts/build-saver.sh    signed for distribution
# Requires Xcode command-line tools and XcodeGen (brew install xcodegen).
set -euo pipefail
cd "$(dirname "$0")/.."

command -v xcodegen >/dev/null || { echo "XcodeGen not found: brew install xcodegen" >&2; exit 1; }

# The vetted catalog is shared with the Windows app; it ships inside the bundle (about 140 KB).
cp ../data/catalog.json.gz Resources/catalog.json.gz

xcodegen generate --quiet

IDENTITY="${SIGN_IDENTITY:--}"
rm -rf build
xcodebuild -project MetSaver.xcodeproj -scheme MetSaver -configuration Release \
  -derivedDataPath build/derived \
  CODE_SIGN_IDENTITY="$IDENTITY" CODE_SIGN_STYLE=Manual \
  ${SIGN_IDENTITY:+OTHER_CODE_SIGN_FLAGS=--timestamp} \
  build | tail -n 25

SAVER="build/derived/Build/Products/Release/MetSaver.saver"
[ -d "$SAVER" ] || { echo "build did not produce MetSaver.saver" >&2; exit 1; }
mkdir -p build/out
cp -R "$SAVER" build/out/MetSaver.saver

echo "--- architectures:"; lipo -archs build/out/MetSaver.saver/Contents/MacOS/MetSaver
echo "--- signature:";     codesign -dv build/out/MetSaver.saver 2>&1 | head -n 6 || true

( cd build/out && ditto -c -k --keepParent MetSaver.saver MetSaver-mac.zip )

# A dmg with the saver and a short how-to (double-clicking a .saver installs it for the current user).
STAGE="$(mktemp -d)"
cp -R build/out/MetSaver.saver "$STAGE/"
cat > "$STAGE/Install.txt" <<'TXT'
Double-click MetSaver.saver. System Settings opens and offers to install it for you.
Then choose it under Screen Saver. If macOS says it can't verify the developer:
System Settings > Privacy & Security > "Open Anyway", then double-click again.
TXT
hdiutil create -quiet -volname "MetSaver" -srcfolder "$STAGE" -ov -format UDZO build/out/MetSaver-mac.dmg
echo "built: build/out/MetSaver-mac.dmg  build/out/MetSaver-mac.zip"
