#!/usr/bin/env bash
# Builds Manifold's C API (manifoldc) from source on Windows and installs
# the resulting DLLs into runtimes/win-x64/native/, mirroring
# scripts/build-manifold-native.sh (Linux). Intended to run under Git Bash
# on a GitHub Actions windows-latest runner (which ships CMake, Ninja and
# some Visual Studio toolchain already), invoked as a CI build step with
# `shell: bash`.
#
# STATUS: this script's first real CI run (windows-latest, image
# "windows-2025-vs2026") failed at the configure step: it hardcoded the
# CMake generator as "Visual Studio 17 2022", and that image ships Visual
# Studio 2026 instead - "could not find any instance of Visual Studio"
# because there was no VS 2022 to find. windows-latest's toolchain moves
# out from under this script on GitHub's own schedule, so naming a specific
# VS version here is a bug by construction, not a one-off: it works until
# the next image update silently breaks it again. The fix (below) is to not
# hardcode a generator at all: prefer Ninja when it's on PATH, and otherwise
# let CMake pick its own default (newest-installed) Visual Studio generator
# rather than asserting a version. Still otherwise unverified beyond that
# one real run - there is no Windows machine here to test further against.
#
# Requires (present on GitHub's windows-latest image, not otherwise checked
# for here): cmake, some Visual Studio installation, and network access to
# github.com.
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
# + cross-section only.
#
# Generator selection is deliberately not a hardcoded version, since that's
# exactly what broke on this script's first CI run (see the header comment):
# windows-latest's Visual Studio version changes on GitHub's own schedule,
# out from under this script. So: prefer Ninja when it's on PATH (recent
# CMake locates the MSVC toolchain itself even outside a Developer Command
# Prompt / vcvars environment, whether the generator is Ninja or Visual
# Studio - both should work unattended from plain Git Bash on a modern CMake).
# Otherwise, pass no -G at all and let CMake choose its own default
# (newest-installed) Visual Studio generator rather than asserting one by
# name. -A x64 is meaningful only for a Visual Studio generator - Ninja
# infers its architecture from the toolchain it finds and rejects -A
# outright - so only add it in the non-Ninja branch.
GENERATOR_ARGS=()
if command -v ninja >/dev/null 2>&1; then
  echo "==> Ninja found on PATH; using the Ninja generator"
  GENERATOR_ARGS=(-G Ninja)
else
  echo "==> Ninja not found; letting CMake pick its own default Visual Studio generator (targeting x64)"
  GENERATOR_ARGS=(-A x64)
fi

echo "==> Configuring (Release, C API only, MSVC x64)"
cmake -S "$MANIFOLD_SRC_DIR" -B "$BUILD_DIR" \
  "${GENERATOR_ARGS[@]}" \
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
