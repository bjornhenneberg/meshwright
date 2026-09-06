# Meshwright — Specification

**Status:** Draft v0.2 — living document, updated as milestones land
**Working name:** Meshwright (placeholder — rename freely)
**Progress:** M0 (Skeleton), M1 (Inspect), M2 (Repair), and M3 (Edit) complete
and verified; see `reports/M0/SUMMARY.md`, `reports/M1/SUMMARY.md`,
`reports/M2/SUMMARY.md`, and `reports/M3/`. M4 (Polish and release) is
underway — batches M4-0 (Manifold RPATH/interop fix), M4-1 (real-world test
corpus), M4-2 (gizmo wiring + menu/undo-redo UI), M4-6 (corpus ground
truth), M4-7 (non-manifold import fix), M4-3 (CI + packaging,
all three platforms), M4-4
(docs/release), M4-8 (make the app do what it says) and M4-9 (correctness
gaps closed) are complete, 520/520 unit tests passing; see the M4 entry in
§11 and §7. The 8 GPU tests pass (re-run 2026-09-05, after fixing a test
parallelism hang unrelated to the geometry code — see §11).

**Caveat on "complete":** M4-8 found that M2's repair operations and M3's
edit operations had been called complete while being unreachable or invisible
in the running app — no Repair UI existed at all, and no edit operation ever
refreshed the viewport. Both are now fixed, but treat a ✅ below as "the code
exists and its tests pass", and check §7's M4-8 entry and the outstanding
items under "Immediate next steps" for what a user can actually do.

---

## 1. Problem

Autodesk Meshmixer was discontinued in 2021 and never replaced. It is still the tool
recommended in 3D printing communities for repairing and preparing meshes, still
distributed from an archive page, and it is increasingly broken on modern operating
systems. Nothing has filled the gap:

| Tool | Why it doesn't fill the gap |
| --- | --- |
| Blender | Powerful, but an artist's tool with an artist's UI. Repair workflows require add-ons and tribal knowledge. |
| Netfabb | Moved to Autodesk enterprise pricing. Out of reach for makers and small shops. |
| MeshLab | Research-grade UI, unpredictable, no printing-oriented workflow. |
| Slicers (Orca, Prusa, Bambu) | Excellent at slicing; deliberately do not repair or edit geometry beyond trivial cases. |
| Microsoft 3D Builder | Abandoned, Windows-only, very limited. |

The target user is not modelling from scratch. They have a mesh — downloaded,
scanned, or exported from CAD — and it needs to be made printable.

## 2. Goal

A focused, fast, cross-platform desktop application that takes a mesh from
"downloaded/scanned" to "ready to slice".

**Explicit non-goal:** this is not a CAD package, not a sculpting suite, and not a
slicer. Scope discipline is the main risk to this project.

## 3. Target users

1. **Makers / hobbyists** — repairing downloaded models, cutting large prints into
   pieces, hollowing to save filament.
2. **Prop, cosplay and model makers** — splitting oversized models, adding
   registration pins, boolean joins.
3. **Small commercial shops** (dental, jewellery, engineering services) — repairing
   customer-supplied files quickly and repeatably.
4. **Scan users** — photogrammetry and 3D scanner output, which is almost always
   non-manifold and needs cleanup.

## 4. Core principles

- **Fixed in one click, tuned if you want.** Every operation has a sane default that
  works without understanding the underlying algorithm.
- **Never silently destroy the model.** Full undo, non-destructive where feasible,
  original file untouched until explicit export.
- **Honest diagnostics.** Tell the user exactly what is wrong with the mesh in plain
  language, not just an error count.
- **Fast on real files.** Target: a 5M-triangle scan loads and is navigable smoothly.
- **No account, no cloud, no telemetry.** Ever. This is a differentiator, not a
  detail.

## 5. Feature scope

### 5.1 v1.0 — Minimum lovable product

The smallest set that makes someone uninstall Meshmixer.

**Import / export**
- Import: STL (binary + ASCII), OBJ
- Export: STL (binary), OBJ
- 3MF and PLY are deferred to v1.x. STL covers the overwhelming majority of real
  files; OBJ covers the rest. The importer interface is designed for more formats
  from the start, but none are implemented in 1.0.
- Unit handling: detect/assign mm vs inch, scale on import
- Drag-and-drop, recent files list

**Inspect**
- Mesh statistics: triangle count, volume, surface area, bounding box, shell count
- Error detection and visual highlight of:
  - non-manifold edges and vertices
  - boundary holes
  - self-intersections
  - inverted / inconsistent normals
  - degenerate and zero-area triangles
  - duplicate vertices
  - disconnected shells and stray floating debris
- Plain-language report: "3 holes, 1 stray shell (0.02 % of volume), 14 flipped faces"

**Repair**
- One-click Auto Repair that runs the pipeline and reports what it did
- Individual operations, each independently runnable:
  - hole filling (flat / smooth / planar fill)
  - normal unification
  - remove degenerate triangles and duplicate vertices
  - remove small disconnected shells (with size threshold slider)
  - self-intersection resolution
  - voxel remesh / solidify as the sledgehammer fallback for hopeless meshes

**Edit**
- Plane cut: interactive plane gizmo, cut with optional cap, keep one side or split
  into separate parts, with an optional peg-and-socket alignment pin pair on the
  mating faces (one shape, configurable diameter and clearance — the fuller joint
  catalogue stays in v1.x)
- Boolean union / difference / intersection between loaded meshes
- Transform: move, rotate, scale, mirror, numeric entry, align to bed, drop to Z=0
- Hollow: offset shell to a given wall thickness
- Drain holes: place holes on the surface, configurable diameter and countersink

**Simplify**
- Quadric edge-collapse decimation, targeting triangle count or percentage, with a
  live before/after triangle count

**Viewport / UX**
- ~~Orbit / pan / zoom, orthographic and perspective, standard view presets~~ ✅
- ~~Shaded, wireframe, x-ray and error-highlight display modes~~ ✅
- ~~Build plate grid with configurable printer size, out-of-bounds warning~~ ✅
- ~~Undo/redo across all operations~~ ✅
- ~~Cross-section preview slider~~ ✅

### 5.2 v1.x — Follow-up

- 3MF import/export (with colour and multi-object support) and PLY import
- Auto-orientation for minimum support / best strength
- Registration pins and dowel/puzzle joints on cut faces — the full catalogue
  (dovetails, finger joints, magnet pockets, multiple pins per cut) beyond the
  single peg/socket pair shipped in v1.0
- Measurement tools (distance, wall thickness heat map)
- Local sculpting brushes: smooth, flatten, drag, pinch
- Text and logo embossing on a surface
- Batch mode: repair a folder of files with saved settings
- Command-line interface for shops that want to automate repair
- Lattice / infill structure generation

### 5.3 v2.0 — Resin printing module

The second product described in the plan, sharing the same geometry core:

- Support generation (auto + manual): tips, struts, rafts, contact-point tuning
- Orientation optimisation for minimum cross-sectional area
- Hollowing with resin drain hole placement and suction-cup detection
- Island detection (unsupported floating regions per layer)
- Export to printer formats where documented

## 6. Technical design

### 6.1 Stack

| Concern | Choice | Notes |
| --- | --- | --- |
| Language | C# / .NET 10 (LTS) | Requested; strong desktop story, good perf with `Span<T>` and SIMD. LTS matters for a tool users keep for years |
| UI | Avalonia UI 11 | True cross-platform (Linux/Windows/macOS), MVVM, native-feeling |
| 3D viewport | Silk.NET (OpenGL 3.3 core) | Embedded in Avalonia via a native control; OpenGL 3.3 for maximum hardware reach |
| Math | System.Numerics + custom double-precision types | Single precision for rendering, double for geometry |
| Geometry core | Custom, in-house | See below |
| Testing | xUnit + a corpus of known-bad meshes | Golden-file regression tests |
| Packaging | Self-contained single-file publish per platform | AppImage/deb (Linux), MSI or plain zip (Windows), notarised .app (macOS) |

### 6.2 Geometry libraries — decision

The realistic options:

- **geometry3Sharp / g3Sharp** (Boost licence) — the closest existing .NET fit:
  DMesh3, remeshing, marching cubes, mesh booleans via voxels. Largely unmaintained,
  but permissively licensed and forkable. **Recommended starting point.**
- **libigl / CGAL / OpenVDB** via P/Invoke — powerful, but CGAL is GPL-or-commercial
  and would dictate licensing; adds native build complexity per platform.
- **MeshLab / VCGlib** — GPL, same licensing problem.
- **Manifold** (MIT, by Emmett Lalish) — modern, extremely fast, robust boolean
  engine. Strong candidate for the boolean and hollowing operations specifically, via
  a thin C interop layer.

**Plan (decided):** *vendor* selected parts of g3Sharp rather than forking it
wholesale. A full fork means owning ~100k lines of unmaintained code, most of which
(solvers, curve tooling, implicit surfaces, its own I/O) this project will never use.
Instead, copy into `Meshwright.Geometry/Vendor/g3/` only what is needed, with the
Boost licence header retained and provenance recorded in a `VENDOR.md`:

- `DMesh3` and its index/attribute structures
- `MeshNormals`, `MeshConnectedComponents`, `MeshBoundaryLoops`
- `Reducer` (quadric decimation) and `Remesher`
- `DMeshAABBTree3` (spatial queries, ray casts, self-intersection detection)
- `MarchingCubes` and `MeshSignedDistanceGrid` (voxel remesh / solidify / hollow)

Booleans come from **Manifold** (MIT) through a thin C interop layer, not from
g3Sharp's voxel booleans, which are lossy. Printing-specific operations (drain holes,
cut-and-cap, shell-removal heuristics, the diagnostics report) are written in-house.

No GPL dependency, so the licence in §8 remains possible.

### 6.3 Architecture

```
Meshwright.Geometry     — mesh data structures, repair, booleans, decimation.
                          No UI, no I/O. Pure, testable, benchmarkable.
Meshwright.IO           — STL/OBJ/3MF/PLY readers and writers.
Meshwright.Core         — document model, operation/command pipeline, undo stack,
                          settings. UI-agnostic.
Meshwright.Rendering    — Silk.NET renderer, camera, gizmos, picking, shaders.
Meshwright.App          — Avalonia views and view models.
Meshwright.Cli          — headless batch entry point (v1.x).
Meshwright.Tests        — unit + golden-file regression tests.
```

Every user-facing action is an `IMeshOperation` with parameters, a `Preview()` and an
`Apply()`. Undo, batch mode, the CLI and scripting all fall out of that one
abstraction for free. Long-running operations run off the UI thread with progress
reporting and cancellation.

### 6.4 Performance targets

| Scenario | Target |
| --- | --- |
| Load 5M-triangle STL | < 3 s |
| Viewport navigation, 5M triangles | > 30 fps on integrated graphics |
| Auto-repair, 500k triangles | < 5 s |
| Memory | < 6× the raw triangle data size |
| UI thread blocked per operation | < 100 ms, regardless of mesh size |

## 7. Milestones

**M0 — Skeleton** ✅ Complete (`reports/M0/SUMMARY.md`)
Solution structure, Avalonia window, Silk.NET viewport rendering a loaded STL with
orbit/pan/zoom. Proves the hardest integration risk first.
Delivered: `global.json` + solution scaffolding for all six projects in §6.3; binary/
ASCII STL autodetection; `OrbitCamera` math; a Silk.NET `MeshRenderer`; and
`MainWindow`/`MeshViewportControl` wiring with pointer/scroll input. 22/22 tests
passing. Actual GPU pixel output could not be verified on the headless dev host and
is flagged for a manual smoke test on a machine with a GPU.

