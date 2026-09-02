# M2 (Repair) — Milestone Summary

**Status: Complete.** All 3 batches verified.

## Goal

Per [SPECIFICATION.md](../../SPECIFICATION.md) §7 M2:

> Auto Repair plus the individual repair operations. Undo stack. Export.

Concretely, per §5.1: a one-click Auto Repair pipeline that reports what it
did; six individually-runnable repair operations (hole filling, normal
unification, degenerate-triangle/duplicate-vertex removal, small-shell
removal, self-intersection resolution, voxel remesh/solidify fallback); full
undo; and STL/OBJ export.

## What was built

1. **Batch 0 — `IMeshOperation` contract, undo stack, `MeshDocument` wiring.**
   Foundational and sequential, done directly rather than dispatched (every
   later batch depends on it): `IMeshOperation` (`Name`/`Preview`/`Apply` per
   §6.3), `MeshOperationBase` (derives `Preview` from `Apply` via a throwaway
   mesh clone), and a snapshot-based `UndoStack` so individual operations
   never implement their own inverse. `MeshDocument` gained
   `Apply`/`Undo`/`Redo`/`CanUndo`/`CanRedo`, recomputing the M1 diagnostics
   report after each.
   Report: [20260902T183819Z-batch0-operation-contract/report.md](20260902T183819Z-batch0-operation-contract/report.md)

2. **Batch 1 — the six repair operations, plus STL/OBJ export.** Seven
   independently-scoped units dispatched in parallel, each a pure algorithm
   in `Meshwright.Geometry/Repair/` plus a thin `Meshwright.Core/Operations/`
   wrapper: degenerate-triangle/duplicate-vertex removal, normal unification,
   small-shell removal, hole filling (flat/planar/smooth), self-intersection
   resolution, and voxel remesh/solidify (which required vendoring
   `MarchingCubes` and `MeshSignedDistanceGrid` plus their real dependencies
   from upstream geometry3Sharp, at the same pinned commit as the rest of the
   vendor tree — see `VENDOR.md`). Plus binary `StlWriter` (round-trip-verified
   through the existing `StlReader`) and ASCII `ObjWriter`.
   Report: [20260902T190146Z-batch1-repair-operations/report.md](20260902T190146Z-batch1-repair-operations/report.md)

3. **Batch 2 — Auto Repair pipeline.** Sequential follow-up, done directly:
   `AutoRepairPipeline` composes five of the six operations (degenerate/
   duplicate cleanup → small-shell removal → self-intersection resolution →
   hole filling → normal unification) into one undoable `IMeshOperation`.
   Voxel remesh is deliberately excluded from the default sequence — it's
   the "sledgehammer fallback for hopeless meshes" (§5.1/§9), not a step
   every repair should pay the detail loss for — and stays available on its
   own. End-to-end verified against the real `BrokenSample.stl` fixture from
   M1: the default pipeline actually clears the hole/stray-shell/flipped-face
   issues, and undo brings them back.
   Report: [20260902T190426Z-batch2-auto-repair-pipeline/report.md](20260902T190426Z-batch2-auto-repair-pipeline/report.md)

## Test results

Final authoritative count, from the batch 2 report: **119 total tests, 0
failed, 0 skipped** (`Meshwright.Tests.Gpu`: 6/6 passed; `Meshwright.Tests`:
113/113 passed). `dotnet build` (clean rebuild): 0 errors, 0 warnings on
every changed project. Up from 75/75 at the end of M1.

## Known gaps / deferred issues

- **No UI wiring.** No Repair panel (buttons for Auto Repair, each individual
  operation, undo/redo) and no export dialog were built. This milestone's
  task scope named pipeline/operations/undo/export without a UI requirement
  (unlike M1, which explicitly needed a diagnostics panel), so it was treated
  as out of scope for this pass rather than assumed. Every piece is fully
  usable programmatically and tested end-to-end
  (`MeshDocument.Apply(new AutoRepairPipeline())`, `Apply`/`Undo`/`Redo` for
  any of the six individual operations, `StlWriter`/`ObjWriter`). Flagged
  explicitly, the same way M0 flagged its unverified GPU smoke test — a real
  next step for M3/M4's UI work, not a silent omission.
- **`HoleFillRepair`'s ear-clipping** doesn't check candidate diagonals
  against unrelated, non-adjacent mesh topology — a narrow, documented
  limitation not expected to matter for real-world hole shapes.
- **`NormalUnificationRepair`'s** "is this shell's volume meaningfully
  nonzero" check uses a fixed absolute threshold (`1e-9`), not scale-relative
  to the mesh.
- **`VoxelRemeshRepair`'s** default resolution (128 cells along the longest
  axis) is correctness-verified only at low test resolutions; not benchmarked
  against §6.4's 500k-triangle/5s auto-repair target.
- **`SelfIntersectionRepair`** only removes offending triangles — it doesn't
  re-triangulate a patch itself. The default `AutoRepairPipeline` sequences
  hole filling right after it specifically to close what it leaves behind;
  calling it standalone can leave holes.
- **Build-hook JSON still invalid** (`.github/hooks/build-on-edit.json`,
  entirely wrapped in `//` comments) — carried forward from M1, unrelated to
  M2 scope, still unresolved.
- **No real-world test corpus yet.** M2's repair operations were verified
  against synthetic fixtures and the one `BrokenSample.stl` mesh from M1.
  SPECIFICATION.md's "Immediate next steps" already calls for 20-30 real
  broken meshes from Thingiverse/Printables/scanner output as regression
  fixtures — this matters more now that M2 exists to repair them, and remains
  outstanding.

## Next milestone

**M3 — Edit** (plane cut, booleans, transforms, hollow, drain holes,
decimation), per §7.
