# Meshwright — Agent Instructions

Full product spec: [SPECIFICATION.md](SPECIFICATION.md). Read it before doing
non-trivial work — it defines scope, architecture, and milestones. This file
only covers things the spec doesn't state as agent-actionable rules.

## Scope discipline

This is not a CAD tool, sculpting suite, or slicer (§2). Before adding a
feature, check it against §5.1 (v1.0 scope) and the non-goals in §2/§9. Do not
pull forward v1.x (§5.2) or v2.0 (§5.3) features into v1.0 work without being
asked.

## Milestone order

Build in the order given in §7 (M0 → M1 → M2 → M3 → M4). Don't start a later
milestone's features while an earlier one is incomplete.

## Architecture (§6.3) — do not blur these boundaries

- `Meshwright.Geometry` — mesh data structures, repair, booleans, decimation.
  No UI, no I/O references.
- `Meshwright.IO` — STL/OBJ (3MF/PLY deferred to v1.x) readers/writers.
- `Meshwright.Core` — document model, `IMeshOperation` pipeline, undo stack,
  settings. UI-agnostic.
- `Meshwright.Rendering` — Silk.NET renderer, camera, gizmos, picking, shaders.
- `Meshwright.App` — Avalonia views and view models.
- `Meshwright.Cli` — headless batch entry point (v1.x, not v1.0).
- `Meshwright.Tests` — xUnit + golden-file regression tests.

Every user-facing action is an `IMeshOperation` with `Preview()` and `Apply()`
(§6.3) — implement new operations through that abstraction, not as one-off
code paths, so undo/batch/CLI keep working for free.

## Geometry library policy (§6.2)

- Vendor selected g3Sharp types into `Meshwright.Geometry/Vendor/g3/` — do not
  add g3Sharp as a package reference or fork the whole project. Keep the Boost
  licence header on every vendored file and record provenance in `VENDOR.md`.
  Only vendor what's listed in §6.2 (`DMesh3`, `MeshNormals`,
  `MeshConnectedComponents`, `MeshBoundaryLoops`, `Reducer`, `Remesher`,
  `DMeshAABBTree3`, `MarchingCubes`, `MeshSignedDistanceGrid`) plus anything
  those types depend on.
- Booleans go through **Manifold** (MIT) via a thin C interop layer — never
  g3Sharp's voxel booleans (lossy).
- No GPL dependencies (CGAL, VCGlib/MeshLab, etc.) — this would break the
  licensing model in §8.

## Runtime / build

- Target **.NET 10 (LTS)** only. Do not target .NET 9.
- A `global.json` pinning the SDK feature band (`rollForward: latestFeature`)
  should exist at the repo root once the solution is scaffolded (M0) — if it's
  missing while doing dotnet work, add it rather than relying on whatever SDK
  happens to be on PATH.
- Dev host is Linux Mint 22.3 / Ubuntu 24.04 base, SDK installed system-wide
  via apt (`dotnet-sdk-10.0`), not `dotnet-install.sh` or a PPA.

## Testing

Every geometry/repair operation needs an xUnit test. Prefer golden-file
regression tests against known-bad meshes over hand-asserting individual
triangle counts, per §6.1/§7 M1.

## Evidence and reporting

Agentic work (via `parallel-orchestrator` / `verifier` / `milestone-lead`)
writes durable, human-reviewable evidence instead of just a chat summary:

- `reports/<milestone>/<UTC-timestamp>-<batch-name>/report.md` — one per
  verified batch: pass/fail per check, build/test logs, and screenshots or
  short frame sequences for anything UI-facing (captured via
  `Avalonia.Headless`, since there's no display server on CI/this dev host).
- `reports/<milestone>/SUMMARY.md` — written once a whole milestone's batches
  are verified; links every batch report plus an overall recap.
- `reports/build-hook/` — transient per-edit build/test logs from the
  auto-build hook, gitignored, not evidence to keep long-term.

Don't mark a batch or milestone done without a corresponding report under
`reports/`.

## Licensing note

Licence choice (MPL-2.0 vs Apache-2.0) is still an open question (§10) — do
not add a `LICENSE` file or SPDX headers implying a final decision has been
made until this is resolved.