**M1 — Inspect** ✅ Complete (`reports/M1/SUMMARY.md`)
Full mesh analysis and error highlighting. Shippable alone as a free "why won't this
print?" tool — and a cheap way to find the first users.
Delivered: a real vendored g3Sharp tree (92 files, see §6.2/`VENDOR.md`) replacing the
M0 `TriangleMesh` stopgap; all 7 v1.0 detectors (non-manifold, boundary holes,
self-intersections, inverted normals, degenerate triangles, duplicate vertices,
disconnected shells) behind a shared `IMeshDetector`/`MeshDiagnosticsRunner`
contract; `MeshDocument` wiring and a `MainWindow` diagnostics panel (statistics,
plain-language summary, per-issue list); and GL highlighting of flagged geometry,
verified with GPU pixel-diff tests against a real broken sample mesh. 75/75 tests
passing (69 unit + 6 GPU).

**M2 — Repair** ✅ Complete (`reports/M2/SUMMARY.md`)
Auto Repair plus the individual repair operations. Undo stack. Export.
Delivered: an `IMeshOperation` contract (`Preview`/`Apply` per §6.3) with a
snapshot-based undo stack wired into `MeshDocument`; the six individually-
runnable repair operations from §5.1 (degenerate-triangle/duplicate-vertex
removal, normal unification, small-shell removal, hole filling in flat/
planar/smooth variants, self-intersection resolution, and voxel remesh/
solidify — the last requiring new vendoring of `MarchingCubes` and
`MeshSignedDistanceGrid` from g3Sharp); an `AutoRepairPipeline` composing five
of the six into one undoable step (voxel remesh stays manual-only by design,
per its "sledgehammer fallback" framing); and binary STL / ASCII OBJ export
writers. End-to-end verified by running the real default pipeline against
M1's `BrokenSample.stl` fixture and confirming its issues clear, then undo
restores them. 119/119 tests passing. No UI wiring (Repair panel, export
dialog) yet — flagged as a known gap for M3/M4, not built in this pass since
this milestone's scope didn't call for it.

**M3 — Edit** ✅ Complete (`reports/M3/`)
Plane cut, booleans, transforms, hollow, drain holes, decimation.
Delivered: all six v1.0 edit operations, wired into `MainWindow` as a tabbed
sidebar, each panel bound to the shared `MeshDocument` for undo/redo; a
plane-cut gizmo and a transform gizmo built as complete `IViewportGizmo`
implementations; and a Manifold C API native-interop layer
(`ManifoldInterop`/native `libmanifoldc`) backing the boolean
union/difference/intersection operations. Shipped with two known gaps: the
boolean/Manifold P/Invoke path had a broken native-library RUNPATH plus two
memory-lifetime bugs in the interop layer, so all 18 boolean-related tests
failed at merge time (documented, not silently ignored); and the plane-cut
and transform gizmos were built but never instantiated or connected to the
viewport — dead code at merge, activation buttons either stubbed or entirely
absent. Both fixed in M4, see below. 185/203 tests passing at merge (18
known Manifold failures); 220/220 + 8/8 GPU passing after the M4-0/M4-2
fixes.

**M4 — Polish and release** ← in progress
Packaging for three platforms, docs, website, sample files, crash-free on the test
corpus. Public 1.0.
Batch M4-0 (Manifold RPATH + interop fix) complete: rebuilt
`libmanifoldc.so`/`libmanifold.so.3` with a portable `$ORIGIN`-relative RPATH
instead of an absolute build-directory path; wired `Directory.Build.props` to
copy both into every project's own output (the `runtimes/<rid>/native/`
probing convention only applies automatically for NuGet-packed native
assets); and fixed two `ManifoldInterop` memory-lifetime bugs uncovered once
the library actually loaded — a buffer being freed out from under a
placement-constructed native object (segfault inside `libmanifold.so.3`) and
a null-pointer `memcpy` destination in `ExtractMeshGL64` (segfault in
`libc`). Also fixed inverted-winding test cube fixtures and a geometrically
unsound `PlaneCutTests` assertion uncovered along the way. Result: 204/204
unit tests + 8/8 GPU tests passing (previously 185/203 + 8/8).

Batch M4-2 (gizmo wiring + menu/undo-redo UI) complete: wired the plane-cut
and transform gizmos into the viewport, following the existing
`DrainHolePanel`/`DrainHoleGizmo` activation pattern (`SetGizmo`/
`SetGizmoActivationCallback`, an "activate" button, `MainWindow` owning
gizmo lifecycle across mesh reloads). Product direction adopted here and
going forward: this is a **gizmo-first** app — the 3D viewport interaction
is the primary way users set spatial parameters, textboxes are a typed
fallback, and once a gizmo has been dragged its values win outright on
Apply rather than merging with stale textbox contents. Wiring the transform
gizmo surfaced that its interaction math was itself unfinished, not just
unwired: pointer-picking tested distance-to-camera instead of where the
user clicked, rotate was a literal stub incrementing a fixed angle every
pointer-move regardless of drag, move used a hardcoded ray-projection
offset, and scale read raw camera distance instead of a since-press ratio —
all rewritten with real ray-based tracking rather than leaving the visibly
broken stub wired live. Also added: a `Menu` (File: Open/Exit; Edit:
Undo/Redo) with `Ctrl+Z`/`Ctrl+Y`/`Ctrl+Shift+Z` shortcuts and a toolbar
undo/redo status indicator, reusing a newly-extracted `RefreshFromDocument`
helper shared by the load and undo/redo paths. "Open Recent" was scoped out
— no settings/preferences persistence exists anywhere in the codebase yet,
and building one solely for that menu item was judged out of scope. Known
gap carried forward: `Viewport.Gizmo` is a single slot but each panel
tracks its own activation state independently, so activating a second
panel's gizmo silently steals the viewport from the first without telling
its panel — not a regression (the same gap existed with one gizmo,
DrainHole), just newly visible with three. Result: 220/220 unit tests + 8/8
GPU tests passing (13 new tests: 3 undo/redo, 6 plane-cut gizmo, 7
transform gizmo/rotate-math tests).

Batch M4-5 (viewport interaction hardening) complete, unplanned — prompted by
a crash on the first click into the drain-hole tool in a real run. Every
interaction defect found in this codebase so far had escaped a green suite for
one structural reason: no test drove a gizmo through a real camera.
`ViewportRaycaster.Unproject` appeared in exactly one test file (the one
testing it), and gizmo tests synthesised rays like `new ViewportRay(new
Vector3(0,0,3), -UnitZ)`, fixing the two variables the bugs actually depended
on — camera distance and display scaling. A camera framed on a 50 mm model
sits ~163 units away, not 3, so a test at distance 3 passed while every real
click failed. Delivered a `ViewportHarness` that drives gizmos through a
framed `OrbitCamera` and the production unprojection, and a picking contract
run against all three gizmos at four model radii × two display scalings:
clicking the gizmo claims the drag, clicking away from it does not (so camera
orbit survives), and its grab radius stays hittable. Fixed under it: `ToRay3d`
asserting a normalization it only had to float precision, crashing g3's
`FindNearestHitTriangle` on the first click; the plane-cut gizmo testing
camera distance rather than click location (the same bug already fixed in the
transform gizmo during M4-2 and not swept for elsewhere); two marker gizmos
composing the model matrix in the wrong order, rendering every drain-hole
marker at `position × radius`; and gizmos sized in fixed world units, which
gave the scale handle a 1px grab radius on a 50 mm model and 0px on a 500 mm
one — all five dimensions across two gizmos are now fractions of viewport
height resolved through a shared `GizmoScale`, so the shape that is drawn is
the shape that responds.

