#!/usr/bin/env bash
# Builds a self-contained, single-file osx-<arch> publish of Meshwright.App,
# wraps it in a minimal .app bundle, and zips that up. Mirrors
# scripts/package-linux.sh's structure. No code signing or notarization -
# explicitly out of scope per SPECIFICATION.md §9 ("Linux + Windows first;
# macOS once there is revenue"). An unsigned .app downloaded from the
# internet will be Gatekeeper-blocked on a real Mac (right-click -> Open,
# or `xattr -d com.apple.quarantine`, works around it once but is a real
# friction point for actual users) - see the NOTES section below for what
# signing/notarization would need once that's in scope.
#
# STATUS: UNVERIFIED beyond the `dotnet publish` step, which was confirmed
# to cross-target-build from this (Linux) dev host for both osx-arm64 and
# osx-x64 (see reports/M4). The .app bundle assembly and zip step have
# never been run on this host; launching the resulting bundle was not and
# cannot be tested here. Intended to run natively on a GitHub Actions
# macos-latest runner (Apple Silicon).
#
# Output: artifacts/macos/Meshwright_<version>_<rid>.zip (containing
# Meshwright.app)
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="${MESHWRIGHT_VERSION:-0.1.0}"
RID="${MESHWRIGHT_MACOS_RID:-osx-arm64}"

case "$RID" in
  osx-arm64|osx-x64) ;;
  *)
    echo "error: MESHWRIGHT_MACOS_RID must be osx-arm64 or osx-x64, got '$RID'" >&2
    exit 1
    ;;
esac

PUBLISH_DIR="$REPO_ROOT/artifacts/macos/publish-$RID"
APP_DIR="$REPO_ROOT/artifacts/macos/Meshwright.app"
OUT_ZIP="$REPO_ROOT/artifacts/macos/Meshwright_${VERSION}_${RID}.zip"

rm -rf "$PUBLISH_DIR" "$APP_DIR" "$OUT_ZIP"
mkdir -p "$PUBLISH_DIR"

echo "==> Publishing self-contained single-file build ($RID)"
dotnet publish "$REPO_ROOT/src/Meshwright.App/Meshwright.App.csproj" \
  --configuration Release \
  --runtime "$RID" \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:Version="$VERSION" \
  --output "$PUBLISH_DIR"

find "$PUBLISH_DIR" -name '*.pdb' -delete

# --- Manifold native library: same honest handling as package-windows.sh --
MANIFOLD_NATIVE_DIR="$REPO_ROOT/runtimes/$RID/native"
if [ -d "$MANIFOLD_NATIVE_DIR" ] && [ -n "$(ls -A "$MANIFOLD_NATIVE_DIR" 2>/dev/null)" ]; then
  echo "==> Manifold native library present for $RID - Boolean will work in this package"
  BOOLEAN_STATUS="enabled"
else
  echo "==> WARNING: no Manifold native library for $RID (expected at $MANIFOLD_NATIVE_DIR)."
  echo "    This package's Boolean operation will throw DllNotFoundException at"
  echo "    runtime rather than working. See NOTICE.txt in the bundle and"
  echo "    reports/M4 for why. Every other feature is unaffected."
  BOOLEAN_STATUS="disabled"
fi

echo "==> Assembling .app bundle"
mkdir -p "$APP_DIR/Contents/MacOS" "$APP_DIR/Contents/Resources"
cp -r "$PUBLISH_DIR"/. "$APP_DIR/Contents/MacOS/"
chmod 755 "$APP_DIR/Contents/MacOS/Meshwright.App"

cat > "$APP_DIR/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>Meshwright</string>
    <key>CFBundleDisplayName</key>
    <string>Meshwright</string>
    <key>CFBundleIdentifier</key>
    <string>com.meshwright.app</string>
    <key>CFBundleVersion</key>
    <string>$VERSION</string>
    <key>CFBundleShortVersionString</key>
    <string>$VERSION</string>
    <key>CFBundleExecutable</key>
    <string>Meshwright.App</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>LSMinimumSystemVersion</key>
    <string>11.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
</dict>
</plist>
EOF

if [ "$BOOLEAN_STATUS" = "disabled" ]; then
  cat > "$APP_DIR/Contents/Resources/NOTICE.txt" <<'EOF'
Meshwright for macOS - known limitation in this build
=======================================================

The Boolean operation (union / difference / intersection) is NOT available
in this build. It depends on a native library (Manifold) that has not yet
been built for macOS. Clicking Boolean will fail with an error rather than
silently doing nothing incorrect.

Every other feature works normally: Inspect, the rest of Repair, plane
cut, transforms, hollow, drain holes, decimation, and mesh import/export.

Tracked as an open item in SPECIFICATION.md, milestone M4-3.
EOF
  echo "==> Wrote NOTICE.txt (Boolean unavailable in this build)"
fi

echo "==> Zipping bundle"
mkdir -p "$(dirname "$OUT_ZIP")"
# ditto (not plain zip) preserves the resource forks / extended attributes
# .app bundles rely on and is what Apple's own tooling (and notarization)
# expects; it's preinstalled on every macOS system, unlike `zip`.
if command -v ditto >/dev/null 2>&1; then
  ditto -c -k --sequesterRsrc --keepParent "$APP_DIR" "$OUT_ZIP"
elif command -v zip >/dev/null 2>&1; then
  (cd "$REPO_ROOT/artifacts/macos" && zip -qr "$(basename "$OUT_ZIP")" "$(basename "$APP_DIR")")
else
  echo "error: neither 'ditto' nor 'zip' found to create the archive" >&2
  exit 1
fi

echo "==> Built $OUT_ZIP (Boolean: $BOOLEAN_STATUS)"
echo
echo "==> NOTES: code signing / notarization (not done, out of scope per §9)"
echo "    Once macOS distribution is in scope, this would need:"
echo "      - an Apple Developer ID Application certificate,"
echo "      - 'codesign --deep --force --options runtime --sign <identity> Meshwright.app',"
echo "      - hardened runtime entitlements if any (none needed today - no camera/mic/etc access),"
echo "      - 'xcrun notarytool submit ... --wait' against the zipped bundle, then"
echo "      - 'xcrun stapler staple Meshwright.app' before re-zipping for distribution."
echo "    None of this can be exercised without an Apple Developer Program"
echo "    enrollment (\$99/yr) and a real Mac (or macOS CI runner with the cert"
echo "    installed in its keychain) - neither available in this environment."
