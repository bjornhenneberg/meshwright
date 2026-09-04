#!/usr/bin/env bash
# Builds Manifold's C API (manifoldc) from source and installs the resulting
# shared library into runtimes/<rid>/native/, mirroring the RID-based native
# asset layout NuGet packages use for native dependencies.
#
# Linux x86_64 only for now (this dev host's platform). Windows/macOS builds
# are an M4 packaging follow-up — see NATIVE.md at the repo root.
#
# Requires: g++/gcc, make, network access to github.com (releases + git
# clones for Manifold's own transitive deps, e.g. Clipper2). No system-wide
# cmake install is required or used — this script downloads a portable
# CMake release into .tools/ (gitignored) and builds with that.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TOOLS_DIR="$REPO_ROOT/.tools"

CMAKE_VERSION="4.4.3"
CMAKE_ROOT="$TOOLS_DIR/cmake-${CMAKE_VERSION}-linux-x86_64"
CMAKE_BIN="$CMAKE_ROOT/bin/cmake"

MANIFOLD_TAG="v3.5.2"
MANIFOLD_SRC_ROOT="$TOOLS_DIR/src"
MANIFOLD_SRC_DIR="$MANIFOLD_SRC_ROOT/manifold-${MANIFOLD_TAG#v}"
BUILD_DIR="$TOOLS_DIR/build/manifold-${MANIFOLD_TAG}"

RID="linux-x64"
OUT_DIR="$REPO_ROOT/runtimes/$RID/native"

mkdir -p "$TOOLS_DIR"

# --- 1. Portable CMake (no apt, no sudo) -----------------------------------
if [ ! -x "$CMAKE_BIN" ]; then
  echo "==> Downloading portable CMake ${CMAKE_VERSION}"
  TARBALL="$TOOLS_DIR/cmake-${CMAKE_VERSION}-linux-x86_64.tar.gz"
  curl -sL -o "$TARBALL" \
    "https://github.com/Kitware/CMake/releases/download/v${CMAKE_VERSION}/cmake-${CMAKE_VERSION}-linux-x86_64.tar.gz"
  tar xzf "$TARBALL" -C "$TOOLS_DIR"
  rm "$TARBALL"
fi
echo "==> Using $("$CMAKE_BIN" --version | head -n1)"

# --- 2. Pinned Manifold source ----------------------------------------------
if [ ! -d "$MANIFOLD_SRC_DIR" ]; then
  echo "==> Fetching Manifold ${MANIFOLD_TAG} source"
  mkdir -p "$MANIFOLD_SRC_ROOT"
  TARBALL="$TOOLS_DIR/manifold-${MANIFOLD_TAG}.tar.gz"
  curl -sL -o "$TARBALL" \
    "https://github.com/elalish/manifold/archive/refs/tags/${MANIFOLD_TAG}.tar.gz"
  tar xzf "$TARBALL" -C "$MANIFOLD_SRC_ROOT"
  rm "$TARBALL"
fi

# --- 3. Configure + build the C API only ------------------------------------
# MANIFOLD_CBIND requires MANIFOLD_CROSS_SECTION (the C API's cross_section.h
# surface), so cross-section support stays on even though M3 only needs the
# 3D boolean entry points. MANIFOLD_TEST=OFF skips GTest/samples/extras
# entirely (not just "off at runtime" - the subdirectories aren't even
# configured), which is most of the build-time and dependency savings.
# MANIFOLD_PAR=OFF avoids a TBB dependency; a two-cube boolean in a proof
# test has no need for the parallel backend.
#
# libmanifoldc.so links against a separate libmanifold.so.3, and by default
# CMake bakes the *build directory's* absolute path into that dependency's
# RUNPATH (an "install" RPATH is only applied by `cmake --install`, which
# this script doesn't run - it copies the .so directly out of the build
# tree). That absolute path breaks the moment the build dir is rebuilt
# elsewhere or removed, which is exactly the RPATH issue this script used to
# ship. CMAKE_BUILD_RPATH_USE_ORIGIN + a literal $ORIGIN RPATH makes the
# dependency resolve relative to wherever libmanifoldc.so itself ends up, so
# copying libmanifold.so.3 alongside it into OUT_DIR (step 4) is enough for
# it to keep working after the build tree is gone.
echo "==> Configuring (Release, C API only)"
"$CMAKE_BIN" -S "$MANIFOLD_SRC_DIR" -B "$BUILD_DIR" \
  -DCMAKE_BUILD_TYPE=Release \
  -DBUILD_SHARED_LIBS=ON \
  -DCMAKE_BUILD_RPATH_USE_ORIGIN=ON \
  -DCMAKE_BUILD_RPATH='$ORIGIN' \
  -DMANIFOLD_CBIND=ON \
  -DMANIFOLD_CROSS_SECTION=ON \
  -DMANIFOLD_PYBIND=OFF \
  -DMANIFOLD_JSBIND=OFF \
  -DMANIFOLD_TEST=OFF \
  -DMANIFOLD_PAR=OFF \
  -DMANIFOLD_DOWNLOADS=ON

echo "==> Building manifoldc target"
"$CMAKE_BIN" --build "$BUILD_DIR" --target manifoldc -j"$(nproc)"

# --- 4. Install into the repo's native-asset layout -------------------------
BUILT_SO=$(find "$BUILD_DIR" -maxdepth 3 -name 'libmanifoldc.so' -print -quit)
if [ -z "$BUILT_SO" ]; then
  echo "error: libmanifoldc.so not found under $BUILD_DIR" >&2
  exit 1
fi

# libmanifold.so.<N> is a separate shared library that libmanifoldc.so
# depends on at runtime (see the RPATH comment above) - it must ship
# alongside libmanifoldc.so, not just the C API entry point.
BUILT_MANIFOLD_SO=$(find "$BUILD_DIR" -maxdepth 3 -name 'libmanifold.so*' -not -name '*.so' -print -quit)
if [ -z "$BUILT_MANIFOLD_SO" ]; then
  echo "error: libmanifold.so.* not found under $BUILD_DIR" >&2
  exit 1
fi

rm -rf "$OUT_DIR"
mkdir -p "$OUT_DIR"
# Dereference symlinks (cp, not cp -P) so OUT_DIR gets real file content
# under fixed names, not a symlink pointing at a versioned filename that
# didn't get copied.
cp "$BUILT_SO" "$OUT_DIR/libmanifoldc.so"
cp "$BUILT_MANIFOLD_SO" "$OUT_DIR/libmanifold.so.3"
echo "==> Installed libmanifoldc.so and libmanifold.so.3 to $OUT_DIR"

RUNPATH=$(readelf -d "$OUT_DIR/libmanifoldc.so" | grep -oP '(?:RPATH|RUNPATH).*\[\K[^\]]*' || true)
if [[ "$RUNPATH" == *"$TOOLS_DIR"* ]]; then
  echo "error: libmanifoldc.so still embeds an absolute build-tree path in its RPATH/RUNPATH: $RUNPATH" >&2
  exit 1
fi
echo "==> Verified libmanifoldc.so RPATH/RUNPATH is portable: '${RUNPATH:-<none>}'"