Batch M4-1 (real-world test corpus) complete: 53 third-party meshes, fetched
on demand and never committed — `tests/corpus/manifest.tsv` carries
filename/sha256/url/source/licence/defect notes, `scripts/fetch-corpus.sh`
downloads and re-verifies them into a gitignored directory, and
`CorpusSmokeTests` loads every file through the shipping importer and full
detector set to assert nothing throws. Not committing keeps licensing with the
upstream projects, keeps ~170MB of binaries out of git history permanently,
and keeps the set reproducible by checksum. Sources are 24 real consumer print
files from the Thingi10K research dataset via Hugging Face (all CC-BY/CC0/
public-domain; NC/ND/share-alike excluded so the set stays usable if ever
redistributed) and 29 research/scan models from `common-3d-test-models` and
`libigl-tutorial-data`. The corpus paid for itself twice on its first two
runs: it exposed `SelfIntersectionDetector` as an all-pairs O(n^2) scan (2.5s
on 5,800 triangles, hours per file extrapolated to the corpus's 269k-triangle
models, against §6.4's 5s budget for a 500k-triangle auto-repair) while
`SelfIntersectionRepair` already had the broadphase — both now share
`Spatial/SelfIntersectionSearch`, 17-84x faster with identical issue counts —
and it crashed the whole import of one real print file via a vendored-g3
limitation on fully-collinear triangles. See `reports/M4/CORPUS.md`.

Also delivered outside the batch plan: **OBJ import** (`ObjReader` +
`MeshImporter`), which §5.1 has always listed in v1.0 scope but which no
milestone had built — the app could not open an OBJ at all. It deliberately
does not weld coincident vertices: OBJ already carries the author's indexing,
and welding on import would repair the file behind the user's back, hiding the
very defects Inspect exists to report. Implementing it uncovered that
`src/Meshwright.IO/Obj/ObjWriter.cs` had **never been compiled**: MSBuild's
`DefaultItemExcludes` covers `obj/**` and matches globs case-insensitively, so
a source directory named `Obj` is silently excluded on Linux too. OBJ export
was absent from the shipping DLL with zero references anywhere, and its 8
tests had never run while the suite reported green — M2 recorded ASCII OBJ
export as delivered, and it has never existed in a build. Fixed at the root by
renaming both directories to `Wavefront/`.

Batch M4-6 (corpus ground truth) complete: `CorpusGroundTruthTests` checks the
detectors against Thingi10K's independent per-file analysis, recorded in the
manifest. Exact counts are deliberately not asserted — two implementations
legitimately count one defect differently — but the direction that harms users
is: a mesh the reference calls clean in a category must not be reported as
defective in it, since false positives push users into "repairing" good
geometry. That comparison immediately found the most serious defect of this
work: **import was silently discarding geometry**. `DMesh3` cannot represent a
non-manifold edge, so `AppendTriangle` refuses such triangles and returns
`NonManifoldID` — and both readers discarded the return value. 14 of the 24
real print files lost triangles, two of them ~73%, after which every detector
was describing a different mesh from the one the user opened (204394's
reference count of 34,905 self-intersections came back as 16 because most of
the mesh was never loaded). Batch M4-7 fixed it properly: `NonManifoldMeshBuilder`
now keeps that geometry by **splitting the mesh at the offending vertices
instead of dropping the triangle** — duplicating a vertex gives the triangle a
fresh edge to attach to, so it lands at exactly the right position while the
topology stays legal. The geometry is complete; only the connectivity is cut,
which is an honest description of a non-manifold junction. This is the
representation `NonManifoldDetector` was always written for ("several distinct
edge ids that share the same pair of vertex *positions*") but which nothing
produced, so it could only ever report defects the importer had already thrown
away. Every corpus file now loads 100% of its triangles, asserted per file
against the reference's face count. Cutting connectivity leaves seams that
vertex-id-based detectors mistake for defects — the first run reported 13,348
phantom holes on a closed file — so `BoundaryHoleDetector` and
`DisconnectedShellDetector` now reason about positions too (`PositionTopology`),
extending the pattern `NonManifoldDetector` already set. Agreement with the
reference improved sharply: 204394's shell count went from 4,757 to 31 against
a reference of 32. See `reports/M4/CORPUS.md`.

Also delivered outside the batch plan: **mesh export**. §5.1 requires STL and
OBJ export in v1.0 and M2 delivered both writers, but `Meshwright.App`
contained no reference to `StlWriter` or `ObjWriter`, no save-file picker and
no Export menu item — flagged in "Immediate next steps" as the largest
remaining v1.0 functional gap, since a user could open and repair a mesh but
not get it back out. Added a `MeshExporter` (extension-to-writer dispatch,
mirroring `MeshImporter`'s `SupportedExtensions`/`SupportedPatterns` shape so
the save-picker filter and the writer set cannot drift apart) and wired a File
> Export... menu item/toolbar button into `MainWindow`, format chosen from the
picked file's extension, errors surfaced on the status line the same way
`OnOpenFileClick` already does. Per AGENTS.md's note that `ObjWriter` compiled
and ran for the first time only recently and is far less battle-tested than
its age suggests, it was exercised against the full M4-1 corpus rather than
just hand-built fixtures: every one of the 53 corpus meshes exported to both
STL and OBJ and reimported through the shipping importer with zero triangles
dropped and an unchanged triangle count. A bit-identical round trip was
deliberately not the invariant checked — import now splits non-manifold
geometry rather than dropping it, and STL's triangle-soup shape means vertex
count can legitimately differ from what was exported — so "export loses no
triangles, reimport drops none" is what was actually asserted. See
`reports/M4/20260904T213615Z-batch2-mesh-export/report.md`.

Batch M4-3 (CI + packaging) complete for Linux first, scoped that way by explicit
decision — Windows/macOS CI and packaging are a follow-up batch, since
neither can be built or verified on this dev host. Delivered
`.github/workflows/ci.yml` (GitHub Actions on `ubuntu-24.04`: restore,
build, cache + fetch the M4-1 corpus, install Xvfb + Mesa, run both test
projects under `xvfb-run`; the Manifold native libs are already committed to
git so CI never needs to build Manifold from source) and
`scripts/package-linux.sh` (self-contained single-file `linux-x64` publish
packaged as a `.deb` — `/opt/meshwright`, a `/usr/bin` symlink, a `.desktop`
entry). AppImage was scoped out — needs `appimagetool`, unavailable on this
host/via apt; `.deb` alone covers Debian/Ubuntu/Mint, this project's own dev
platform. Verified locally (GitHub Actions itself can't be triggered from
this session): full build + both test suites green (432 + 8), the packaged
`.deb` built, inspected with `dpkg-deb -c`/`-I`, and its installed binary
launched cleanly from a scratch extraction root with no missing-library
errors. Not verified: the CI workflow's actual execution on GitHub (this
session had no `sudo` to install Xvfb and confirm that path locally), and
any real GUI rendering (no way to see a window from this environment — only
process-start was smoke-tested). See
`reports/M4/20260904T214856Z-batch-linux-packaging-ci/report.md`.

Batch M4-4 (docs/release, first pass) complete: a `docs/index.html` static
project site (GitHub Pages, served from `/docs` on `main` — no build step,
no external dependencies), a `samples/` directory with two small original
STL fixtures (`sample-tetrahedron.stl` clean, `broken-cube.stl` with three
deliberate defects, both already used as test fixtures elsewhere so no new
licensing surface) so a first-time user has something to try Inspect/Repair
on without hunting down a real file, and an expanded `README.md` (build/run/
test/package instructions, links to the site and samples). Scoped to what's
actually true today: no binaries are published anywhere, so the site's "Try
it" section is honest about that and points at building from source rather
than a nonexistent download link. Verified by rendering `docs/index.html`
in headless Chromium and reviewing the screenshots (layout, both theme
branches present in the CSS, all internal links resolve to real anchors);
GitHub Pages itself was not exercised since no remote is pushed yet from
this session. See the name-search finding below and
`reports/M4/20260904T230000Z-batch-docs-release/report.md`.

Batch M4-8 (make the app do what it says) complete, largely unplanned —
prompted by the user reporting that "a lot of stuff I try doesn't really do
anything". It didn't: **no edit operation had ever been visible**. All six
Edit panels applied their operations straight to `MeshDocument`, but the
viewport and diagnostics panel were only refreshed by load, undo and redo —
the three call sites of `RefreshFromDocument`. Operations mutate the mesh in
place, so the viewport kept rendering its already-uploaded copy and the
diagnostics panel kept showing the pre-operation report. All eight `Apply`
call sites across all six panels changed the mesh with nothing on screen
moving; only an unrelated undo/redo revealed it afterwards. `MeshDocument`
now raises `Changed` after load/apply/undo/redo, naming what caused it, and
`MainWindow` refreshes from that one event, so a panel cannot forget to ask.

Closed M2's deferred UI gap: **the entire Repair feature set had no UI**.
`AutoRepairPipeline` and all six individually-runnable operations from §5.1
were implemented and tested but referenced from nothing outside the test
project, while §7's M2 entry and the project site both described Repair as
delivered. Added a Repair tab — first in the sidebar, since inspect-then-repair
is the primary workflow — with one-click Auto Repair plus each step
individually, voxel remesh kept below a separator and labelled a last resort
per the §11 decision excluding it from the default sequence.

Plane cut was rebuilt after five separate defects, any one of which made it
look inert or destructive: Keep and Discard appended their result instead of
replacing it, so the half being cut away stayed; Discard never built the
negative side, leaving its result selection as dead code that fell through to
the positive side, so Discard did exactly what Keep did; `Split` was a stub
falling through to the Keep operation behind a "for now" comment, silently
discarding the half the mode exists to keep; cap loops were extracted from the
split mesh but handed to cap routines that index the mesh being filled;
and — the root cause of the rest — `SplitMixedTriangle` re-triangulated only
the lone-corner side of each straddling triangle and appended cut vertices per
triangle rather than per edge, dropping a strip of surface along the entire cut
and tearing the surface apart along it. A cut cube came back as six loose faces
rather than one solid.

That last one made repair *actively destructive*, which is the finding worth
carrying: on a halved Menger sponge, Auto Repair's small-shell step could not
distinguish the cut's fragments from debris, deleted the model's cut end, and
hole filling then sealed the stump — reporting **"0 issues found"** while
taking 11% off the model's height. The only visible tell was the bounding box
shrinking, which nothing asserted.

Also in this batch: `OrbitCamera` now treats Z as up, so print files stop
loading on their side; Reset View (`Ctrl+0`) and explicit `FrameMesh()`
framing; decimation reports when it cannot reach its target instead of
presenting a 558× shortfall as success; a command-line file argument; and
`docs/usage.html`, a usage guide screenshotted from real sessions with a
"known rough edges" section listing what is still wrong.
453 unit tests + 8 GPU tests passing.

Batch M4-9 (correctness gaps closed) complete: all five gaps handed off after
M4-8 — the multi-loop cut cap (item 12), long operations off the UI thread
(item 13), booleans between loaded meshes (item 14), a genuinely smooth hole
fill (item 15), and gizmo coverage for Hollow (item 16). 453 → 520 unit
tests, 0 skipped. The GPU suite is *not* included in that figure: it hung
past ten minutes on the dev host and was abandoned rather than reported as
passing. Two further GPU test hosts from earlier sessions were found already
hung on the same machine, so this looks environmental rather than caused by
this batch — but it is unverified either way, and worth its own look. See §11
for the decisions each item produced.

The headline fix is the cut cap. Cutting the sample Menger sponge — a mesh the
app itself reported as having zero issues — used to yield 892: 70 non-manifold
edges, 804 self-intersections and 17 flipped faces, all of it in the cap. It
now yields none, with the volume, bounds and shell count unchanged.

Every item in this batch was verified by driving the running application, and
that is the reason the batch is worth recording. The Hollow gizmo came back
green from its author and wrong on screen: it anchored along -Y, from before
the same day's Z-up decision, and its tests asserted `point.Y == 1`, so they
passed while the handle pointed sideways out of the model. Three further
defects were found only by looking, none of them in the work being reviewed —
uppercase `.STL` files were invisible in the file dialogs, Reset View does
nothing, and every panel's before/after readout compares the mesh with itself.
The last two are now items 17 and 18, and were fixed in the same batch. A
fourth, found the same way, is fixed here without its own item: the busy
indicator introduced by item 13 read "Working: Loaded..." during a hollow,
because it displayed the document's *last completed* change rather than the
running operation's name. None of the four would have surfaced from the suite.

Verification of this batch was done by driving the real GUI until the last
change, the busy-indicator label, which is covered by a test asserting the
running operation's name is reported and is not the previous change's — but
was not confirmed on screen, because the display was needed elsewhere.

Windows/macOS CI + packaging landed later and is now green on all three
platforms; see the 2026-09-05 and 2026-09-06 rows in §11.

**M5+**
v1.x features, then the resin module.

## 8. Licensing and funding

**Model:** open source core, paid convenience — the Krita/Aseprite/Ultimaker pattern.

- Source is public under a permissive licence (MPL-2.0 or Apache-2.0). Anyone can
  build it themselves.
- Prebuilt, signed, auto-updating binaries are sold: **one-time ~€30, includes all
  1.x updates.** Not a subscription.
- The free and paid downloads are the **same binary**. Editing and export features
  are gated behind a licence key; inspection, diagnostics and the repair report are
  always available. One build to produce, test and ship; upgrading is entering a key,
  not reinstalling.
- GitHub Sponsors and a donate button as a secondary channel.
- Consider a separate commercial support/batch-CLI tier later, aimed at print shops.

Rationale: pure donations on a desktop tool historically return near zero. A cheap
one-time paid build converts far better, keeps the community goodwill of open
source, and matches how this audience already buys tools.

## 9. Risks

| Risk | Mitigation |
| --- | --- |
| **Scope creep into "another Blender"** | The non-goals in §2 are binding. Every feature must answer "does this get a mesh to the slicer?" |
| Robust booleans and self-intersection repair are genuinely hard | Use Manifold rather than writing one; voxel remesh as the always-works fallback |
| Avalonia + OpenGL interop friction | Tackled in M0, before anything else is built |
| g3Sharp is unmaintained | Vendor only the needed parts under Boost licence and own them outright — see §6.2 |
| Licence gating in a single open-source binary is trivially patched out | Accepted. The paid build sells convenience and support, not DRM. Keep the check simple and unobtrusive |
| No users notice the release | Build in public from M1; the "Meshmixer is dead" story is the marketing hook |
| macOS notarisation cost/hassle | Linux + Windows first; macOS once there is revenue |

## 10. Open questions

- Name and domain availability — "Meshwright" is a placeholder, deferred until later.
  A quick check during M4-4 found an active Florida LLC, "MeshWright, LLC,"
  selling unrelated wire-mesh reinforcement design software under
  "MeshWright Designer" — same spelling, same broad category (design
  software), different industry (construction rebar vs. 3D-print mesh
  repair). Low risk for a free/open-source hobby project with no
  commercial use of the name today; worth a real look (trademark search,
  not just a web search) before any paid release under §8's plan.
- Which permissive licence: MPL-2.0 (file-level copyleft, keeps improvements public)
  or Apache-2.0 (maximum adoption)?
- Ship Manifold as a prebuilt native binary per platform, or build it from source in
  CI? Affects release complexity considerably.

## 11. Decision log

| Date | Decision |
| --- | --- |
| 2026-08-29 | STL + OBJ only for 1.0; 3MF and PLY deferred to v1.x |
| 2026-08-29 | Free tier is the same binary with editing disabled, not a separate build |
| 2026-08-29 | Vendor selected g3Sharp components rather than forking the whole project |
| 2026-08-29 | Removed the mis-targeted Debian trixie apt repo |
| 2026-08-29 | Target .NET 10 (LTS), installed system-wide via apt. .NET 9 was briefly used and discarded: it is STS and went out of support in May 2026 |
| 2026-08-29 | M0 (Skeleton) complete: solution scaffolding, STL import, orbit camera, Silk.NET renderer, Avalonia integration. GPU pixel output unverified on the headless dev host — flagged for a manual smoke test |
| 2026-08-31 | M1 (Inspect) complete: real vendored g3Sharp tree (not a handwritten subset — an earlier attempt at this was rejected on review), all 7 v1.0 detectors, diagnostics UI, and GPU pixel-diff-verified error highlighting |
| 2026-08-31 | Every vendored g3Sharp file carries its own per-file Boost Software License 1.0 header, beyond upstream's repo-root-only licensing, to keep provenance unambiguous file-by-file |
| 2026-09-02 | M2 (Repair) complete: `IMeshOperation` contract + snapshot undo stack, all six repair operations, `AutoRepairPipeline`, and STL/OBJ export |
| 2026-09-02 | Voxel remesh/solidify is intentionally excluded from the default `AutoRepairPipeline` sequence — it discards fine detail, so it stays a manual, individually-runnable fallback rather than something every Auto Repair run pays for |
| 2026-09-02 | M2 shipped without UI wiring (no Repair panel or export dialog) — the milestone's task scope named pipeline/operations/undo/export without a UI requirement, so it was treated as out of scope rather than assumed; deferred to M3/M4 |
| 2026-09-04 | M3 (Edit) complete: all six v1.0 edit operations wired into `MainWindow`, plane-cut and transform gizmos built (but not yet connected to the viewport), Manifold C API interop for booleans. Shipped with the 18 boolean tests known-failing (Manifold RUNPATH pointed at an absolute build-tree path) — documented as an M4 blocker rather than silently accepted |
| 2026-09-04 | M4 batch 0: fixed the Manifold RUNPATH (now `$ORIGIN`-relative, both `libmanifoldc.so` and `libmanifold.so.3` shipped and copied into every project's output) and two further memory-lifetime bugs in `ManifoldInterop` that only surfaced once the library could actually load (a placement-constructed object's backing buffer freed before use; a null-pointer `memcpy` destination in mesh extraction). Also fixed inverted-winding test fixtures and an unsound `PlaneCutTests` assertion found while chasing the above. 204/204 tests + 8/8 GPU now passing |
| 2026-09-04 | Adopted a gizmo-first UI direction: the 3D viewport is the primary way users set spatial parameters going forward, textboxes are a typed fallback, and a touched gizmo's values win outright on Apply rather than merging with textbox contents |
| 2026-09-04 | M4 batch 2: wired the plane-cut and transform gizmos into the viewport (dead code since M3) following the `DrainHolePanel` activation pattern; discovered and fixed the transform gizmo's interaction math was itself unfinished, not just unwired (rotate was a stub incrementing a fixed angle per pointer-move regardless of drag, pointer-picking tested camera distance not click location) rather than wiring the visibly-broken stub live. Added a File/Edit menu with undo/redo keyboard shortcuts and a status indicator, reusing a newly-extracted `RefreshFromDocument` helper. Skipped "Open Recent" — no settings persistence exists in the codebase, not worth building solely for this. 220/220 tests + 8/8 GPU now passing |
| 2026-09-04 | Gizmos are sized as a fraction of viewport height, not in world units, resolved through a shared `GizmoScale` that both the render and pick paths call. Fixed world sizes only work at one zoom: the scale handle had a 1px grab radius on a 50mm model and 0px on a 500mm one. Bounds-derived sizing was considered and rejected — screen size must be invariant to *camera distance*, not model size, or the gizmo breaks again the moment the user zooms |
| 2026-09-04 | Gizmo interaction is tested through a real `OrbitCamera` and the production unprojection (`ViewportHarness`), never a hand-built ray. Every interaction defect found so far escaped a green suite because synthetic rays fixed the two variables the bugs depended on: camera distance and display scaling |
| 2026-09-04 | `GizmoPointerEvent` carries the frame's view/projection as required (not defaulted) fields. `default(Matrix4x4)` is all zeros and would silently yield a nonsense scale — the quiet-wrong-answer failure mode this work exists to remove |
| 2026-09-04 | OBJ import does not weld coincident vertices, unlike STL import. STL is triangle soup so welding is the only way to recover an indexed mesh; OBJ already carries the author's indexing, and merging on import would repair the file behind the user's back and hide the duplicate-vertex and non-manifold defects Inspect exists to report. Import stays faithful; repair stays the user's choice |
| 2026-09-04 | Source directories must never be named `Obj`/`obj`. MSBuild's `DefaultItemExcludes` covers `obj/**` and matches case-insensitively on every platform, so such a directory is silently dropped from compilation. `ObjWriter` and its 8 tests had been invisible since M2 while the suite reported green; both directories renamed to `Wavefront/` |
| 2026-09-04 | The test corpus is fetched, never committed: a manifest of checksums plus `scripts/fetch-corpus.sh`. Keeps licensing with the upstream projects, keeps ~170MB of binaries out of git history permanently, and keeps the set reproducible. Corpus meshes are restricted to CC-BY/CC0/public-domain so the set stays usable if it is ever redistributed — NC and ND models are excluded because §8 sells binaries |
| 2026-09-04 | Thingi10K via Hugging Face is the source for real print files. Thingiverse and Printables both refuse automated download (403) and bulk fetching breaks Thingiverse's terms; the research mirror is both legitimate and strictly better, adding per-file licence and per-file defect ground truth. Epic's Sketchfab/Fab were checked and rejected: OAuth-gated, and art assets rather than print files |
| 2026-09-04 | `SelfIntersectionDetector` and `SelfIntersectionRepair` share one `SelfIntersectionSearch`, so detection and repair cannot disagree about what a self-intersection is. The detector's former O(n^2) all-pairs scan was a documented M1 shortcut that the corpus made untenable |
| 2026-09-04 | The corpus asserts *direction*, not exact counts, against Thingi10K's ground truth: a mesh the reference calls clean must not be reported defective. Two implementations legitimately count one defect differently, but a false positive pushes a user into "repairing" good geometry, so that direction is the one worth pinning |
| 2026-09-04 | Import reports the geometry it cannot represent rather than dropping it silently (`MeshImportResult`). `DMesh3` cannot hold a non-manifold edge and `AppendTriangle` refuses those triangles; ignoring that return value made 14 of 24 real print files load incomplete, two at ~73% loss, with every downstream diagnostic then describing a different mesh. Loading such geometry properly is a data-structure decision deferred to its own milestone; misreporting it is not acceptable in the meantime |
| 2026-09-04 | Import keeps non-manifold geometry by splitting the mesh at the offending vertices rather than dropping triangles. `DMesh3` cannot represent a non-manifold junction, so the only faithful options were losing geometry or cutting connectivity; cutting is strictly better, since the surface stays complete and correctly positioned and the junction genuinely has no single consistent surface to connect to |
| 2026-09-04 | Topology-derived detectors reason about vertex *positions*, not vertex ids (`PositionTopology`). The mesh structure under-represents the true topology, so a seam left by splitting is indistinguishable from a hole by id alone. `NonManifoldDetector` established this pattern; `BoundaryHoleDetector` and `DisconnectedShellDetector` now follow it. A consequence, accepted deliberately: a crack from near-coincident vertices is reported as duplicate vertices rather than as a hole, because that names the cause and points at the repair that fixes it |
| 2026-09-04 | M4-3 (packaging & CI) scoped to Linux only for its first batch, by explicit decision — Windows and macOS can't be built or verified on this dev host, so doing all three at once would mean shipping unverified config. `.deb` chosen over AppImage for the first Linux package format: covers this project's own dev platform (Debian/Ubuntu/Mint) and `appimagetool` isn't available on this host or via apt |
| 2026-09-05 | Every mesh change announces itself through one event (`MeshDocument.Changed`) and the UI refreshes from that single subscription, rather than each caller remembering to refresh. Operations mutate the mesh in place, so a panel that applies one and doesn't refresh leaves the viewport rendering its uploaded copy and the diagnostics panel showing the pre-operation report — which is exactly what had happened to all eight `Apply` call sites across all six Edit panels, making every edit in the app invisible while the suite stayed green |
| 2026-09-05 | Camera framing is explicit (`FrameMesh()`), never a side effect of assigning `MeshViewportControl.Mesh`. Once every operation refreshes the viewport, framing on assignment would snap the camera back on each Apply; opening a file and Reset View are the only two things that should move the user's view |
| 2026-09-05 | Z is up. STL, 3MF and the print bed all put the build direction along +Z, so the Y-up convention inherited from realtime graphics showed practically every real print file lying on its side. Also added Reset View (`Ctrl+0`) — orbiting or zooming could put the mesh off screen with no way back short of reopening the file |
| 2026-09-05 | M2's deferred Repair UI shipped as M4-8, closing a gap that had been open since 2026-09-02. Deferring UI wiring because a milestone's task scope didn't name it left the app's headline feature — and every operation behind it — unreachable for three days while the spec and the public site both described Repair as delivered. Milestones that deliver a user-facing capability should not be called complete while nothing in the UI reaches it |
| 2026-09-05 | Geometry tests assert invariants, not existence. `PlaneCutTests` asserted `TriangleCount > 0` after a cut, which passes just as happily when the operation appends its result on top of the half it was asked to discard. The invariants that actually catch these are bounding box, volume, shell count and issue count compared before and after — a cut must leave nothing on the discarded side, a split must preserve total volume, and a cut must produce a closed shell |
| 2026-09-05 | A repair that cannot fix something must say so rather than delete it. Auto Repair's small-shell step removed a cut model's fragmented end as if it were debris, hole filling sealed the stump, and the pipeline reported "0 issues found" while the model had silently lost 11% of its height — a worse outcome than the damage it was asked to repair, and invisible except in the bounding box. Decimation had the same shape of bug, reporting a 558× shortfall as plain success; it now names the target it missed and why |
| 2026-09-05 | Documentation records what is wrong as well as what works. `docs/usage.html` carries a "known rough edges" section and its screenshots are real sessions including unflattering ones, because the first version of that page used a screenshot of a mesh Auto Repair had quietly mutilated as its success story |
| 2026-09-05 | A cut cross-section is recovered from real edge connectivity, never by sorting intersection points by angle. Angular sorting can only describe one star-shaped loop, so any cut through a model with a hole in it produced a cap zig-zagging between separate boundary loops. Cutting the sample Menger sponge — a clean mesh reported as having zero issues — produced 892 issues: 70 non-manifold edges, 804 self-intersections and 17 flipped faces. Loops are now nested by parity, so a loop inside an odd number of others is a hole in the cap and one inside an even number is filled. |
| 2026-09-05 | Cap correctness is asserted by area, not just by closure. The multi-loop cap tests compute the cross-section's true area by hand (256/81 for the level-2 sponge cut at z=0.5) and compare, because a cap that spans the model's holes still produces a closed, single-shell, correct-volume result and passes every other invariant. |
| 2026-09-05 | File-picker patterns list every case variant of each extension. GTK matches `FilePickerFileType` patterns case-sensitively, so the lower-case-only list hid `Model.STL` — routine CAD exporter output, and the form of this project's own Eiffel tower corpus file — from the Open dialog with nothing explaining why. Import and export already accepted any case; only the dialog was affected. |
| 2026-09-05 | Gizmos anchor along +Z, not +Y. The Hollow gizmo shipped its first round casting its anchor ray along -Y with a +Y fallback, a leftover from before the Z-up decision earlier the same day, so the handle pointed sideways out of the model; its tests asserted `point.Y == 1` and so passed while the feature was visibly wrong. A surface anchor also cannot rely on a single ray through the bounding-box centre: on a Menger sponge that ray goes straight through the hole in the middle of each face and the no-hit fallback placed the handle in mid-air, attached to nothing. |
| 2026-09-05 | §6.4 gained a responsiveness target (UI thread blocked < 100 ms per operation) alongside its throughput targets. Throughput numbers alone let "Auto-repair, 500k triangles: < 5 s" pass even if every one of those 5 seconds froze the window — which, before item 13, it did: Hollow on a 2112-triangle mesh blocked the UI thread for ~25 s with no spinner, no repaint, no way to tell the app from a hang. A tool whose whole pitch is staying usable on meshes the target users' current tools choke on needs a number for "stays usable while working," not just "finishes quickly" |
| 2026-09-05 | Item 13 (long operations off the UI thread): `MeshDocument.ApplyAsync` runs the operation via `Task.Run` and awaits it with the calling context captured, so `Changed`/`BusyChanged`/`Progress` all fire back on the caller's own thread (the UI thread, in practice) — no explicit `Dispatcher.Invoke` needed, and `Meshwright.Core` stays Avalonia-agnostic. `IsBusy` blocks `Apply`/`Undo`/`Redo` from running concurrently with the background mutation, closing the Ctrl+Z-mid-operation race. Progress is real only for `AutoRepairPipeline` (`IProgressReportingMeshOperation`, step-based, so cancellation is honoured between steps too); every other operation is one opaque call into vendored/native geometry code with no safe midpoint, so the UI shows an honest indeterminate spinner with Cancel disabled for those rather than a percentage that isn't tracking anything (§4). Verified live on the real GUI: orbiting the viewport mid-drag while a 25-second Hollow ran on the Menger sponge sample actually rotated the model on screen, and Auto Repair on the 139,989-triangle Eiffel tower sample completed correctly (36,708 → 6,162 issues) while showing the busy indicator throughout |
| 2026-09-05 | A "smooth" fill has to be measurably smoother than a planar one, and flat where the surface is flat. `HoleFillMode.Smooth` ear-clipped the loop and added one centroid vertex relaxed onto three *fixed* boundary corners, so the relaxation converged in a single step and the result was indistinguishable from `Planar` — a distinct mode in §5.1 that did nothing distinct. It now refines the patch for interior degrees of freedom and displaces it by a curvature-derived sagitta, sampling curvature one ring in from the boundary because a boundary vertex's one-ring is missing the hole side entirely and measured about five times too curved. The tests pin the *improvement*, not the implementation: closer to a test sphere than planar by a clear margin, a flat plate staying flat to 1e-9, and all three modes differing measurably, so none can quietly collapse into another again |
| 2026-09-05 | The GPU suite's hangs were xunit parallelism, not the GPU. Three test classes each took their own `IClassFixture<GpuTestFixture>`, and xunit runs collections in parallel by default, so several fixtures called `Glfw.CreateWindow` concurrently; GLFW/GLX window creation on Linux is not thread-safe and the race hangs forever, which is why the suite was green at M4-8 with fewer GPU test classes to race. Diagnosed from managed stacks off a live hung host showing two threads stopped inside `GpuTestFixture..ctor` — captured through the .NET diagnostic IPC sockets in `/tmp`, since `ptrace_scope=1` blocks gdb from attaching to a non-descendant without sudo. The tell that it was never a driver stall: 22 s of CPU over 94 minutes and no thread in a DRM ioctl. Fixed by `DisableTestParallelization` for that assembly; the suite now completes in 483 ms rather than hanging past ten minutes. Always run it under `timeout`, or a hang orphans a test host — five had accumulated on the dev host, one for 22 hours |
| 2026-09-05 | M4-3's Windows/macOS half landed **unverified by construction**, and says so. The 2026-09-04 Linux-only decision held on the point that mattered — this dev host cannot *run* a Windows or macOS build — but its implied corollary was wrong: GitHub's `windows-latest`/`macos-latest` runners are real machines with their own toolchains, so CI can build Manifold per-platform rather than shipping a placeholder. New `build-and-test-windows`/`build-and-test-macos` jobs build Manifold from source (no prebuilt binary is committed for those platforms, unlike `linux-x64`) and run `Meshwright.Tests` only, excluding the GPU suite since the runners have no GPU. Packaging (`package-windows.sh` zip, `package-macos.sh` unsigned `.app` zip; no MSI, no notarisation) ships honestly: a build without the native library disables Boolean behind a `NOTICE.txt` instead of crashing when the user clicks it. Two real bugs were found while verifying rather than assuming: `Directory.Build.props` unconditionally bundled the Linux `.so` into Windows/macOS publishes, so a Windows package carried unloadable Linux binaries (now RID-gated, and checked in both directions — a Windows publish now carries no natives, a Linux one still carries both); and `fetch-corpus.sh` verified checksums with `sha256sum`, which stock macOS does not have, where both call sites treated the missing tool as a checksum mismatch and would have failed the macOS job with 53 bogus mismatches blaming the corpus. The DllImport naming reasoning — `libmanifoldc.dll`/`libmanifoldc.dylib` matching .NET's default probing — held; what it missed was the *location* rather than the name, which cost two further CI rounds (see the 2026-09-06 row) |
| 2026-09-06 | Native libraries are copied **next to the managed assemblies**, not only into `runtimes/<rid>/native/`. Every Manifold test failed on macOS with `DllNotFoundException` while the dylib sat correctly under `runtimes/osx-arm64/native/` in the test output. CI diagnostics eliminated every other explanation: the file was arm64, ad-hoc signed, carried `@loader_path` on its `LC_RPATH`, had its dependency resolving, and a plain `ctypes.CDLL` of that exact path printed `dlopen OK`. The library was always loadable; .NET never looked there. `runtimes/<rid>/native/` is a deps.json contract and these loose files are not in deps.json, so the fact that it works on Linux is incidental rather than guaranteed — which is why the gap survived until a second platform ran the same code. Both libraries are copied, not just the entry point, since `libmanifoldc` records its dependency on `libmanifold` as `@rpath`-relative and the two must sit together. Verified without a Mac: hiding `runtimes/` in the Linux test output reproduces the macOS condition exactly — the interop tests failed that way before the change and pass on the flat copy after it |
| 2026-09-06 | A control that reads a value it never uses is worse than a missing control: it is a promise the app breaks silently. A UX audit of the real GUI found three — Plane Cut's "Add Cap" checkbox (parsed into a local and discarded; no cut operation has an uncapped path at all, so §5.1's "optional cap" is unreachable), Drain Holes' "Countersink Depth" (validated, echoed into the summary as "1mm countersink", never used to change geometry), and the drain-hole gizmo's diameter (hard-coded to 2.0 at placement, so the Placed Holes list and the viewport marker both describe a hole the user did not ask for). Each shipped behind a green suite because the tests drive the operations directly and never assert that a UI control changes the operation's output — the gap a test suite cannot see unless something asserts the wiring itself. **Closed** the same day: all three now drive the operation, and `DrainHoleParameterWiringTests` starts at a typed field value and ends at a measurement of the resulting mesh |
| 2026-09-06 | Drain Holes does not drill; it deletes whole triangles inside the requested radius and adds nothing. On a coarse mesh a Ø0.5 mm request removed a full 2 × 2 mm face — 16× the requested area — leaving the model open (0 issues → 1 boundary hole, surface area 24 → 20, vertex count unchanged at 8, proof nothing was constructed) while reporting "Applied 1 drain hole(s)… (Ø0.5mm, removed 2 triangles)". The honesty failure is one line: `DrainHoleResult.DiameterAchieved` is documented as the achieved diameter but returns the *requested* one verbatim, so it can never disagree with the request, and `DepthDrilled` is `diameter * 2.0` — a constant dressed as a measurement. A field named for what was achieved must be measured from the result, never copied from the input. **Closed** — see the drilling row below |
| 2026-09-06 | Decimation names the target it missed (correct, per the 2026-09-05 row) but the reason it gives is false: reducing the clean Menger sponge to 734 triangles introduced 67 self-intersections while the summary said further collapses "would have created invalid geometry". A quality-collapse loop that refuses individual collapses on a *local* validity test still has to be checked against the whole-mesh invariants afterwards — the app's own status bar reported the 67 issues in the same frame the panel denied them. **Open** |
| 2026-09-06 | Most of §5.1's "Viewport / UX" block was never built, and nothing said so. Orthographic projection (`OrbitCamera` only ever constructs a perspective matrix), standard view presets (the View menu holds Reset View alone), the build plate grid, the out-of-bounds warning, the wireframe and x-ray display modes and the cross-section preview slider are absent from the code entirely — not unwired, as M2's Repair UI was, but never written. So are import's unit detection/mm-inch scaling, drag-and-drop and recent-files list. This is the M2-Repair-UI shape of gap at larger scale: the spec, `README.md` and `docs/usage.html` all described a viewport richer than the one that exists. `docs/usage.html` now documents each absence; the scope decision — build them for v1.0 or move them to v1.x — is still open |
| 2026-09-06 | Detection and repair must agree about what a defect is, the way `SelfIntersectionDetector` and `SelfIntersectionRepair` already share one search. `BoundaryHoleDetector` excludes import seams via `PositionTopology.SeamEdges`, so it correctly reports no hole at a non-manifold junction that import split; `HoleFillRepair` finds its loops with `MeshBoundaryLoops`, which is vertex-index-based and has no such exclusion, so the same junction is an open loop to it. Inspect can therefore report zero holes on a file whose Auto Repair run adds geometry across a seam. Found while fact-checking a documentation claim, which is its own argument for writing the limitations down. **Open** — documented in `docs/usage.html`, not yet reconciled in code. Note this is a *different* defect from the `DMesh3.Copy` timestamp row below, which also made openings invisible: that one is fixed, this one is not |

