# M2 batch 2 — Auto Repair pipeline

**Verdict: verified**

## What this batch is

Sequential follow-up, done directly (like batch 0) rather than dispatched,
since it composes all six repair operations from batch 1 into shared state —
exactly the kind of single-piece-of-shared-state work the milestone-lead
convention says not to parallelize.

## What was built

- `src/Meshwright.Core/Operations/AutoRepairPipeline.cs` — implements
  `IMeshOperation` itself (via `MeshOperationBase`), so `MeshDocument.Apply`
  treats a full Auto Repair run as a single undoable step. Default sequence:
  1. Remove degenerate triangles + duplicate vertices
  2. Remove small disconnected shells
  3. Resolve self-intersections
  4. Fill holes
  5. Unify normals

  Ordering rationale (documented in the type's XML doc): cleanup/small-shell
  removal first so later steps see a tidier mesh; self-intersection
  resolution before hole filling, since it can leave holes for that step to
  close; normal unification last, since earlier steps change which
  edges/shells exist.

  **Voxel remesh is deliberately excluded** from the default sequence — per
  §5.1/§9 it's the "sledgehammer fallback for hopeless meshes," not a step
  every repair should pay the detail loss for. It stays available as its own
  individually-runnable operation (`VoxelRemeshOperation`, from batch 1) for
  meshes the rest of the pipeline can't fix. This is a deliberate scope
  decision, not an oversight — flagging it explicitly since it's the one
  place this batch departs from "wire up all six operations" literally.

  A constructor overload accepts a custom step list, used by this batch's
  own tests to isolate sequencing/aggregation logic from the real algorithms.

- `tests/Meshwright.Tests/Operations/AutoRepairPipelineTests.cs`:
  - Sequencing/aggregation logic tested against fake operations (order
    preserved, only changed steps' summaries joined, "No repairs needed."
    when nothing changed).
  - Preview-doesn't-mutate check.
  - **A real end-to-end test**: loads `tests/Meshwright.Tests/Fixtures/BrokenSample.stl`
    (the same fixture M1's acceptance test used — a hole, a stray shell, and
    a flipped face) through `MeshDocument`, runs the real default
    `AutoRepairPipeline` via `document.Apply(...)`, and asserts the
    `BoundaryHole`, `InvertedNormal`, and `DisconnectedShell` diagnostic
    categories are all gone afterward — then undoes and confirms the
    `BoundaryHole` issue reappears. This is the one test in M2 that proves
    the six operations actually compose into a working repair, not just that
    each passes in isolation.

## "Individually runnable" and undo, for the record

No new Core-layer plumbing was needed for "each operation independently
runnable" beyond what batch 0 already built:
`document.Apply(new FillHolesOperation())` (etc.) already works for any of
the six operations, since `MeshDocument.Apply` takes any `IMeshOperation`.
Undo/redo for both individual operations and the full pipeline is the same
snapshot-based `UndoStack` from batch 0 — verified again here via the
end-to-end test's undo assertion.

## Verification

- `dotnet build` — 0 errors, 0 warnings. [build.log](build.log)
- Full `dotnet test` — **119/119 passing** (113 unit + 6 GPU), up from 115/115
  after batch 1 (4 new tests). [test.log](test.log)
- No UI-facing change in this batch — no screenshots needed.

## Known gap carried into the M2 summary

No UI wiring (a Repair panel with buttons for Auto Repair / each individual
operation / undo / redo, and an export dialog) was built in M2. The original
task scope for this milestone named "Auto Repair pipeline, individually-runnable
repair operations, an undo stack, and STL/OBJ export" without a UI
requirement (unlike M1, which explicitly needed a diagnostics panel), so this
was treated as out of scope for this pass rather than assumed. Every piece is
fully usable programmatically (`MeshDocument.Apply`/`Undo`/`Redo`,
`StlWriter`/`ObjWriter`) and tested end-to-end. Flagged here the same way M0
flagged its unverified GPU smoke test — a known, explicit gap, not a silent
omission.
