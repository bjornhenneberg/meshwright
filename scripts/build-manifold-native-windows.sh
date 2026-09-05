#!/usr/bin/env bash
# Builds Manifold's C API (manifoldc) from source on Windows and installs
# the resulting DLLs into runtimes/win-x64/native/, mirroring
# scripts/build-manifold-native.sh (Linux). Intended to run under Git Bash
# on a GitHub Actions windows-latest runner (which ships CMake, Ninja/MSBuild
# and a Visual Studio 2022 toolchain already), invoked as a CI build step
# with `shell: bash`.
#
# STATUS: UNVERIFIED. Written and reviewed on a Linux dev host that cannot
# run this script - there is no Windows machine or CI runner available here
# to execute it against. `bash -n` syntax-checks clean; nothing more. See
# reports/M4 for what has and hasn't been exercised. Its first real run will
# be the first CI job that invokes it.
#
# Requires (present on GitHub's windows-latest image, not otherwise checked
# for here): cmake, a Visual Studio 2022 installation (for the
# "Visual Studio 17 2022" generator), and network access to github.com.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TOOLS_DIR="$REPO_ROOT/.tools"

MANIFOLD_TAG="v3.5.2"
MANIFOLD_SRC_ROOT="$TOOLS_DIR/src"
MANIFOLD_SRC_DIR="$MANIFOLD_SRC_ROOT/manifold-${MANIFOLD_TAG#v}"
BUILD_DIR="$TOOLS_DIR/build/manifold-${MANIFOLD_TAG}-win"

RID="win-x64"
OUT_DIR="$REPO_ROOT/runtimes/$RID/native"

mkdir -p "$TOOLS_DIR"

# --- 1. Pinned Manifold source (same tag as the Linux build) ---------------
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
# Same flag set as the Linux build (see build-manifold-native.sh for the
# rationale behind each one) - MANIFOLD_PAR=OFF, MANIFOLD_TEST=OFF, C API
# + cross-section only. The "Visual Studio 17 2022" generator picks up the
# MSVC toolchain from the runner image directly; unlike Ninja/Makefiles it
# doesn't need a Developer Command Prompt / vcvars environment to find
# cl.exe, which matters here since this runs from plain Git Bash.
echo "==> Configuring (Release, C API only, MSVC x64)"
cmake -S "$MANIFOLD_SRC_DIR" -B "$BUILD_DIR" \
  -G "Visual Studio 17 2022" -A x64 \
  -DCMAKE_BUILD_TYPE=Release \
  -DBUILD_SHARED_LIBS=ON \
  -DMANIFOLD_CBIND=ON \
  -DMANIFOLD_CROSS_SECTION=ON \
  -DMANIFOLD_PYBIND=OFF \
  -DMANIFOLD_JSBIND=OFF \
  -DMANIFOLD_TEST=OFF \
  -DMANIFOLD_PAR=OFF \
  -DMANIFOLD_DOWNLOADS=ON

echo "==> Building manifoldc target (Release)"
cmake --build "$BUILD_DIR" --target manifoldc --config Release

# --- 3. Install into the repo's native-asset layout -------------------------
# CMake's default MSVC output name for the manifoldc shared library target
# is "manifoldc.dll" (no "lib" prefix - that's a Unix convention CMake
# doesn't apply on Windows). .NET's default DllImport probing on Windows
# tries the P/Invoke name exactly, then that name + ".dll" - it does NOT add
# a "lib" prefix the way Unix probing does. ManifoldInterop.cs declares
# `LibraryName = "libmanifoldc"`, so the file must be named
# "libmanifoldc.dll" (with the prefix) for probing to find it without a
# custom NativeLibrary.SetDllImportResolver - the same reason the Linux
# script renames its .so output rather than leaving CMake's own filename.
BUILT_DLL=$(find "$BUILD_DIR" -iname 'manifoldc.dll' -print -quit)
if [ -z "$BUILT_DLL" ]; then
  echo "error: manifoldc.dll not found under $BUILD_DIR" >&2
  exit 1
fi

# manifold.dll is the separate shared library manifoldc.dll depends on at
# runtime (equivalent to libmanifold.so.3 on Linux). Windows has no RPATH
# to fix up: the default DLL search order checks the loading module's own
# directory first, so shipping it alongside manifoldc.dll in the same
# folder is sufficient - no build-tree-path issue to work around here.
BUILT_MANIFOLD_DLL=$(find "$BUILD_DIR" -iname 'manifold.dll' -print -quit)
if [ -z "$BUILT_MANIFOLD_DLL" ]; then
  echo "error: manifold.dll not found under $BUILD_DIR" >&2
  exit 1
fi

rm -rf "$OUT_DIR"
mkdir -p "$OUT_DIR"
cp "$BUILT_DLL" "$OUT_DIR/libmanifoldc.dll"
cp "$BUILT_MANIFOLD_DLL" "$OUT_DIR/manifold.dll"
echo "==> Installed libmanifoldc.dll and manifold.dll to $OUT_DIR"