| 2026-09-06 | Drain holes cut the circle they are asked for, and the opening is measured from the result. The old implementation deleted every triangle within the requested radius and added nothing; it now grows a local patch, projects the requested circle onto it, refines the patch outline and lofts outline to rim. The refinement is not a nicety: a coarse outline — the four corners of a cube face — fans onto the rim from far enough away that part of the arc is hidden behind the hole, and those triangles come out inverted and overlapping, which the *signed* area is happy with and only the absolute area catches. On the 12-triangle cube a Ø0.5 mm hole now costs 0.195 mm² against πr² = 0.196, takes the vertex count 8 → 100, leaves exactly one boundary loop measuring Ø0.5 mm and does not move the bounding box. `DiameterAchieved` and `CountersinkAchieved` are read back off the mesh by walking that loop, so they can disagree with the request; `DepthDrilled`, which was the constant `diameter * 2.0`, is gone rather than patched. A hole that does not fit is refused with the mesh untouched, and the message names the largest diameter that does |
| 2026-09-06 | A drain hole is **open by design**: it adds exactly one boundary loop, the requested circle, rather than boring a closed tube. §5.1 pairs drain holes with Hollow, and the point is connecting the cavity to the outside; a boolean-subtracted cylinder would add a tube wall's worth of surface and still leave a hollowed model's cavity sealed behind the far wall. Inspect therefore reports one boundary hole per drain hole, which is the truth about the model — and Auto Repair's hole filling would undo a drain hole if run afterwards. Countersink is implemented as the 45° chamfer its doc comment always described, clamped and reported when the material or the local surface cannot take it, rather than removed as a dead control |
| 2026-09-06 | `DMesh3.Copy(DMesh3, …)` replaced every buffer in a mesh without calling `updateTimeStamp`, unlike every other mutator in that file, and `CachedBounds`/`CachedIsClosed` recompute only when the stamp moves. `MeshBoundaryLoops` early-returns *no loops* when `CachedIsClosed` is true, and `MeshDocument.Load` primes that flag on every open — so after any operation using that overload, geometry the operation had opened was invisible to hole detection, hole filling and the diagnostics panel alike. Nine call sites were exposed: all three plane cuts, all three booleans, `VoxelRemeshRepair` and `RemoveDegenerateAndDuplicatesRepair`. Two agents hit the same wall independently, one drilling a drain hole and one opening an uncapped cut, which is what made it look like a shared cause rather than two feature bugs. Fixed centrally in the vendored file with the deviation recorded in `VENDOR.md`; a local re-stamping workaround added in `DrainHole` was then removed, and its 64 tests still pass — evidence the central fix is what carries them |
| 2026-09-06 | §5.1's "optional cap" is real: `PlaneCut.Cut` takes an `addCap` flag and skips loop extraction and triangulation entirely when it is false, and the panel's long-ignored checkbox is wired to it. The test that pins it asserts the uncapped result has boundary loops and strictly fewer triangles than the capped one — volume cannot catch this, since the cap's own volume contribution is what a closed-mesh integral assumes anyway |
| 2026-09-06 | A count of what an operation changed must be a count of net change, not a sum of passes. `NormalUnificationRepair` added the per-shell consistency flips to the whole-shell re-orientation's `triangleIds.Length`, so a triangle flipped twice was counted twice and Auto Repair reported "flipped 13 triangles" on a 12-triangle mesh. It now tracks flipped ids in a set that toggles, so a double flip cancels; the reported figure is the number of triangles whose final winding differs from their original, and a test pins it at 11 for the case that produced 13 |
| 2026-09-06 | Edit panels' result lines clear on undo and redo, not only on file load. A message describing an operation Undo has just reverted is exactly as stale as one left over from a previous file, and both fall out of the same `MeshDocument.Changed` subscription, so no new call sites were needed. "Drop to Z=0" also became its own `IMeshOperation` instead of an alias that printed "Aligned to bed", and both it and Align to Bed now report the direction they actually moved the model — the old format string rendered a move up as "moved down by -1 mm". Whether Align to Bed should additionally orient a face flat-down, as print tooling usually implies, is left open |

