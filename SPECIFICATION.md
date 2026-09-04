# Meshwright — Specification

**Status:** Draft v0.2 — living document, updated as milestones land
**Working name:** Meshwright (placeholder — rename freely)
**Progress:** M0 (Skeleton), M1 (Inspect), M2 (Repair), and M3 (Edit) complete
and verified; see `reports/M0/SUMMARY.md`, `reports/M1/SUMMARY.md`,
`reports/M2/SUMMARY.md`, and `reports/M3/`. M4 (Polish and release) is
underway — batches M4-0 (Manifold RPATH/interop fix) and M4-2 (gizmo wiring
+ menu/undo-redo UI) are complete, 220/220 tests + 8/8 GPU passing; see the
M4 entry in §11 and §7.

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

Remaining M4 batches (packaging & CI — M4-3; docs/release — M4-4) are not yet
started — see `reports/M4/` as they land. 389 unit tests + 8 GPU tests passing.

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
8. ~~Collect a real test corpus (M4-1)~~ — done; see `reports/M4/CORPUS.md`.
   Outstanding: the corpus only *smoke-tests* that nothing throws. Thingi10K
   publishes per-file defect ground truth (boundary edges, self-intersections,
   manifoldness, orientation, component count) which the manifest already
   records — asserting Meshwright's detector output against it would turn the
   smoke test into a true regression corpus, and is the single highest-value
   follow-up here.
9. Packaging & CI (M4-3) and docs/release (M4-4) remain. Note for CI: the corpus
   is fetched, not committed, so a CI job must run `scripts/fetch-corpus.sh`
   (and should cache it) to get corpus coverage; without it those tests pass
   trivially rather than failing, by design.
10. **Export is entirely absent from the UI.** §5.1 requires STL and OBJ export
    in v1.0 and M2 delivered both writers, but `Meshwright.App` contains no
    reference to `StlWriter` or `ObjWriter`, no save-file picker and no Export
    menu item — a user can open and repair a mesh but cannot get it back out.
    This is the largest remaining v1.0 functional gap and was flagged as a known
    M2 deferral ("no UI wiring") that no later milestone picked up. It should be
    a batch of its own before packaging (M4-3), since shipping an installer for
    a tool that cannot save would be worse than shipping later.
