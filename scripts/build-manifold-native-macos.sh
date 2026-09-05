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
# depends on at runtime (equivalent to libmanifold.so.3 on Linux). CMake
# builds it with a *versioned* soname/install_name - `-install_name
# @rpath/libmanifold.3.dylib`, backed by a real file
# libmanifold.3.5.2.dylib with libmanifold.3.dylib and libmanifold.dylib
# as symlinks CMake also creates alongside it. libmanifoldc.dylib's own
# dependency, recorded at link time, is the exact install_name string
# above: @rpath/libmanifold.3.dylib - *not* whichever of the three names
# a plain filename search happens to pick. So the file must be installed
# under that exact versioned name; normalising it to "libmanifold.dylib"
# (as this script used to) leaves the real dependency name missing from
# $OUT_DIR, and libmanifoldc.dylib fails to load even though a
# same-content file sits right next to it under the wrong name. This
# mirrors the Linux script, which deliberately keeps its versioned soname
# (libmanifold.so.3) rather than normalising it - do the same here instead
# of copying to a fixed "libmanifold.dylib" name.
#
# Read the exact dependency name libmanifoldc.dylib actually needs at
# load time straight off its own LC_LOAD_DYLIB command, rather than
# guessing which of libmanifold.dylib / libmanifold.3.dylib /
# libmanifold.3.5.2.dylib is the right one to install.
MANIFOLD_DEP_NAME=$(otool -L "$BUILT_DYLIB" | tail -n +2 | awk '{print $1}' | grep -E '^(@rpath|@loader_path)/libmanifold\.' | head -n1 | xargs -I{} basename {})
if [ -z "$MANIFOLD_DEP_NAME" ]; then
  echo "error: libmanifoldc.dylib records no @rpath/@loader_path dependency on libmanifold under $BUILD_DIR" >&2
  otool -L "$BUILT_DYLIB" >&2
  exit 1
fi
BUILT_MANIFOLD_DEP=$(find "$BUILD_DIR" -maxdepth 3 -name "$MANIFOLD_DEP_NAME" -print 2>/dev/null | grep -v manifoldc | head -n1)
if [ -z "$BUILT_MANIFOLD_DEP" ]; then
  echo "error: $MANIFOLD_DEP_NAME (the exact name libmanifoldc.dylib depends on) not found under $BUILD_DIR" >&2
  exit 1
fi

rm -rf "$OUT_DIR"
mkdir -p "$OUT_DIR"
cp "$BUILT_DYLIB" "$OUT_DIR/libmanifoldc.dylib"
cp "$BUILT_MANIFOLD_DEP" "$OUT_DIR/$MANIFOLD_DEP_NAME"
echo "==> Installed libmanifoldc.dylib and $MANIFOLD_DEP_NAME to $OUT_DIR"

# install_name_tool sanity check: confirm libmanifoldc.dylib's dependency
# on libmanifold points at a loader-relative path, not an absolute
# build-tree path (the macOS equivalent of the Linux script's RUNPATH
# check) - AND that every @rpath/@loader_path dependency it names
# actually exists in $OUT_DIR. The absolute-path check alone would have
# passed while shipping a libmanifoldc.dylib that referenced
# libmanifold.3.dylib and had no such file next to it (this is exactly
# what happened before this fix) - it printed "==> Verified ..." while
# shipping an unloadable library, and the 22 resulting DllNotFoundException
# failures only showed up later, in the test run.
DEPS=$(otool -L "$OUT_DIR/libmanifoldc.dylib" | tail -n +2)
if echo "$DEPS" | grep -q "$TOOLS_DIR"; then
  echo "error: libmanifoldc.dylib still references an absolute build-tree path:" >&2
  echo "$DEPS" >&2
  exit 1
fi
# otool -L's first line after the header is the library's own id
# (LC_ID_DYLIB, e.g. @rpath/libmanifoldc.3.dylib - not the "libmanifoldc.dylib"
# name it was actually installed under) rather than a real dependency, so
# skip it here; only the lines after it are things libmanifoldc.dylib
# needs to find at load time.
while read -r DEP_PATH; do
  [ -z "$DEP_PATH" ] && continue
  case "$DEP_PATH" in
    @rpath/*|@loader_path/*)
      DEP_NAME="${DEP_PATH#@rpath/}"
      DEP_NAME="${DEP_NAME#@loader_path/}"
      if [ ! -e "$OUT_DIR/$DEP_NAME" ]; then
        echo "error: libmanifoldc.dylib depends on $DEP_PATH but $OUT_DIR/$DEP_NAME does not exist:" >&2
        echo "$DEPS" >&2
        exit 1
      fi
      ;;
  esac
done <<< "$(echo "$DEPS" | tail -n +2 | awk '{print $1}')"
echo "==> Verified libmanifoldc.dylib has no absolute build-tree dependency path, and every @rpath/@loader_path dependency resolves in $OUT_DIR"
