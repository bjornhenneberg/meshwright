# Meshwright — Specification

**Status:** Draft v0.1
**Working name:** Meshwright (placeholder — rename freely)

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
- Import: STL (binary + ASCII), OBJ, 3MF, PLY
- Export: STL (binary), 3MF, OBJ
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
| Language | C# / .NET 9 | Requested; strong desktop story, good perf with `Span<T>` and SIMD |
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

**Plan:** fork g3Sharp as the base mesh representation and repair toolkit, add
Manifold via interop for booleans, and write the printing-specific operations
(drain holes, cut-and-cap, shell removal heuristics, diagnostics) in-house. Avoid
any GPL dependency so the licence in §8 stays possible.

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

**M0 — Skeleton**
Solution structure, Avalonia window, Silk.NET viewport rendering a loaded STL with
orbit/pan/zoom. Proves the hardest integration risk first.

**M1 — Inspect**
Full mesh analysis and error highlighting. Shippable alone as a free "why won't this
print?" tool — and a cheap way to find the first users.

**M2 — Repair**
Auto Repair plus the individual repair operations. Undo stack. Export.

**M3 — Edit**
Plane cut, booleans, transforms, hollow, drain holes, decimation.

**M4 — Polish and release**
Packaging for three platforms, docs, website, sample files, crash-free on the test
corpus. Public 1.0.

**M5+**
v1.x features, then the resin module.

## 8. Licensing and funding

**Model:** open source core, paid convenience — the Krita/Aseprite/Ultimaker pattern.

- Source is public under a permissive licence (MPL-2.0 or Apache-2.0). Anyone can
  build it themselves.
- Prebuilt, signed, auto-updating binaries are sold: **one-time ~€30, includes all
  1.x updates.** Not a subscription.
- Free prebuilt binaries for the Inspect-only feature set, as the funnel.
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
| g3Sharp is unmaintained | It is permissively licensed — fork it and own the fork |
| No users notice the release | Build in public from M1; the "Meshmixer is dead" story is the marketing hook |
| macOS notarisation cost/hassle | Linux + Windows first; macOS once there is revenue |

## 10. Open questions

- Name and domain availability — "Meshwright" is a placeholder.
- Fork g3Sharp wholesale, or vendor only the parts needed?
- Is 3MF import worth full support in v1, or is STL + OBJ enough to start?
- Should the Inspect tier be a genuinely separate free download, or the same binary
  with editing disabled?

---

## Immediate next steps

1. Install the .NET 9 SDK (not currently present on this machine).
2. Validate M0: Avalonia window with a Silk.NET OpenGL control rendering a triangle,
   then an STL. If this is painful, reconsider the UI stack now rather than later.
3. Collect a test corpus: 20-30 real broken meshes from Thingiverse/Printables plus
   scanner output, kept as regression fixtures.
4. Read a week of "Meshmixer alternative" threads and turn them into a prioritised
   feature list to check against §5.1.
