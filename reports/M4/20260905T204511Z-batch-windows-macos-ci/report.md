# M4-3 batch — Windows/macOS CI + packaging

Worked from `main` at `a2cd2de` (verified via `git log --oneline -1` before
starting; no rebase needed).

Scope: the Linux-only decision recorded in SPECIFICATION.md §11
(2026-09-04) was checked first, per instructions, rather than assumed.
It still holds in the sense that mattered most — this dev host cannot
*run* a Windows or macOS build — but one thing about it turned out to be
wrong: it does not follow that CI can't *build* Manifold per-platform.
GitHub's `windows-latest`/`macos-latest` runners are real Windows/macOS
machines with their own native toolchains (MSVC+CMake, Xcode+CMake) — they
don't need this Linux host to cross-compile anything. So this batch adds
real (if unverified) Windows/macOS native-library builds, not a "ship it
broken" placeholder.

## What was delivered

1. **`.github/workflows/ci.yml`** — two new jobs, `build-and-test-windows`
   (`windows-latest`) and `build-and-test-macos` (`macos-latest`), added
   alongside the existing `build-and-test` (Linux) job, same structure:
   restore → build → cache/fetch the test corpus → test. Two differences
   from the Linux job, both deliberate:
   - an extra first step, "Build Manifold native library", running the new
     per-platform native-build script (see below) — Linux's native lib is
     committed to git, Windows/macOS's isn't, so it has to be built as
     part of the job;
   - `dotnet test tests/Meshwright.Tests` instead of `dotnet test
     Meshwright.sln` — deliberately excludes `Meshwright.Tests.Gpu`, per
     instruction (GitHub runners have no GPU, and Xvfb is what makes the
     GPU suite viable at all on the Linux job; Windows/macOS get no
     equivalent here, and no Xvfb-equivalent step was added).
   `macos-latest` is Apple Silicon, so the macOS job is `osx-arm64` only;
   `osx-x64` (Intel) coverage would need a separate runner, not added.

2. **`scripts/build-manifold-native-windows.sh`** and
   **`scripts/build-manifold-native-macos.sh`** — mirror
   `scripts/build-manifold-native.sh`'s structure (same pinned Manifold
   tag `v3.5.2`, same CMake flags: C API + cross-section on, tests/pybind/
   jsbind/parallel off) for their respective platforms, installing into
   `runtimes/win-x64/native/` and `runtimes/osx-<arch>/native/`
   respectively. Both are **unverified** — no Windows or Mac machine was
   available here to run them; `bash -n` is all that was checked. Their
   first real execution will be the first CI run of the jobs above.

