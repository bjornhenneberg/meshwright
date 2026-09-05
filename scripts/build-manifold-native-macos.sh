#!/usr/bin/env bash
# Builds Manifold's C API (manifoldc) from source on macOS and installs the
# resulting dylibs into runtimes/osx-<arch>/native/, mirroring
# scripts/build-manifold-native.sh (Linux). Intended to run on a GitHub
# Actions macos-latest runner (Apple Silicon; ships Xcode command line
# tools and a CMake install via the runner image's toolcache) as a CI
# build step.
#
# STATUS: UNVERIFIED. Written and reviewed on a Linux dev host - there is
# no Mac available here to run it, and cross-compiling a macOS native
# library from Linux is not attempted (no osxcross toolchain set up, and
# it wouldn't be a faithful stand-in for the real build environment
# anyway). `bash -n` syntax-checks clean; nothing more. Its first real run
# will be the first CI job that invokes it. See reports/M4.
#
# Requires (present on GitHub's macos-latest image, not otherwise checked
# for here): cmake, Xcode command line tools (clang/make), network access
# to github.com. Builds for the host architecture only - arm64 on
# macos-latest. An Intel (osx-x64) build needs either a macos-13 (Intel)
# runner or -DCMAKE_OSX_ARCHITECTURES=x86_64 added below; neither is done
# yet (see the report for why this was left for a follow-up).
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TOOLS_DIR="$REPO_ROOT/.tools"

MANIFOLD_TAG="v3.5.2"
MANIFOLD_SRC_ROOT="$TOOLS_DIR/src"
MANIFOLD_SRC_DIR="$MANIFOLD_SRC_ROOT/manifold-${MANIFOLD_TAG#v}"
BUILD_DIR="$TOOLS_DIR/build/manifold-${MANIFOLD_TAG}-macos"

# uname -m on Apple Silicon reports "arm64"; .NET's RID uses the same word.
HOST_ARCH="$(uname -m)"
case "$HOST_ARCH" in
  arm64) RID="osx-arm64" ;;
  x86_64) RID="osx-x64" ;;
  *)
    echo "error: unrecognised macOS architecture '$HOST_ARCH'" >&2
    exit 1
    ;;
esac
OUT_DIR="$REPO_ROOT/runtimes/$RID/native"

mkdir -p "$TOOLS_DIR"

# --- 1. Pinned Manifold source (same tag as the Linux/Windows builds) ------
if [ ! -d "$MANIFOLD_SRC_DIR" ]; then
  echo "==> Fetching Manifold ${MANIFOLD_TAG} source"
  mkdir -p "$MANIFOLD_SRC_ROOT"
  TARBALL="$TOOLS_DIR/manifold-${MANIFOLD_TAG}.tar.gz"
  curl -sL -o "$TARBALL" \
    "https://github.com/elalish/manifold/archive/refs/tags/${MANIFOLD_TAG}.tar.gz"
  tar xzf "$TARBALL" -C "$MANIFOLD_SRC_ROOT"
  rm "$TARBALL"
fi

# --- 2. Configure + build the C API only ------------------------------------
# Same flag set as the Linux build (see build-manifold-native.sh). RPATH
# handling differs from Linux's $ORIGIN: macOS's equivalent token is
# @loader_path, and CMake understands it directly in CMAKE_BUILD_RPATH.
echo "==> Configuring (Release, C API only, $RID)"
cmake -S "$MANIFOLD_SRC_DIR" -B "$BUILD_DIR" \
  -DCMAKE_BUILD_TYPE=Release \
  -DBUILD_SHARED_LIBS=ON \
  -DCMAKE_BUILD_RPATH_USE_ORIGIN=ON \
  -DCMAKE_BUILD_RPATH='@loader_path' \
  -DMANIFOLD_CBIND=ON \
  -DMANIFOLD_CROSS_SECTION=ON \
  -DMANIFOLD_PYBIND=OFF \
  -DMANIFOLD_JSBIND=OFF \
  -DMANIFOLD_TEST=OFF \
  -DMANIFOLD_PAR=OFF \
  -DMANIFOLD_DOWNLOADS=ON

echo "==> Building manifoldc target"
cmake --build "$BUILD_DIR" --target manifoldc -j"$(sysctl -n hw.ncpu)"

# --- 3. Install into the repo's native-asset layout -------------------------
# CMake's default output name on macOS keeps the "lib" prefix (Unix
# convention applies here, unlike MSVC) - libmanifoldc.dylib is expected to
# already match the P/Invoke name ManifoldInterop.cs declares
# (LibraryName = "libmanifoldc") once .NET appends the platform's default
# suffix (".dylib") during probing, the same way "libmanifoldc" ->
# "libmanifoldc.so" already resolves on Linux with no custom
# NativeLibrary.SetDllImportResolver. Copied to a fixed name regardless, to
# dereference symlinks and strip any version suffix, matching the Linux
# script's own approach.
BUILT_DYLIB=$(find "$BUILD_DIR" -maxdepth 3 -name 'libmanifoldc*.dylib' -print -quit)
if [ -z "$BUILT_DYLIB" ]; then
  echo "error: libmanifoldc*.dylib not found under $BUILD_DIR" >&2
  exit 1
fi

# libmanifold.dylib is the separate shared library libmanifoldc.dylib
# depends on at runtime (equivalent to libmanifold.so.3 on Linux).
BUILT_MANIFOLD_DYLIB=$(find "$BUILD_DIR" -maxdepth 3 \( -name 'libmanifold.dylib' -o -name 'libmanifold.*.dylib' \) -print 2>/dev/null | grep -v manifoldc | head -n1)
if [ -z "$BUILT_MANIFOLD_DYLIB" ]; then
  echo "error: libmanifold*.dylib (not manifoldc) not found under $BUILD_DIR" >&2
  exit 1
fi

rm -rf "$OUT_DIR"
mkdir -p "$OUT_DIR"
cp "$BUILT_DYLIB" "$OUT_DIR/libmanifoldc.dylib"
cp "$BUILT_MANIFOLD_DYLIB" "$OUT_DIR/libmanifold.dylib"
echo "==> Installed libmanifoldc.dylib and libmanifold.dylib to $OUT_DIR"

# install_name_tool sanity check: confirm libmanifoldc.dylib's dependency
# on libmanifold points at a loader-relative path, not an absolute
# build-tree path (the macOS equivalent of the Linux script's RUNPATH
# check).
DEPS=$(otool -L "$OUT_DIR/libmanifoldc.dylib" | tail -n +2)
if echo "$DEPS" | grep -q "$TOOLS_DIR"; then
  echo "error: libmanifoldc.dylib still references an absolute build-tree path:" >&2
  echo "$DEPS" >&2
  exit 1
fi
echo "==> Verified libmanifoldc.dylib has no absolute build-tree dependency path"
