# Meshwright — Specification

**Status:** Draft v0.2 — living document, updated as milestones land
**Working name:** Meshwright (placeholder — rename freely)
**Progress:** M0 (Skeleton), M1 (Inspect), M2 (Repair), and M3 (Edit) complete
and verified; see `reports/M0/SUMMARY.md`, `reports/M1/SUMMARY.md`,
`reports/M2/SUMMARY.md`, and `reports/M3/`. M4 (Polish and release) is
underway — batches M4-0 (Manifold RPATH/interop fix), M4-1 (real-world test
corpus), M4-2 (gizmo wiring + menu/undo-redo UI), M4-6 (corpus ground
truth), M4-7 (non-manifold import fix), M4-3 (Linux CI + packaging), M4-4
(docs/release), M4-8 (make the app do what it says) and M4-9 (correctness
gaps closed) are complete, 495/495 unit tests passing; see the M4 entry in
§11 and §7. The 8 GPU tests were last run green in M4-8 and were not re-run
in M4-9.

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
  into separate parts
- Boolean union / difference / intersection between loaded meshes
- Transform: move, rotate, scale, mirror, numeric entry, align to bed, drop to Z=0
- Hollow: offset shell to a given wall thickness
- Drain holes: place holes on the surface, configurable diameter and countersink

**Simplify**
- Quadric edge-collapse decimation, targeting triangle count or percentage, with a
  live before/after triangle count

**Viewport / UX**
- Orbit / pan / zoom, orthographic and perspective, standard view presets
- Shaded, wireframe, x-ray and error-highlight display modes
- Build plate grid with configurable printer size, out-of-bounds warning
- Undo/redo across all operations
- Cross-section preview slider

### 5.2 v1.x — Follow-up

- 3MF import/export (with colour and multi-object support) and PLY import
- Auto-orientation for minimum support / best strength
- Registration pins and dowel/puzzle joints on cut faces
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

Batch M4-3 (Linux CI + packaging) complete, scoped to Linux only by explicit
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

Batch M4-9 (correctness gaps closed) complete: three of the five gaps handed
off after M4-8 — the multi-loop cut cap (item 12), booleans between loaded
meshes (item 14), and gizmo coverage for Hollow (item 16). 453 → 495 unit
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
The last two are now items 17 and 18. None would have surfaced from the suite.

Remaining M4 work: Windows/macOS CI + packaging (still under M4-3, not
started); the outstanding correctness and UX gaps listed under "Immediate
next steps" below; and revisiting "Meshwright" as a name before any paid
release per the finding below.

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
9. ~~Packaging & CI (M4-3)~~ — Linux done: `.github/workflows/ci.yml` fetches
   and caches the corpus via `scripts/fetch-corpus.sh` as planned, and
   `scripts/package-linux.sh` builds a self-contained `.deb`. Windows/macOS
   CI + packaging still remain, as does docs/release (M4-4).
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
13. **Run long operations off the UI thread** — every operation is
    synchronous, so Auto Repair or decimation on a six-figure-triangle mesh
    freezes the window for tens of seconds with no progress indication. A
    large part of why the app felt like it "wasn't doing anything" even
    where it worked. Relates to §6.4's performance targets, which are about
    throughput and say nothing about responsiveness.
14. ~~**Boolean needs a second loaded mesh**~~ — done, M4-9. Multi-mesh
    loading now works through the `BooleanPanel`'s own "Load Secondary
    Mesh…" button, and the operation buttons stay disabled with a
    status line explaining why until a secondary mesh is loaded.
15. **`HoleFillMode.Smooth` is not smooth** — the plane-cut rewrite routes
    capping through `CutCrossSection`, where `Smooth` and `Planar` are the
    same path and `Flat` is the only distinct one. This is defensible for a
    *cut* — a cut cross-section is planar by definition — so the remaining
    gap is in `HoleFillRepair.FillSmooth`, which adds a single centroid vertex
    relaxed onto the average of three fixed boundary corners and is barely
    distinguishable from a planar fill.
16. ~~**Gizmo coverage against the gizmo-first decision**~~ — done, M4-9.
    Plane Cut, Transform, Drain Hole and Hollow all have gizmos. Hollow
    shows wall thickness by dragging a handle in the viewport.
17. **Reset View does nothing.** The toolbar button and its `Ctrl+0` shortcut
    both leave the camera untouched — verified on a fresh load with no
    operation applied, the screenshots pixel-identical before and after. §11's
    2026-09-05 row added it precisely because orbiting or zooming could put
    the mesh off screen with no way back short of reopening the file, so the
    escape hatch that row describes does not exist.
18. **Edit panels report "Before" equal to "After".** Every panel's before/after
    summary re-reads its "before" figures from the already-mutated document,
    so the two always match: Plane Cut showed "Before: 1309 triangles / 2.195"
    after cutting a 2112-triangle, 4.39-volume mesh, and Boolean showed
    "Before: 36 / 1875" after a 12-triangle input. No panel can show that an
    operation removed anything, which is the same dishonest-reporting failure
    as the Auto Repair row already in §11.