| 2026-09-06 | Registration pins are promoted from §5.2 into v1.0, as a single peg-and-socket pair on a plane cut's mating faces (one shape, diameter and clearance; the dovetail/finger-joint/magnet-pocket catalogue stays in v1.x). §3 already named "splitting oversized models, adding registration pins" as a target-user workflow while the feature sat in v1.x, and plane-cut splitting was already v1.0 — so v1.0 shipped the half of the workflow that creates the problem and deferred the half that solves it. Reddit corroborates it verbatim: *"all i want to do is cut an stl in half and put pins in it for alignment, clicking boolean union is painfully slow, its been twenty minutes"* — note the complaint is speed, not capability, so pins should be generated directly rather than by handing two meshes to a boolean |
| 2026-09-06 | §5.1's Viewport / UX block is **built for v1.0, not deferred** — orthographic projection, standard view presets, the build plate grid with configurable printer size and its out-of-bounds warning, wireframe and x-ray display modes, the cross-section preview slider, mm/inch unit handling, drag-and-drop and a recent-files list. The alternative was moving them to §5.2, which would have made §5.1 describe a viewport nobody had written; the honest options were build them or stop claiming them, and the decision is to build. Recent files needs settings persistence, which does not exist in the codebase yet and is now a v1.0 dependency rather than a reason to skip the feature (it was skipped on exactly that ground on 2026-09-04) |
| 2026-09-06 | Reddit is reachable and is now part of the research toolkit. It refuses `curl` and `WebFetch` with 403s and serves *headless* Chromium a "Prove your humanity" challenge, but a normal headed Chromium on the dev host's real display is served normally; `scripts/browse.py` drives that browser over the DevTools protocol. No challenge is defeated or circumvented — it does not appear for a real browser session, and if it ever does, the instruction is to stop and tell the user. This closed the gap that made backlog item 5 only partially answerable, and the first pass immediately produced the verbatim evidence behind the pin promotion above |

