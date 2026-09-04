#!/usr/bin/env bash
# Builds a self-contained, single-file linux-x64 publish of Meshwright.App
# and packages it as a .deb. AppImage is left for a follow-up (needs
# appimagetool, not present on this dev host) - see SPECIFICATION.md §7 M4-3.
#
# Output: artifacts/linux/meshwright_<version>_amd64.deb
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="${MESHWRIGHT_VERSION:-0.1.0}"
RID="linux-x64"

PUBLISH_DIR="$REPO_ROOT/artifacts/linux/publish"
PKG_ROOT="$REPO_ROOT/artifacts/linux/deb-root"
OUT_DEB="$REPO_ROOT/artifacts/linux/meshwright_${VERSION}_amd64.deb"

rm -rf "$PUBLISH_DIR" "$PKG_ROOT" "$OUT_DEB"
mkdir -p "$PUBLISH_DIR" "$PKG_ROOT"

echo "==> Publishing self-contained single-file build ($RID)"
dotnet publish "$REPO_ROOT/src/Meshwright.App/Meshwright.App.csproj" \
  --configuration Release \
  --runtime "$RID" \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:Version="$VERSION" \
  --output "$PUBLISH_DIR"

# Single-file publish still emits loose .pdb symbol files alongside the
# executable; the shipped package only needs the executable itself.
find "$PUBLISH_DIR" -name '*.pdb' -delete

echo "==> Assembling .deb package tree"
INSTALL_DIR="$PKG_ROOT/opt/meshwright"
mkdir -p "$INSTALL_DIR" "$PKG_ROOT/usr/bin" "$PKG_ROOT/usr/share/applications" "$PKG_ROOT/DEBIAN"
cp "$PUBLISH_DIR/Meshwright.App" "$INSTALL_DIR/meshwright"
chmod 755 "$INSTALL_DIR/meshwright"
ln -sf /opt/meshwright/meshwright "$PKG_ROOT/usr/bin/meshwright"

cat > "$PKG_ROOT/usr/share/applications/meshwright.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=Meshwright
Comment=Repair and prepare meshes for 3D printing
Exec=/usr/bin/meshwright
Icon=meshwright
Terminal=false
Categories=Graphics;3DGraphics;
EOF

INSTALLED_SIZE_KB=$(du -sk "$PKG_ROOT/opt" | cut -f1)

cat > "$PKG_ROOT/DEBIAN/control" <<EOF
Package: meshwright
Version: $VERSION
Section: graphics
Priority: optional
Architecture: amd64
Installed-Size: $INSTALLED_SIZE_KB
Maintainer: Meshwright <noreply@example.invalid>
Description: Repair and prepare meshes for 3D printing
 A focused, cross-platform desktop tool for repairing and preparing
 meshes for 3D printing - the Meshmixer successor that never arrived.
EOF

mkdir -p "$(dirname "$OUT_DEB")"
dpkg-deb --build --root-owner-group "$PKG_ROOT" "$OUT_DEB"

echo "==> Built $OUT_DEB"
