#!/usr/bin/env bash
# Builds a self-contained, single-file win-x64 publish of Meshwright.App
# and zips it up. Mirrors scripts/package-linux.sh's structure; a zipped
# folder rather than an MSI/installer, since building an MSI needs WiX (or
# similar) tooling this isn't set up for yet - a follow-up once there's a
# reason to want an actual installer (Start Menu entry, uninstaller, etc.)
# rather than "unzip and run".
#
# STATUS: UNVERIFIED beyond the `dotnet publish` step. The publish itself
# was confirmed to succeed cross-target from this (Linux) dev host - see
# reports/M4 for the exact command and output. The zip step is untested
# (zip/7z availability wasn't checked in a Windows shell from here); running
# the produced Meshwright.App.exe was not and cannot be done from this
# host. Intended to run under Git Bash (`shell: bash`) on a GitHub Actions
# windows-latest runner, where `zip` isn't installed by default either -
# see the fallback note below.
#
# Output: artifacts/windows/meshwright_<version>_win-x64.zip
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="${MESHWRIGHT_VERSION:-0.1.0}"
RID="win-x64"

PUBLISH_DIR="$REPO_ROOT/artifacts/windows/publish"
STAGE_DIR="$REPO_ROOT/artifacts/windows/meshwright_${VERSION}_win-x64"
OUT_ZIP="$REPO_ROOT/artifacts/windows/meshwright_${VERSION}_win-x64.zip"

rm -rf "$PUBLISH_DIR" "$STAGE_DIR" "$OUT_ZIP"
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

# --- Manifold native library: honest handling, not silent breakage --------
# ManifoldInterop.cs P/Invokes "libmanifoldc" with no per-OS native binary
# shipped for win-x64 yet (only runtimes/linux-x64/native/ exists in this
# repo as of this batch - see reports/M4). scripts/build-manifold-native-
# windows.sh, run as an earlier CI step, is what would populate
# runtimes/win-x64/native/ before this script runs; if that hasn't
# happened (e.g. running this script standalone on this dev host, or that
# build step failing), ship the package anyway but with Boolean disabled
# and be explicit about it - a mesh-repair tool that crashes when you click
# one specific button is worse than one that visibly can't do that one
# thing yet.
MANIFOLD_NATIVE_DIR="$REPO_ROOT/runtimes/$RID/native"
if [ -d "$MANIFOLD_NATIVE_DIR" ] && [ -n "$(ls -A "$MANIFOLD_NATIVE_DIR" 2>/dev/null)" ]; then
  echo "==> Manifold native library present for $RID - Boolean will work in this package"
  BOOLEAN_STATUS="enabled"
else
  echo "==> WARNING: no Manifold native library for $RID (expected at $MANIFOLD_NATIVE_DIR)."
  echo "    This package's Boolean operation will throw DllNotFoundException at"
  echo "    runtime rather than working - see NOTICE.txt in the package and"
  echo "    reports/M4 for why. Every other feature (Inspect, Repair minus"
  echo "    booleans, plane cut, transforms, hollow, drain holes, decimation,"
  echo "    import/export) is unaffected."
  BOOLEAN_STATUS="disabled"
fi

echo "==> Assembling package folder"
mkdir -p "$STAGE_DIR"
cp -r "$PUBLISH_DIR"/. "$STAGE_DIR/"

if [ "$BOOLEAN_STATUS" = "disabled" ]; then
  cat > "$STAGE_DIR/NOTICE.txt" <<'EOF'
Meshwright for Windows - known limitation in this build
=========================================================

The Boolean operation (union / difference / intersection) is NOT available
in this package. It depends on a native library (Manifold) that has not
yet been built for Windows. Clicking Boolean will fail with an error
rather than silently doing nothing incorrect.

Every other feature works normally: Inspect, the rest of Repair (hole
fill, non-manifold fix, duplicate removal, etc.), plane cut, transforms,
hollow, drain holes, decimation, and mesh import/export.

This is tracked as an open item in the project's own specification
(SPECIFICATION.md, milestone M4-3) and will be resolved once Manifold is
built as part of Windows CI.
EOF
  echo "==> Wrote NOTICE.txt (Boolean unavailable in this build)"
fi

echo "==> Zipping package"
mkdir -p "$(dirname "$OUT_ZIP")"
# `zip` may not be preinstalled on a Windows CI runner; PowerShell's
# Compress-Archive is the built-in fallback there. Prefer `zip` (works
# identically on this Linux dev host and in Git Bash if it happens to be
# available) and fall back to PowerShell if it isn't found.
if command -v zip >/dev/null 2>&1; then
  (cd "$REPO_ROOT/artifacts/windows" && zip -qr "$(basename "$OUT_ZIP")" "$(basename "$STAGE_DIR")")
elif command -v powershell.exe >/dev/null 2>&1; then
  powershell.exe -NoProfile -Command \
    "Compress-Archive -Path '$(cygpath -w "$STAGE_DIR" 2>/dev/null || echo "$STAGE_DIR")\\*' -DestinationPath '$(cygpath -w "$OUT_ZIP" 2>/dev/null || echo "$OUT_ZIP")' -Force"
else
  echo "error: neither 'zip' nor 'powershell.exe' found to create the archive" >&2
  exit 1
fi

echo "==> Built $OUT_ZIP (Boolean: $BOOLEAN_STATUS)"