| 2026-09-06 | §9's macOS deferral holds, but with a trigger instead of a vague "later": signing and notarisation are decided **at the first tagged release**, not whenever revenue appears. The Reddit research argued the other way — macOS is where demand is most concentrated and least served — so this is a deliberate "not yet", not an oversight. Unsigned `.app` zips keep shipping with the Gatekeeper workaround documented in `docs/usage.html` until then |
| 2026-09-06 | Settings persist as JSON in the platform config directory (`~/.config/meshwright/settings.json` on Linux and its Windows/macOS equivalents) via `System.Text.Json`, no dependency and no database. Recent files was skipped on 2026-09-04 because no persistence existed; it is now a v1.0 item, so the subsystem has to exist, and printer bed size, unit preference and window state will land in the same file. Plain and inspectable is the point — a tool whose pitch includes "no account, no cloud, no telemetry" should keep its settings somewhere the user can read and delete |
| 2026-09-06 | Registration pins (item 25) are built **before** the Viewport/UX block, jumping the queue on the strength of the evidence: they are the only feature in the whole research pass backed by a user describing the exact workflow unprompted, and they are self-contained geometry work on a plane cut that already exists. The viewport block then starts with the camera and display modes — orthographic, view presets, wireframe, x-ray — as one coherent subsystem, and the build plate, out-of-bounds warning, cross-section slider and import conveniences follow it |
| 2026-09-06 | Registration pins are **generated, never booleaned**, and the mechanism is one line long: the pin circle joins the cut cross-section as one more loop, so `CutCrossSection`'s parity nesting punches it out of the cap exactly the way it punches out a tunnel through a Menger sponge, and a cylinder wall plus an end disc is stitched onto the boundary that leaves. Nothing is intersected against anything, so the cost is proportional to the cross-section rather than to the model: on the 139,989-triangle Eiffel tower sample a pinned split measures 318 ms against 290 ms unpinned, the ~28 ms difference being almost entirely the AABB tree built for the break-out check. That is the whole point of the feature — the Reddit complaint behind its promotion was *"clicking boolean union is painfully slow, its been twenty minutes"*, a complaint about speed rather than capability, which a boolean-based implementation would reproduce verbatim. The peg and socket each get their **own** cap triangulation (the peg half's punched at the peg diameter, the socket half's at the socket diameter); before pins the two halves shared one triangulation wound opposite ways, and the unpinned path still does |
| 2026-09-06 | Automatic pin placement is the cross-section's **pole of inaccessibility** — the point furthest from any of its edges — found by subdividing whichever cell could still beat the best answer so far. Not the centroid: a cut through a level-2 Menger sponge is sixteen disjoint squares at the centre plane, and their collective centroid is in fresh air. The largest inscribed circle is defined for a cross-section of any shape and is the placement that leaves the most wall around the bore, which is the thing that matters. Inside-ness uses the same even-odd parity rule as the cap, so a pin can never be placed in a region the cap left open |
| 2026-09-06 | **Clearance is applied to the socket alone**, radially and axially: the peg comes out at exactly the requested diameter so the printed part measures what was asked for, and the hole it drops into is the one that grows. The socket is also cut one clearance *deeper* than the peg is long, so the two mating faces meet flush instead of the peg's end bottoming out first — a pin that only works in one direction is half a feature, and the workflow is a round trip (split, print, reassemble). Both figures on `RegistrationPinResult` are read back off the finished meshes rather than restated from the request, per the `DrainHoleResult.DiameterAchieved` rule above |
| 2026-09-06 | A pin that does not fit **refuses the whole cut**, not just the pin. Three ways it can fail, each with the mesh returned untouched: the cross-section cannot host the bore with a wall left around it (the message names the largest diameter that can, and a test retries that diameter and requires it to succeed); the socket would bore out through the model's own wall below the cut, which the cross-section cannot see because a part can neck in below the plane; and Keep/Discard mode or an uncapped cut, which have no mating face at all. Refusing only the pin and splitting anyway would hand back two halves that silently do not align — the §4 failure that §11's drain-hole and decimation rows exist to record |
| 2026-09-06 | Orthographic projection shares the orbit camera's Target/Distance/Yaw/Pitch and sizes its view volume as `2 · Distance · tan(fov/2)`, the height the perspective frustum already has at the target plane. Toggling the mode therefore leaves whatever the user has centred exactly the size and position it was — a display choice, not a reframing — and the mouse wheel keeps zooming, which it would not if the frustum height ignored Distance. The near plane is placed *behind* the eye (at `-FarPlane`) rather than at `NearPlane`: an orthographic volume has no eye point to be in front of, and zooming in walks the camera toward the target while the view height shrinks, so a near plane at the eye would slice the model in half on the way past. Depth stays linear across that span, so the cost is precision measured in millionths of the model. The tests measure the defining property rather than the flag — two equal segments at different depths projecting to equal screen length, with the perspective case asserted to differ as the control — because a mode enum can be right while the matrix is not |
| 2026-09-06 | View presets write **every** component of the camera's orientation and none of its framing. Yaw and Pitch are both set (the `Frame()` bug of 2026-09-05, which reset Target and Distance but not orientation, was the same defect one field over), while Target and Distance are left alone, so asking for the top view while zoomed in on a detail shows that detail from above instead of refitting the model. Top and Bottom set Pitch to exactly ±90°, which `Orbit`'s clamp deliberately never reaches: there the view direction *is* the world Z axis, so a +Z up vector gives a degenerate all-NaN look-at — the classic "top view looking down -Z with up = +Z" that draws nothing while every flag says Top. `OrbitCamera` therefore chooses the up vector as the limit of +Z as pitch approaches the pole, which keeps the on-screen orientation continuous with an orbit arriving there and gives the top view +Y up and +X right. The tests assert what each preset *shows* — the view direction and which world axes come out right and up on screen — not which enum was passed. Isometric is defined as the orientation `Frame()` restores, and `Frame()`'s default elevation moved from 30° to the true isometric asin(1/√3) = 35.26° so that Isometric and Reset View cannot differ by a few degrees no user could name |
| 2026-09-06 | Wireframe and x-ray are GL state around the existing surface pass — polygon mode Line and an unlit uniform for one, alpha blending with **depth writes off but the depth test on** for the other — and every mode restores filled polygons, no blending and depth writes before returning. The context is shared with the gizmo pass and with Avalonia; leaving polygon mode on Line is precisely how the gizmo became invisible in the depth-test row below, so a GPU test asserts the state after each mode's render, not just its pixels. The pixel tests state the property rather than "the frame changed": wireframe must cover less than half the pixels shaded does (the fill is gone), and x-ray must reveal a triangle sealed inside a cube that shaded rendering is separately asserted to hide — a control without which the x-ray half would prove nothing |
| 2026-09-06 | The light follows the camera. It was fixed in world space at (-0.5, -1, -0.3), which nothing had noticed while the only way to move was orbiting; the moment view presets existed, Front, Left and Bottom stared straight at the model's unlit side and drew it at the 0.2 ambient floor — a black silhouette on a dark background, half the new feature unusable. Found by looking at the running app on the Eiffel tower sample, not by any test: the suite had no notion of brightness. `MeshRenderer` now derives a key light from the view matrix, over the viewer's left shoulder so the shading still describes shape instead of flattening it, and a GPU test renders all seven presets and fails if any is at the ambient floor (it fails on the old world-fixed light, which is what makes it a guard rather than a decoration) |
| 2026-09-06 | Pick round-trip tolerances are measured in **pixels**, not as a fraction of the model radius. `ViewportHarnessTests`' project-then-unproject test bounded the miss at `radius * 1e-3`, which is 0.05 px on a 500 mm model and 0.22 px on a 0.5 mm one — four times stricter at the small end for no reason anyone chose, and already within 1% of failing on Linux. Moving the default camera elevation to the true isometric angle changed the pose enough for macOS's float rounding to tip it over, and `main` went red on one platform with nothing wrong. What the round trip exists to protect is picking, where the only error that matters is how far off the click lands on screen, so the bound is now half a **logical** pixel at every scale in both projections — logical, because the same world-space rounding counts double at `RenderScaling` 2, which is a property of the display rather than of the unprojection under test, and a first pass in device pixels failed on macOS for exactly that reason. Worst measured: 0.22 logical px on Linux, 0.26 on macOS |
| 2026-09-06 | Reports **embed** their screenshots in the markdown, with a caption saying what to look at, rather than leaving PNGs beside the file. `reports/M4/20260905T221759Z-ux-audit/` holds 111 images that its own report references none of, which is evidence nobody will ever see. A picture is the fastest way to show a UI change is real and the cheapest way for a reader to catch that it is not — the black-silhouette defect in the row above is obvious in one before/after pair and invisible in any prose description of it — so before/after pairs sit next to each other and the caption says what changed. `AGENTS.md`'s reporting section is updated, along with its stale claim that this dev host has no display server |
| 2026-09-06 | The build plate is drawn at its **true configured size and never scales to the model**; what adapts is the grid spacing, in two tiers. A bed that resized itself to whatever was loaded would be decoration, and the one question it exists to answer — does this part fit my printer — would become unanswerable. Major lines come from the bed alone on a 1/2/5 ladder (a 220 mm bed always reads as a 220 mm bed with a 10 mm grid), and minor lines come from the model's footprint so a part spans at least four divisions. The two corpus meshes are the reason: the 120 mm Eiffel tower needs no second tier, while the 2 × 2 × 2 mm Menger sponge on the same bed would otherwise stand in empty space between two lines 10 mm apart, and it now gets a 0.5 mm grid. Every line is still at a whole multiple of a real millimetre spacing, and the division count is capped so a pathologically small model cannot ask for an unbounded grid |
| 2026-09-06 | The bed is **centred on the world origin in X and Y** and rises from Z=0, so the build volume is X ∈ [−W/2, W/2], Y ∈ [−D/2, D/2], Z ∈ [0, H]. Z=0 as the bed was already settled (2026-09-05) and is what `AlignToBedOperation` and `DropToZ0Operation` move a model onto; centring X/Y follows from the same place, since nothing in the app translates a model to a bed corner and the sample meshes are modelled about the origin. A corner origin would put every freshly opened file outside the bed and make the warning fire on everything, which is the same as it firing on nothing |
| 2026-09-06 | The build plate renders **before the mesh, with the depth test and depth writes both on**, and is **unlit**. It is opaque world geometry, not an overlay: the model occludes it where the model is in front, and it occludes whatever has sunk below the bed — which is exactly the case the warning is shouting about. Drawing it after the mesh would need the depth test off (the gizmo pass's arrangement) and would stripe grid lines across the model. The headlight the mesh pass adopted the same day does not carry over, because a line has no surface to shade: its normal is undefined, and any value invented for one would make the grid's brightness depend on the camera for no reason a user could interpret |
| 2026-09-06 | Bed size is **configurable by preset and held in-session**, not persisted. Five common printers sit in View → Build Plate; a free-form custom size lands with `settings.json`, which recent files needs anyway (2026-09-06), rather than growing a settings subsystem as a side quest inside a viewport slice |
| 2026-09-06 | A dim UI element needs a **contrast floor, not just a taste**. The minor grid shipped at 0.35 of the plate colour, which against the viewport's (0.15, 0.15, 0.18) clear colour resolves to the background to within one 8-bit step: the Menger sponge stood on a fine grid that was in the vertex buffer, present in every unit assertion, and invisible to a person looking at the screen. Found by opening the app, not by the suite. The GPU test that now pins it measures the **contrast** of the pixels the minor tier adds over a major-only frame, because the first attempt — counting pixels that are "not the clear colour" — passed at the broken value: a one-step difference is still a difference |
| 2026-09-06 | Avalonia updates a menu item's `IsChecked` when the item is **clicked** but not when it is activated by its **HotKey**, so a handler that reads `IsChecked` makes its own shortcut a silent no-op and every radio check mark can end up describing a state the app is not in. `Ctrl+Shift+B` did nothing at all, and — pre-existing from the camera slice — `Ctrl+Shift+O` really did switch the viewport to orthographic while the menu went on showing the dot next to Perspective. Handlers now derive their state (the toggle flips its own flag; the radios read their `Tag`) and `RefreshViewMenuChecks` writes every check mark back from the live viewport, so the menu cannot disagree with what is on screen. Tests that raise `Click` directly cannot see this — the regression tests drive `KeyPressQwerty` |
| 2026-09-06 | Gizmos were being **depth-tested against the mesh**, so a gizmo inside the solid drew nothing. `MeshViewportControl` said "render active gizmo on top of the mesh" while leaving `DepthTest` enabled; the plane cut gizmo is anchored at the mesh centre and sized to a tenth of the viewport, so on any closed model it was drawn entirely inside the surface and no plane square, normal arrow or pin circle appeared at all. Found the only way it could be — by placing a pin in the running app and seeing nothing happen while the panel correctly reported "Pin placed at (0.03, 0.24, 0), Ø0.4 mm". The depth test is now disabled around the gizmo render and restored after, which is what the comment always claimed |
| 2026-09-06 | The cross-section preview is **a fragment discard in the mesh pass, not a fourth render pass**. The viewport draws build plate, mesh, then gizmo, in that order and for stated reasons; a section is not another thing to draw but a decision about which fragments of an existing pass survive, so the mesh shader and the flagged-edge overlay both discard the half-space `dot(n, p) + d > 0` and the two passes around them are deliberately untouched. The build plate is not clipped because it is the reference the model is being measured against — sawing the bed in half removes the thing the section is relative to — and the gizmo is not clipped because it is an overlay drawn with the depth test off, and clipping it would make the plane cut handle vanish at exactly the moment a user opens the model to aim it. The consequence that justifies the whole shape: the cost is independent of triangle count, nothing is re-uploaded when the plane moves, and the slider is as smooth on the 139,989-triangle tower as on a cube |
| 2026-09-06 | The section **does not cap the face it opens**, and the reason was measured rather than assumed. Stencil parity is the textbook cap, and the framebuffer Avalonia hands `OnOpenGlRender` has no stencil attachment — probed in the running app, `stencilSize=0` against `depthSize=24`, with the stencil query itself returning `InvalidOperation` because there is no attachment to describe. Capping therefore means an offscreen render target plus a blit, or re-deriving the cross-section on the CPU for every slider position; the second would give up the triangle-count independence that is the point. Plane Cut with Add Cap already produces the exact capped face, undoably, so the preview is honest about being a way of looking *into* the model rather than a picture of the cut surface, and the docs say so in those words. Logged as item 28 |
| 2026-09-06 | Back faces are shaded and tinted **only while a section is open**. Opening a model puts its inward-facing surfaces in view and they shade to the 0.2 ambient floor, so the opened part reads as a black hole; under a section the normal is flipped and the surface tinted. Doing it unconditionally would have been a regression rather than an improvement, because a back face on a closed mesh is exactly what an inverted normal looks like, and the viewport drawing it dark is a diagnostic this app exists to provide (`InvertedNormalDetector`). The GPU test renders the same inside-out box twice with only the section differing, and asserts the tint is absent in one and present in the other |
| 2026-09-06 | The section's position is a **world millimetre**, and one carried over from a different model means nothing. The slider travels the loaded model's own extent along the chosen axis, so both ends are useful at 2 mm and at 120 mm, and the readout says `Z ≤ 20 mm` rather than `Z = 20 mm` because the latter is equally true of both halves and cannot describe what Flip Side just did. Switching the section on always re-centres on whatever is loaded now: a clamp that only re-centred positions falling *outside* the model left a 40 mm cube inheriting 0 mm from the sample tetrahedron, and the first thing the feature ever showed was an empty viewport — arithmetically correct, and indistinguishable from having deleted the model. Found by opening the app after the suite was green, the same way as the black silhouette and the invisible minor grid above |

## 12. Development environment

Development host: Linux Mint 22.3 (Ubuntu 24.04 "noble" base), x86-64.

The SDK is installed **system-wide via apt**:

```bash
sudo apt install dotnet-sdk-10.0
```

This comes from Ubuntu's own `noble-updates/main` and `noble-security/main`, not a
PPA or a third-party feed, so security patches arrive automatically with normal
system updates. Currently 10.0.111.

### Runtime version policy

Target **.NET 10, which is LTS**. Do not target .NET 9: it is an STS release that
reached end of support in May 2026, and Ubuntu only offers it through a lagging
backports PPA. For a desktop tool that users install once and keep for years, staying
on LTS is worth more than early access to language features.

A prior setup on this machine used a user-local install at `~/.dotnet` via
`dotnet-install.sh`, as a workaround for a broken apt configuration (Microsoft's
Debian 13 repository enabled against an Ubuntu base). Both the broken repository and
the user-local SDK have been removed. If a future need arises for a second SDK
version side by side, `dotnet-install.sh` into `~/.dotnet` remains the way to do it
without touching the system install.

### SDK pinning

The repository should carry a `global.json` pinning the SDK feature band with
`rollForward: latestFeature`, so contributors and CI build against a known toolchain
rather than whatever happens to be on PATH. To be added with the solution skeleton in
M0.

---

## Immediate next steps

1. ~~Validate M0~~ — done; see `reports/M0/SUMMARY.md`. Outstanding: a manual smoke
   test of actual GPU pixel output on a machine with a display/GPU, since the dev
   host is headless.
2. ~~Start M2 — Repair~~ — done; see `reports/M2/SUMMARY.md`. Outstanding: no UI
   wiring yet (Repair panel, export dialog) — everything is usable
   programmatically and tested end-to-end, but not yet exposed in `MainWindow`.
3. ~~Start M3 — Edit~~ — done; see `reports/M3/`. Shipped with all 18 boolean
   tests known-failing (Manifold RUNPATH); fixed in M4 batch 0.
4. ~~Collect a real test corpus~~ — done as M4-1; superseded by item 8 below.
   Note the source changed: Thingiverse and Printables both refuse automated
   download, so the print-file half comes from the Thingi10K research dataset
   via Hugging Face instead, which also supplies per-file licence and defect
   ground truth that scraping would not have.
5. Read a week of "Meshmixer alternative" threads and turn them into a prioritised
   feature list to check against §5.1.
6. ~~Continue M4 batch 2 — gizmo/menu UI polish~~ — done; see §7 and §11. Packaging
   & CI for Linux/Windows/macOS (M4-3) and docs/release (M4-4) are still ahead, per
   the M4 kickoff plan.
7. ~~Known follow-up from M4-2: `Viewport.Gizmo` single-slot arbitration~~ — done.
8. ~~Collect a real test corpus (M4-1)~~ and ~~assert detectors against its
   ground truth (M4-6)~~ — both done; see `reports/M4/CORPUS.md`.
   ~~Outstanding: import cannot load non-manifold geometry~~ — fixed in M4-7 by
   splitting at the offending vertices; all 53 corpus files now load complete.
9. ~~Packaging & CI (M4-3)~~ — **done on all three platforms, and verified
    by real CI runs.** `.github/workflows/ci.yml` builds and tests on Linux,
    Windows and macOS; Windows and macOS build the Manifold native library
    from source in the job (only `linux-x64` ships a prebuilt binary in the
    repo), and all 520 tests pass on each. Packaging exists for all three:
    `package-linux.sh` (`.deb`), `package-windows.sh` (zip) and
    `package-macos.sh` (unsigned `.app` zip). Still open, deliberately: an
    MSI for Windows, and macOS signing/notarisation (§9 defers that until
    there is revenue). Getting the first green run took four rounds — a
    hardcoded VS 2022 generator, `sha256sum` missing on macOS, Ninja keeping
    the `lib` prefix, a normalised dylib dependency name, and finally the
    native-library probing path in §11 above.
10. ~~Export is entirely absent from the UI~~ — done: `MeshExporter` + a File >
    Export... menu item/toolbar button in `MainWindow`, round-tripped against
    the full M4-1 corpus. See §7 M4 and
    `reports/M4/20260904T213615Z-batch2-mesh-export/report.md`.
11. ~~Docs/release, first pass (M4-4)~~ — done: `docs/index.html` (GitHub
    Pages site), `samples/` (two small original meshes to try immediately),
    an expanded `README.md`. ~~Not yet done: pushing the repo to GitHub~~ —
    pushed to `github.com/bjornhenneberg/meshwright`; Pages deploys via
    `.github/workflows/static.yml`, which was uploading the repo root and so
    served a 404 until it was pointed at `docs/`. `docs/usage.html` (M4-8)
    now documents the actual workflow with screenshots from real sessions.
12. ~~**Cap multi-loop cut cross-sections**~~ — done, M4-9. Extracted caps now
    walk real edge connectivity instead of sorting vertices by angle, so cuts
    through models with holes produce correct multi-loop caps with proper
    per-loop winding and parity nesting.
13. ~~**Run long operations off the UI thread**~~ — done. Every `IMeshOperation`
    now runs through `MeshDocument.ApplyAsync` on a background thread; the UI
    shows real step-based progress for `AutoRepairPipeline` and an honest
    indeterminate spinner (Cancel disabled) for everything else, disables the
    rest of the Edit UI while an operation is in flight, and stays responsive
    — verified live by orbiting the viewport mid-drag during a 25 s Hollow.
    §6.4 gained a responsiveness target and §11 records the reasoning.
14. ~~**Boolean needs a second loaded mesh**~~ — done, M4-9. Multi-mesh
    loading now works through the `BooleanPanel`'s own "Load Secondary
    Mesh…" button, and the operation buttons stay disabled with a
    status line explaining why until a secondary mesh is loaded.
15. ~~**`HoleFillMode.Smooth` is not smooth**~~ — done. `HoleFillRepair.FillSmooth`
    now refines the patch until it has interior degrees of freedom, relaxes it,
    and displaces it by a curvature-derived spherical-cap sagitta, so a hole in
    a curved wall gets a curved cap. The cut path was deliberately left planar:
    a cut cross-section is planar by definition, and a bulging cap would be
    wrong for cutting a model into printable parts.
16. ~~**Gizmo coverage against the gizmo-first decision**~~ — done, M4-9.
    Plane Cut, Transform, Drain Hole and Hollow all have gizmos. Hollow
    shows wall thickness by dragging a handle in the viewport.
17. ~~**Reset View does nothing**~~ — done. Two independent causes fixed:
    `OrbitCamera.Frame()` reset Target/Distance but never Yaw/Pitch, so an
    orbit alone left it looking like a no-op, and the menu's `HotKey="Ctrl+0"`
    parsed to `Key.None` (Avalonia wants the digit-key name `D0`), so the
    shortcut had never been bound to anything. Both verified live: orbit away,
    click Reset View or press Ctrl+0, camera returns to the framed pose.
18. ~~**Edit panels report "Before" equal to "After"**~~ — done. Every panel
    now snapshots its statistics immediately before calling `Apply` and
    rewrites the display afterward. Plane Cut on a 2112-triangle, 4.39-volume
    mesh now correctly reports "Before: 2112 / 4.39" rather than repeating
    the after figures — verified live.

19. ~~**Drain Holes is destructive and reports success**~~ — done. It now
    drills: a local patch is grown, the requested circle projected onto it, the
    patch outline refined and lofted to the rim. A Ø0.5 mm hole on the coarse
    cube costs 0.195 mm² against πr² = 0.196 (it took 4.0 mm², a whole face,
    before), takes the vertex count 8 → 100, leaves exactly one boundary loop
    measuring Ø0.5 mm and does not move the bounding box. `DiameterAchieved` is
    measured by walking that loop; `DepthDrilled` is deleted. Countersink is
    implemented as a real 45° chamfer, and the gizmo places holes at the
    panel's diameter rather than a hard-coded 2 mm.
20. ~~**Plane Cut's "Add Cap" checkbox is inert**~~ — done. `PlaneCut.Cut`
    takes an `addCap` flag and skips cap generation when it is false. The test
    asserts the uncapped result has boundary loops and strictly fewer triangles
    than the capped one.
21. ~~**Panel reporting defects found by the same audit**~~ — done, all six.
    Transform reports full bounding-box size plus min/max corners; Hollow says
    nothing about a gizmo until one is touched; the flip count is net, not a
    sum of passes (11, not 13, on a 12-triangle mesh); Decimate's unit label
    follows the mode; result lines clear on load, undo and redo alike; and
    "Drop to Z=0" is its own operation reporting its own name and the direction
    it actually moved. Whether Align to Bed should also orient a face flat-down
    is left open.
22. **Decimation introduces the invalid geometry it claims to have avoided** —
    734 triangles and 67 self-intersections from a clean mesh, while the summary
    says further collapses "would have created invalid geometry". The local
    validity test each collapse passes has to be checked against whole-mesh
    invariants afterwards.
23. **Build §5.1's Viewport / UX block.** **Scope decided 2026-09-06: these
    ship in v1.0 rather than moving to §5.2.** A multi-batch build, one slice per
    branch.
    - ~~**Camera and display modes**~~ — done 2026-09-06. Orthographic
      projection, seven view presets (`Ctrl+1`–`Ctrl+7`), and the wireframe and
      x-ray display modes, all in the View menu and all reachable by shortcut.
      See the §11 rows for that date.
    - ~~**Build plate grid with configurable printer size, and the out-of-bounds
      warning**~~ — done 2026-09-06. A grid on the Z=0 plane sized to one of five
      printer presets in View → Build Plate (`Ctrl+Shift+B` hides it), and a
      status-bar warning naming every side the model overhangs and by how much,
      with the bed outline turning amber to match. See the §11 rows for that date.
    - ~~**Cross-section preview slider**~~ — done 2026-09-06.
      `View → Cross-Section` (`Ctrl+Shift+C`) opens a bar under the viewport with
      an axis picker, a millimetre slider over the model's own extent, and Flip
      Side. Implemented as a fragment discard in the mesh pass and its
      flagged-edge overlay, so it costs the same at any triangle count and edits
      nothing; the build plate and the gizmo are deliberately not clipped. **It
      does not cap the opened face** — see §11 and item 28. Report in
      `reports/M4/20260906T233000Z-viewport-cross-section/report.md`.
    - **Import conveniences**: mm/inch unit detection and scaling,
      drag-and-drop, recent files. Recent files needs settings persistence,
      which nothing in the codebase provides yet — decided 2026-09-06 as JSON in
      the platform config directory.
25. ~~**Registration pins on cut faces**~~ — done. A peg-and-socket pair on a
    plane cut's mating faces, generated directly: the pin circle joins the cut
    cross-section as one more loop, so the existing parity-nested capping code
    punches it out of the cap, and a cylinder wall plus an end disc is stitched
    onto the boundary that leaves. No boolean anywhere — on the 139,989-triangle
    Eiffel tower sample a pinned split costs 318 ms against 290 ms unpinned.
    Placement is automatic at the cross-section's pole of inaccessibility, or
    wherever the user clicks on the plane gizmo; clearance goes on the socket
    alone, radially and axially. A pin that does not fit refuses the whole cut
    with the mesh untouched. See §11.
24. **Hole filling and hole detection disagree about import seams** — see §11,
    2026-09-06. `BoundaryHoleDetector` excludes seams by position;
    `HoleFillRepair` finds loops by vertex index and does not.

26. **A split leaves both halves in one mesh with coincident cut faces.**
    `PlaneCutSplitOperation` appends the negative half into the same `DMesh3` as
    the positive one, and the two caps occupy exactly the same plane and area, so
    every cap triangle overlaps its opposite number. Splitting the clean Menger
    sponge sample in the running app reports **1,808 issues** where the two halves
    measured separately have none at all — each is closed, single-shell and
    issue-free (verified 2026-09-06 while building item 25; pins add geometry to
    both caps and take it to 2,072, but they are not the cause and the pinned
    halves are individually just as clean). Either the halves should be separated
    before being merged, or a split should produce two documents rather than one
    mesh. Found while verifying pins, not caused by them.

28. **The cross-section preview does not cap the face it opens.** A solid part
    opened by the slider reads as an open shell, because the preview hides
    fragments rather than constructing the cut surface. The textbook fix is
    stencil parity, and Avalonia's framebuffer has no stencil attachment
    (measured, 2026-09-06: `stencilSize=0`, `depthSize=24`), so capping means
    either an offscreen render target of our own plus a blit, or re-deriving the
    cross-section on the CPU per slider position — and the second gives up the
    triangle-count independence that is the point of the preview. Plane Cut with
    Add Cap already produces the exact capped face, so this is a refinement, not
    a gap in capability. Related: a section plane that is not axis-aligned, and a
    draggable in-viewport section gizmo, which the gizmo-first direction argues
    for.

27. **A refused operation still counts as a change.** `MeshDocument.ApplyAsync`
    calls `RefreshReport` unconditionally, so an operation that returns
    `Changed: false` — every refusal path: a pin that will not fit, a drain hole
    too big for the surface, a plane through no geometry — still raises `Changed`,
    still pushes an undo entry, and still makes `MainWindow.RefreshFromDocument`
    rebuild every gizmo and clear the viewport slot. In practice a user who asks
    for a pin that does not fit loses the pin they had positioned and has to place
    it again to try a smaller one. Small and sharp.