3. **`scripts/package-windows.sh`** — self-contained single-file `win-x64`
   publish, zipped (`zip`, falling back to PowerShell's
   `Compress-Archive` if `zip` isn't on PATH) rather than an MSI — no WiX
   tooling set up, deferred as a follow-up the same way AppImage was for
   Linux. Output: `artifacts/windows/meshwright_<version>_win-x64.zip`.

4. **`scripts/package-macos.sh`** — self-contained single-file `osx-arm64`
   (or `osx-x64` via `MESHWRIGHT_MACOS_RID`) publish wrapped in a minimal
   `Meshwright.app` bundle (`Info.plist` + `Contents/MacOS`), zipped with
   `ditto` (falling back to `zip`). No code signing or notarization —
   explicitly out of scope per §9 ("Linux + Windows first; macOS once
   there is revenue"); the script prints what signing/notarization would
   require (Developer ID cert, `codesign --options runtime`, `xcrun
   notarytool submit --wait`, `xcrun stapler staple`) so it isn't just a
   silent gap. Output: `artifacts/macos/Meshwright_<version>_<rid>.zip`.

5. **`Directory.Build.props`** — the one production-code change in this
   batch, and the one that matters most for honesty. See "The Manifold
   dependency" below for what it fixes and why.

6. **`README.md`** — a new "Windows and macOS" subsection stating plainly
   that these builds are unverified, that Booleans don't work in a package
   built without the CI native-build step, and that macOS isn't signed.

## The Manifold dependency — what I found and what I changed

Confirmed the prior audit's finding: `ManifoldInterop.cs` hardcodes
`private const string LibraryName = "libmanifoldc";` with no
`NativeLibrary.SetDllImportResolver`, and only
`runtimes/linux-x64/native/{libmanifoldc.so,libmanifold.so.3}` exist in the
repo — no Windows/macOS native binary anywhere.

**Can CI build Manifold per-platform, or does it have to ship prebuilt?**
Build from source per-platform, using each GitHub-hosted runner's own
native toolchain (`windows-latest` has MSVC+CMake; `macos-latest` has
Xcode+CMake already) — the same way the Linux native lib itself is built
in this repo, just running on that platform's own runner instead of
needing this Linux host to cross-compile. This is architecturally sound
and standard practice; it is **not verified against a real run**, since
this host can't execute either script.

**Does .NET's default DllImport probing find `libmanifoldc` on
Windows/macOS without a resolver?** Conclusion: yes, *if* the native
binary is named to match what probing looks for — no
`SetDllImportResolver` needed. Basis:
- The Linux script already relies on exactly this: .NET's Unix probing
  for a bare name like `"libmanifoldc"` tries the name as given, then that
  name plus the platform's default suffix (`.so` on Linux) — no additional
  `lib` prefix is inserted since the name already has one. The existing
  script's own `cp "$BUILT_SO" "$OUT_DIR/libmanifoldc.so"` line is doing
  exactly this rename (CMake's raw output has a versioned filename that
  doesn't match on its own).
- On **Windows**, probing tries the literal name, then name + `.dll` — it
  does **not** add a `lib` prefix (that's Unix-only behavior). CMake's
  default MSVC output for the `manifoldc` target is `manifoldc.dll` (no
  prefix). So the new Windows script renames it to `libmanifoldc.dll`
  during install, the same move the Linux script already makes, and
  probing then finds `libmanifoldc` + `.dll` = `libmanifoldc.dll` exactly.
- On **macOS**, CMake keeps the `lib` prefix by default (Unix convention),
  so the natural output `libmanifoldc.dylib` already matches
  `"libmanifoldc"` + the platform's default `.dylib` suffix with no rename
  needed — copied to a fixed name anyway, mirroring the Linux script, to
  dereference symlinks/strip any version suffix.
This reasoning is sound but **unverified against an actual Windows/macOS
process** — no machine here to confirm the P/Invoke actually resolves at
runtime.

**A real, verified bug found and fixed along the way:** `Directory.Build.props`
unconditionally globbed `runtimes/linux-x64/native/*.so*` into every
project's output, with no RID conditioning. Confirmed by direct test: a
`dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true`
run from this host bundled `runtimes/linux-x64/native/{libmanifoldc.so,
libmanifold.so.3}` into the Windows output — wrong-platform files that
would never load, on top of the underlying fact that no Windows Manifold
build exists yet. Fixed: `Directory.Build.props` now resolves a
`_MeshwrightNativeRid` (the explicit `$(RuntimeIdentifier)` when a publish
sets one, else the host OS, defaulting to `osx-arm64` for a RID-less macOS
build) and copies only that RID's `runtimes/<rid>/native/*` — restricted
to `Meshwright.App`/`Meshwright.Tests`/`Meshwright.Tests.Gpu` (gated on
`$(MSBuildProjectName)`, not `$(OutputType)` — see the in-file comment for
why the more "obvious" `OutputType` check doesn't work: `Directory.Build.props`
is imported before the SDK sets `OutputType`'s default, so a
`PropertyGroup` condition on it there is silently always false; moving
the logic into a `Target` fixes the property but breaks `%(Filename)`/
`%(Extension)` batching in the accompanying `Link` metadata — MSB3024,
empty destination filenames — a real dead end hit and backed out of during
this batch). Verified: a repeat `-r win-x64` publish afterward carries
**zero** `*manifold*` files (down from two Linux ones); `-r osx-arm64` and
`-r osx-x64` publishes are likewise clean; the Linux `dotnet build` +
`dotnet test` path is unaffected (520/520 still pass — see below). Today,
with no `runtimes/win-x64` or `runtimes/osx-*` directories committed, a
Windows/macOS package built outside CI has **no Manifold native library at
all** — Boolean throws `DllNotFoundException` rather than loading a
wrong-platform file that would have failed anyway, and the packaging
scripts detect this and ship an honest `NOTICE.txt` saying so (see
`scripts/package-windows.sh`/`package-macos.sh`).

**A portability risk not fixed, flagged instead:** `scripts/fetch-corpus.sh`
uses `sha256sum`, a GNU coreutils tool. It's present via Git Bash on
`windows-latest` (confirmed by Git for Windows shipping it), but stock
macOS does **not** ship `sha256sum` (BSD userland has `shasum -a 256`
instead) — whether GitHub's `macos-latest` image happens to have GNU
coreutils on `PATH` was not checked and isn't something I could verify
from here. Left as-is per the instruction to reuse the existing corpus-
fetch approach rather than inventing a new one; if the macOS CI job's
"Fetch test corpus" step fails, this is the first thing to check.

## Verified vs. unverified — precise split

**Verified on this host (Linux Mint 22.3, dotnet 10.0.111):**
- `git log --oneline -1` → `a2cd2de` before starting; no rebase needed.
- `bash -n` clean on all four new/modified shell scripts, plus a re-check
  of the two pre-existing ones.
- `python3 -c "import yaml; yaml.safe_load(...)"` — `.github/workflows/ci.yml`
  parses with the expected three job names.
- `dotnet publish -r win-x64/osx-arm64/osx-x64 --self-contained
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true`
  all **succeed** from this host (exit 0), each producing an executable
  and zero `*manifold*` files (correct — no native lib exists for those
  RIDs yet, and none should be silently substituted).
- `scripts/package-windows.sh` and `scripts/package-macos.sh` run
  end-to-end on this host (the `zip`/fallback path, not the Windows-
  `PowerShell`/macOS-`ditto` path, which aren't present here): both
  produce a correctly-structured archive (`meshwright_0.1.0_win-x64.zip`
  containing the exe + `NOTICE.txt`; `Meshwright_0.1.0_osx-arm64.zip`
  containing a `Meshwright.app` with `Info.plist`, the executable, and
  `Contents/Resources/NOTICE.txt`) with the Boolean-disabled notice
  correctly triggered (no native lib present for either RID). One real bug
  was caught and fixed this way: an unescaped `$99/yr` in a printed note
  in `package-macos.sh` was being parsed as `$9` (positional-parameter
  expansion) by bash, tripping `set -u` — escaped to `\$99/yr`.
- `dotnet test tests/Meshwright.Tests -c Release` — **520 passing, 0
  skipped, 0 failed**, from a from-scratch `restore` + `build` (all
  `obj`/`bin` deleted first) after all changes in this batch, confirming
  the `Directory.Build.props` change doesn't regress the one platform
  that can actually be tested here.

**Unverified (by construction — no Windows or macOS execution environment
available in this session):**
- Whether `build-manifold-native-windows.sh` / `-macos.sh` actually
  succeed on real `windows-latest`/`macos-latest` runners (CMake generator
  selection, target/library names found by `find`, etc.).
- Whether the resulting `libmanifoldc.dll`/`libmanifoldc.dylib` actually
  P/Invoke-resolve and produce correct boolean results at runtime — the
  naming-convention reasoning above is sound but untested end-to-end.
- Whether `Meshwright.App.exe`/`Meshwright.app` actually launch and render
  a window on real Windows/macOS.
- Whether `scripts/package-windows.sh`'s PowerShell `Compress-Archive`
  fallback and `scripts/package-macos.sh`'s `ditto` path work as written
  (never exercised — this host has neither).
- The `fetch-corpus.sh` / `sha256sum` risk on macOS noted above.
- Whether `windows-latest`/`macos-latest` CI minutes/quota or any org
  policy affects these jobs — outside this session's visibility entirely.

## Proposed §11 additions (for the dispatcher to land)

Suggested row/entry text, for SPECIFICATION.md §11 (not edited directly,
per instructions):

> M4-3 (Windows/macOS CI + packaging) added, **unverified**: CI jobs
> (`build-and-test-windows` on `windows-latest`, `build-and-test-macos` on
> `macos-latest`) build Manifold from source per-platform (no prebuilt
> binary committed, unlike `linux-x64`) and run `Meshwright.Tests` only
> (GPU suite excluded, same reasoning as Linux's Xvfb dependency but with
> no runner GPU to exercise it at all). Packaging scripts
> (`package-windows.sh` zip, `package-macos.sh` unsigned `.app` zip) ship
> honestly: a package built without the CI-built native library disables
> Boolean with a `NOTICE.txt` rather than crashing silently. Found and
> fixed a real bug while verifying: `Directory.Build.props` was
> unconditionally bundling the Linux `.so` into Windows/macOS publishes;
> now RID-gated. First real CI run on GitHub's Windows/macOS runners is the
> next milestone for this item, not this batch.

Suggested "Immediate next steps" wording:

> Trigger the first Windows/macOS CI run on GitHub (push/PR) and read the
> result — this batch's Windows/macOS work is unverified by construction
> (no such machine available in the dev sandbox); that first run is where
> the Manifold-native-build reasoning in this report gets its first real
> test. If `build-manifold-native-windows.sh`/`-macos.sh` fail, check the
> CMake generator/target-name assumptions first. If Manifold builds but
> Boolean tests still fail, check the DllImport naming-convention
> reasoning (`libmanifoldc.dll` / `libmanifoldc.dylib`) next. If the macOS
> job's corpus fetch step fails, check `sha256sum` availability first.

## Files touched

- `.github/workflows/ci.yml` — two new jobs
- `Directory.Build.props` — RID-gated native asset copying (fixes a real
  wrong-platform-bundling bug found while verifying this batch)
- `README.md` — new Windows/macOS section
- `scripts/build-manifold-native-windows.sh` (new)
- `scripts/build-manifold-native-macos.sh` (new)
- `scripts/package-windows.sh` (new)
- `scripts/package-macos.sh` (new)

Not touched, per instructions: `SPECIFICATION.md`, `docs/images/`,
`tests/Meshwright.Tests.Gpu/`, `NEXT-SESSION.md`.
