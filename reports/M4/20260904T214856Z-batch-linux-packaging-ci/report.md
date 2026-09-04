# M4-3 batch — Linux CI + packaging

Scope for this batch, per user direction: Linux only. CI and packaging for
Windows/macOS are a follow-up batch, since neither can be built or verified
on this dev host.

## What was delivered

1. **`.github/workflows/ci.yml`** — GitHub Actions CI on `ubuntu-24.04`:
   restore, build (Release), cache + fetch the real-world test corpus
   (`actions/cache` keyed on `tests/corpus/manifest.tsv`'s hash, via
   `scripts/fetch-corpus.sh`), install Xvfb + Mesa software rendering, and
   run the full test suite (`Meshwright.Tests` + `Meshwright.Tests.Gpu`)
   under `xvfb-run`. The Manifold native libraries
   (`runtimes/linux-x64/native/*.so*`) are already committed to git (built by
   `scripts/build-manifold-native.sh` in an earlier batch), so CI does not
   need to build Manifold from source — it just restores/builds/tests.
   Runs on push and PR against `main`.

2. **`scripts/package-linux.sh`** — self-contained, single-file `linux-x64`
   publish (`PublishSingleFile` + `IncludeNativeLibrariesForSelfExtract`, so
   the Manifold native libs self-extract at first run rather than needing a
   separate install step) packaged into a `.deb`: binary at
   `/opt/meshwright/meshwright`, a `/usr/bin/meshwright` symlink, and a
   `.desktop` entry. Output: `artifacts/linux/meshwright_<version>_amd64.deb`
   (gitignored — build artifacts, not source). AppImage was scoped out of
   this batch: it needs `appimagetool`, which isn't installed on this host
   and isn't available via `apt`; `.deb` alone covers Debian/Ubuntu/Mint,
   which is this project's own dev platform and a large share of the Linux
   desktop-print-shop audience. Revisit AppImage as a fast follow once there
   is a CI runner that can fetch `appimagetool` over the network.

## Verification

All done locally on this dev host (Linux Mint 22.3 / Ubuntu 24.04 base,
`dotnet 10.0.111`), which stood in for what CI would do since GitHub Actions
itself can't be triggered from here:

- `dotnet restore Meshwright.sln` / `dotnet build -c Release` — clean build,
  0 errors (360 pre-existing warnings, all in vendored g3 code or unused
  gizmo fields, unrelated to this batch).
- `dotnet test Meshwright.sln -c Release --no-build` — **432/432** passing
  (`Meshwright.Tests`, includes the M4-1 corpus tests, which were already
  fetched on this host from earlier work).
- `dotnet test tests/Meshwright.Tests.Gpu -c Release --no-build` —
  **8/8** passing, against this host's real `$DISPLAY=:0` GL context (Xvfb
  itself could not be installed in this sandboxed session — no `sudo`
  password — so the CI-specific `xvfb-run` path is unverified end-to-end;
  the GPU tests' own skip-if-unavailable behavior means a CI run without a
  working Xvfb/Mesa setup would still pass, just by skipping those 8, not by
  failing red).
- `MESHWRIGHT_VERSION=0.1.0 ./scripts/package-linux.sh` — builds
  `artifacts/linux/meshwright_0.1.0_amd64.deb` (33 MB compressed, 95 MB
  installed executable).
- `dpkg-deb -c`/`-I` — package tree and control metadata verified: binary at
  the right path, `usr/bin` symlink, `.desktop` entry, correct
  `Installed-Size`.
- Ran the raw publish output directly
  (`artifacts/linux/publish/Meshwright.App`) and the `.deb`-installed copy
  (`dpkg-deb -x` into a scratch root, then executed
  `opt/meshwright/meshwright`) — both start cleanly under a 5s timeout with
  empty stdout/stderr (no missing-library or startup errors), confirming the
  self-extracting single-file bundle finds `libmanifoldc.so`/
  `libmanifold.so.3` correctly outside of a `dotnet build` output directory.
  Full interactive UI verification (window actually rendering, gizmos
  responding) was not done — no way to see a GUI window from this
  environment; the smoke test only confirms process start, not rendered
  output.

## Known gaps / follow-ups

- Windows and macOS CI + packaging are not started — separate batch.
- AppImage packaging not done — needs `appimagetool`.
- CI's Xvfb path is written but not run end-to-end from this session; next
  push to `main`/a PR will be the first real execution.
- No code signing / notarization (macOS) or Authenticode (Windows) — out of
  scope until those platforms are packaged at all.
- Docs/release (M4-4) — website, sample files, install instructions — not
  started.
