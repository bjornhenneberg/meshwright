# Next session brief — Meshwright

Paste this as the opening prompt of the next Claude Code session. It is a
pointer into `SPECIFICATION.md` rather than a second source of truth — when the
two disagree, the spec wins, and this file should be rewritten or deleted once
its backlog is done.

---

You are running as **Opus, in a dispatcher role**, on the Meshwright repo
(`/home/bjorn/Code/meshwright`) — a cross-platform desktop tool for repairing
meshes for 3D printing (C# / .NET 10 / Avalonia / Silk.NET, geometry on a
vendored g3Sharp).

**Read `SPECIFICATION.md` first.** It is the source of truth and a living
document: §5.1 defines v1.0 scope, §7 narrates each milestone batch, §11 is a
dated decision log with rationale, and "Immediate next steps" at the end is the
prioritised backlog. Every task below is an item there — work from the spec, and
update it as things land, following the conventions already in it (a batch
narrative in §7, dated rows in §11, struck-through items in the next-steps list).

## How to work

Do not implement the backlog yourself. **Dispatch each task to a subagent, then
personally verify the result.** Your value here is the review, not the typing.

Pick the model by the kind of judgment the task needs, not by its size:

- **Haiku** — mechanical and fully specified: doc/spec text updates, inventory
  and audit sweeps ("list every X that does Y"), changelog and README sync,
  renames. Do not give Haiku geometry or UI-state work.
- **Sonnet** — well-scoped implementation against an existing pattern in the
  repo, where the acceptance test is obvious: UI plumbing, wiring an operation
  to a panel, file dialogs, async/progress. Most of this backlog is Sonnet work.
- **Opus** — algorithmic or topological reasoning where being subtly wrong looks
  like success. Here that is item 12 and nothing else.

Dispatch independent tasks in parallel. Give each subagent the *invariant* it
must satisfy and the relevant §, not just the change to make.

## How to verify (read this before accepting anything)

The recurring failure mode in this codebase is **work that reports success while
being wrong**, and §11 records several instances. From the most recent batch
(§7, M4-8):

- Every edit operation mutated the mesh correctly and nothing appeared on
  screen — for days, with a green suite — because only load/undo/redo refreshed
  the UI.
- `Plane Cut` appended its result instead of replacing it. The test asserted
  `TriangleCount > 0`, which passes just as well with the discarded half still
  in the mesh.
- `Auto Repair` reported **"0 issues found"** while having *deleted* the model's
  cut end. The only tell was the bounding box shrinking, which nothing asserted.

So, for each returned task:

1. Build, and run `dotnet test tests/Meshwright.Tests -c Release`. Expect
   **453 passing, 0 skipped** plus whatever the task adds. Never accept a newly
   skipped test without an explicit reason.
2. **Ask what invariant would catch this being wrong**, and check the test
   asserts that — not merely that the operation ran. Per §11 (2026-09-05), the
   invariants that work for geometry are bounding box, volume, shell count and
   issue count, compared before and after.
3. **Run the actual app and look at it.** A passing suite is not evidence the
   feature works; see above, and §11's 2026-09-04 row on gizmo tests passing
   with synthetic rays while every real click failed. Launch guidance is in
   memory under `reference-running-meshwright-gui` — real X display on
   `DISPLAY=:0`, the app takes a file path argument, and
   `samples/broken-cube.stl` is 14 triangles with one of every defect.
4. Treat a success message as a claim to check, not a result. If a summary says
   "repaired" or "reduced", confirm the numbers moved the way they should and
   that nothing silently disappeared.

Report back honestly, including what you could not verify. Per §4, "never
silently destroy the model" and "honest diagnostics" are core principles, not
nice-to-haves — most of this backlog exists because they were violated.

## State

Ten unpushed commits on `main`; `origin` is
`github.com/bjornhenneberg/meshwright` and Pages deploys `docs/` via
`.github/workflows/static.yml`. 453 tests + 8 GPU tests pass.
`docs/usage.html` is the user-facing guide and its "Known rough edges" section
must stay in sync with §7/§11 as items land.

One commit in history, `842c0ab`, baked a temporary screenshot hack into
`App.axaml.cs`; it is superseded by later commits, but consider squashing it
before pushing.

## Backlog — the numbered items under "Immediate next steps" in the spec

**§12 — Cap multi-loop cut cross-sections. (Opus)**
The largest remaining correctness gap, and it blocks the §5.1 promise of
"cut with optional cap ... keep one side or split into separate parts" for any
model with holes through it. Invariant: cutting a Menger sponge leaves one shell
per side with **zero** self-intersections, and preserves total volume.

**§13 — Run long operations off the UI thread. (Sonnet)**
Invariant: the window stays responsive during a repair of
`~/Downloads/Eiffel_tower_sample.STL` (139,989 triangles, 36,708 issues).
Note §6.4 sets throughput targets but says nothing about responsiveness —
consider whether it should, and add a §11 row if you decide it does.

**§14 — Boolean between loaded meshes. (Sonnet)**
Straight §5.1 scope compliance. Update `docs/usage.html`, which currently
documents the fixture-cube limitation in its rough-edges list.

**§15 — Make `HoleFillMode.Smooth` actually smooth. (Sonnet)**
Small, well-bounded, listed in §5.1's hole-filling variants.

**§16 — Gizmo coverage. (Sonnet)**
Against the gizmo-first direction adopted in §11 (2026-09-04).

**Also worth doing (not yet in the spec):**
- Retake `docs/images/decimate.png` (Sonnet). It was captured from a mesh
  produced by the *old, broken* plane cut, so it shows a model that had already
  lost geometry. The message it illustrates is still correct; the mesh is not.
- Windows/macOS CI + packaging (§7, still open under M4-3) — the largest
  remaining M4 gap, and per §11 (2026-09-04) it was deliberately deferred
  because neither can be verified on this dev host. Check that constraint still
  holds before starting.

Ask me before pushing, and before starting anything not on this list.
