# M2 batch 0 — IMeshOperation contract, undo stack, MeshDocument wiring

**Verdict: verified**

## What this batch is

Foundational, sequential prerequisite for the six repair-operation batches that
follow: the `IMeshOperation` abstraction from §6.3, a snapshot-based undo
stack, and `MeshDocument` wiring (`Apply`/`Undo`/`Redo`). Done directly rather
than dispatched, since every later batch builds against this contract and it
needed to be settled first.

## What was built

- `src/Meshwright.Core/Operations/OperationResult.cs` — `record OperationResult(bool Changed, string Summary)`,
  the plain-language-report unit every operation returns (§5.1's "3 holes ...
  14 flipped faces" style).
- `src/Meshwright.Core/Operations/IMeshOperation.cs` — `Name`, `Preview(DMesh3)`,
  `Apply(DMesh3)`. Parameters live on the concrete operation instance per §6.3.
- `src/Meshwright.Core/Operations/MeshOperationBase.cs` — abstract base
  implementing `Preview` as `Execute` against a throwaway `DMesh3` clone, so
  concrete operations only implement the mutating path once.
- `src/Meshwright.Core/Operations/UndoStack.cs` — clones the mesh before every
  `Apply` rather than requiring per-operation inverse logic. Simpler and safer
  for whole-mesh repair ops than element-wise undo (§4 "never silently destroy
  the model").
- `src/Meshwright.Core/MeshDocument.cs` — added `Apply(IMeshOperation)`,
  `Undo()`, `Redo()`, `CanUndo`, `CanRedo`. `Load` now clears undo history.
  `Report` is recomputed after every apply/undo/redo so the diagnostics panel
  stays in sync with repairs, matching the existing M1 diagnostics contract.
- `tests/Meshwright.Tests/Operations/MeshDocumentApplyUndoTests.cs` — exercises
  apply/undo/redo/preview through a deterministic test-double operation
  (`RemoveOneTriangleOperation`), since the real repair operations are tested
  independently by the batches that implement them.

## Design decisions for downstream batches

- Repair **algorithms** belong in `Meshwright.Geometry/Repair/` as plain,
  UI/undo-agnostic code operating on `DMesh3` — mirrors how M1's detectors
  live in `Meshwright.Geometry/Diagnostics/` with no Core dependency.
- Each operation gets a thin `Meshwright.Core/Operations/<Name>Operation.cs`
  wrapper (typically `: MeshOperationBase`) that calls into the Geometry
  algorithm and reports an `OperationResult`. This keeps the Geometry/Core
  boundary from AGENTS.md intact: Geometry has no Core reference, Core has no
  UI reference.
- Undo is snapshot-based and lives entirely in `MeshDocument`/`UndoStack` —
  individual operations never need to implement their own undo.

## Verification

- `dotnet build` — 0 errors. [build.log](build.log)
- `dotnet test` (full suite) — 79/79 passing (73 unit + 6 GPU), up from 75/75
  at end of M1 (4 new tests added by this batch). [test.log](test.log)
- No UI-facing change in this batch — no screenshots needed.
