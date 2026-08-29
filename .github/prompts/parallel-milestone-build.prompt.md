---
description: "Orchestrate Meshwright development milestone-by-milestone, dispatching parallel subagents for independent tasks within each milestone"
name: "Parallel Milestone Build"
agent: "agent"
---
You are the lead agent building Meshwright — see [SPECIFICATION.md](../../SPECIFICATION.md)
and [AGENTS.md](../../AGENTS.md). Work milestone-by-milestone, in dependency
order, using parallel subagents for independent work within a milestone. Do
not skip ahead to later milestones or features from §5.2/§5.3 — scope
discipline per §2/§9 is binding.

## Milestone order (do not reorder)

1. **M0 — Skeleton**: solution structure, `global.json`, Avalonia window,
   Silk.NET OpenGL viewport control embedded in Avalonia, load and render one
   STL with orbit/pan/zoom. This is the highest-risk integration point — prove
   it before building anything else on top.
2. **M1 — Inspect**: mesh statistics + full error detection (non-manifold,
   boundary holes, self-intersections, inverted normals, degenerate triangles,
   duplicate vertices, disconnected shells) with visual highlighting and a
   plain-language report.
3. **M2 — Repair**: Auto Repair pipeline + individually runnable repair ops,
   undo stack, export.
4. **M3 — Edit**: plane cut, booleans, transforms, hollow, drain holes,
   decimation.
5. **M4 — Polish/release**: packaging, docs, sample corpus, crash-free pass.

## How to parallelize within a milestone

Only parallelize subagent work when tasks touch **disjoint modules or files**
and have no data/type dependency on each other's output in this pass.
Concretely:

- **M0**: solution/project scaffolding (`.sln`, `.csproj` files, `global.json`)
  can run in parallel with researching the Avalonia+Silk.NET interop approach —
  but the actual viewport control implementation depends on the scaffolding,
  so sequence those two.
- **M1**: each mesh error detector (non-manifold edges, holes,
  self-intersections, normal consistency, degenerate triangles, duplicate
  vertices, disconnected shells) is an independent, separately testable unit
  against `DMesh3` — dispatch one subagent per detector in parallel, each
  producing its implementation + xUnit tests + a small fixture mesh. Merge
  into the report/highlight UI afterward (sequential, depends on all
  detectors).
- **M2**: each individual repair operation (hole fill variants, normal
  unification, degenerate/duplicate removal, small-shell removal,
  self-intersection resolution, voxel remesh fallback) is independent —
  parallelize per operation, each with its own tests. Auto Repair pipeline
  composition and the undo stack are sequential follow-ups that depend on all
  operations existing.
- **M3**: plane cut, transforms, hollow, drain holes, and decimation are
  largely independent of each other; booleans depend on the Manifold interop
  layer being done first (sequence that one ahead of the rest).
- Never parallelize two subagents writing to the same file, the same vendored
  g3Sharp type, or anything touching the undo/command pipeline at the same
  time.

## Execution pattern

For the current milestone, hand its batch breakdown above to the
`parallel-orchestrator` subagent: give it the list of independent tasks per
batch, the module boundaries each touches, and the `IMeshOperation`/detector
contracts from SPECIFICATION.md §6.3. It will dispatch `scoped-worker`
subagents per batch and integrate the results. Review its final report before
moving to the next batch or milestone — don't chain milestones automatically
without checking in.

Begin with M0, or with whichever milestone is next incomplete based on current
repo state.
