# Next session

You are working on the Meshwright repo (`/home/bjorn/Code/meshwright`) — a
cross-platform desktop tool for repairing meshes for 3D printing (C# /
.NET 10 / Avalonia / Silk.NET, geometry on a vendored g3Sharp).

**Read `SPECIFICATION.md` first.** §5.1 is v1.0 scope, §7 narrates each
milestone batch, §11 is a dated decision log (read the last ~15 rows — they are
the most useful pages in the repo), and "Immediate next steps" at the end is
the backlog. Items 1–21 are done; 22, 23 and 24 are open.

## How to work

**Do the work yourself, in this one session.** Do not fan out to parallel
subagents — the last session ran three at once and it cost more than it saved:
two agents driving the same X display sent synthetic clicks into each other's
windows and killed each other's app process, and one verification was lost
outright. If you want a subagent for a genuinely separable, non-GUI slice
(a research sweep, a broad read-only search), fine — one at a time, and never
two that both need the display or the build output.

The corollary: **you** are the one who has to catch your own mistakes, with no
dispatcher reviewing your work. Budget for that. Verify before claiming.

### Branching

**Work on a feature branch and merge to `main` when the work is done and
green.** CI triggers only on pushes to `main` and PRs targeting `main`
(`.github/workflows/ci.yml`), so a branch costs nothing: push it as often as
you like, then pay for exactly one CI run at the merge, instead of one per
push as before.

```bash
git checkout -b fix/whatever      # branch first
git push -u origin fix/whatever   # free, no CI
# ... when done, verified, and the suite is green ...
git checkout main && git merge --no-ff fix/whatever && git push
```

Don't open a PR unless you actually want the review — opening one against
`main` spends a CI run too. Pushing to `main` directly is still allowed for
genuinely trivial things, but branch by default.

## How to verify

The recurring failure mode here is **work that reports success while being
wrong**. §11 is largely a catalogue of it. Treat a success message — including
your own — as a claim to check.

1. Build and run `dotnet test tests/Meshwright.Tests -c Release`. Baseline is
   **562 passing, 0 skipped**. Never accept a newly skipped test without a
   stated reason.
2. The GPU suite is `tests/Meshwright.Tests.Gpu` (8 tests, ~0.5 s). **Always
   run it under `timeout`** — it used to hang past ten minutes, and a hang
   orphans a test host that outlives the session. Five had accumulated on this
   machine once, one for 22 hours.
3. Ask what invariant would catch this being wrong, and check the test asserts
   *that*, not merely that the operation ran. For geometry: bounding box,
   volume, surface area, shell count and issue count, compared before and
   after. `TriangleCount > 0` after a cut passes just as happily when the
   operation appended its result on top of the half it was told to discard.
   The drain-hole rewrite was pinned by "area removed ≈ πr²" and "vertex count
   must increase" — both of which the old broken version failed outright.
4. **Run the actual app and look at it** unless told not to. A passing suite is
   not evidence a feature works, and neither is a screenshot — read the words
   next to it too.

Three techniques that worked when guessing did not:

- **Reproduce an unavailable platform's constraint locally.** The macOS
  native-loading bug was fixed and verified with no Mac, because hiding
  `runtimes/` in the Linux test output recreates the exact condition.
- **Make the failing thing report instead of theorising about it.** Two
  hypotheses were already wrong before a self-diagnosing checksum and a CI step
  that asked dyld directly settled the cause.
- **When two unrelated features fail the same way, suspect one cause
  underneath both.** That is how `DMesh3.Copy` was caught (see §11) — a drain
  hole and an uncapped cut were both invisible to hole detection, and the real
  bug was in neither feature.

## Driving the GUI

There **is** a display: `DISPLAY=:0`, 1920x1080. `AGENTS.md` claims there
isn't; it is wrong. The user has confirmed it is fine to use — synthetic input
takes over their real cursor for a few seconds at a time, so keep interaction
batches short.

- Launch with a file (the app takes a path argument), as a **background** task;
  a `nohup ... &` inside a foreground Bash call dies with the shell:
  `DISPLAY=:0 dotnet run --project src/Meshwright.App -c Release --no-build -- model.stl`
- **`pkill -f "Meshwright.App"` kills the calling shell too**, because the
  shell's own command line contains the pattern. Use `pkill -f "Meshwright[.]App"`.
- Available: `gnome-screenshot`, `wmctrl`, `xwd`, Python + PIL. No `xdotool`,
  no ImageMagick, no `xvfb-run`.
- Synthetic input: ctypes against `libXtst.so.6` — `XTestFakeMotionEvent` lives
  in libXtst, **not** libX11. Left-drag orbits, middle/right-drag pans, wheel
  zooms.
- **Coordinate mapping**: `gnome-screenshot -w` includes the window frame, so
  screenshot coordinates are not screen coordinates. With the window placed via
  `wmctrl -i -r $W -e 0,90,80,1740,1000`, the measured offset is
  `screen = shot + (90, 8)`. Re-measure after moving the window (move the
  pointer, capture with `gnome-screenshot -p`).
- Re-`wmctrl -i -a $W` before every interaction and every screenshot; focus
  drifts, and `-w` captures whatever is active. Confirm from the screenshot
  that you are looking at your own window before believing any result.
- The sidebar tab strip reflows as tabs are added, shifting every control below
  it — locate buttons from a fresh screenshot rather than reusing coordinates
  across builds.

Meshes to drive it with:
- `samples/broken-cube.stl` — 14 triangles, one of every defect.
- `~/Downloads/Menger_sponge_sample.stl` — clean, 2112 triangles, holes right
  through it. Excellent at breaking assumptions: it broke the cut-cap
  algorithm, and a ray through its centre misses the surface entirely.
- `~/Downloads/Eiffel_tower_sample.STL` — 139,989 triangles, 36,708 issues.
  Use for the §6.4 responsiveness target. Note the uppercase `.STL`.

## Design direction worth knowing

**Gizmo-first.** The user's own words: *"i want gizmo (meaning 3d interaction?)
on everything if possible, no one will use a textbox control."* For any spatial
parameter the viewport gizmo is the primary interaction and textboxes are a
precision fallback; once a gizmo has been touched, its values win outright on
Apply. Plane Cut, Transform, Drain Hole and Hollow all have gizmos. Boolean is
the notable gap — its secondary mesh is used at its own file coordinates with
no way to reposition it.

MainWindow holds exactly **one** gizmo slot, so a new gizmo must go through
`ActivateGizmoOwner`, never by assigning `Viewport.Gizmo` directly. Wire panel
fields *into* the gizmo as well as out of it, and watch the control's
`TextProperty` rather than its `TextChanged` routed event — the routed event
never fires for a panel exercised outside a visual tree, which is how the
drain-hole gizmo placed every hole at a hard-coded 2 mm behind a green suite.

## State

`main` is clean and pushed. **562 tests passing, 0 skipped**; GPU suite 8
passing. **CI green on Linux, Windows and macOS** on the current commit.

- `gh` is authenticated and git has a credential helper, so you can push. The
  token has `workflow` scope for `.github/workflows/` changes.
- The repo is `github.com/bjornhenneberg/meshwright`. Pages deploys `docs/` via
  `.github/workflows/static.yml`; `docs/index.html`, `docs/usage.html` and
  `README.md` all hardcode that URL, so a rename means grepping for it.
- **Stale worktrees**: two old unrelated ones (`agent-a0ef1435...`,
  `agent-a15af9a8...`) and `meshwright.worktrees/progress-check-inquiry`
  predate all recent work — leave them alone unless you know what they are.

## Backlog

**22. Decimation introduces the invalid geometry it says it declined to
create.** Reducing the clean Menger sponge to 734 triangles produced 67
self-intersections while the panel said further collapses "would have created
invalid geometry" — and the app's own status bar reported the 67 in the same
frame. The local validity test each edge collapse passes has to be checked
against whole-mesh invariants afterwards. **Best item; real geometry work.**

**24. Hole filling and hole detection disagree about import seams.**
`BoundaryHoleDetector` excludes seams via `PositionTopology.SeamEdges`;
`HoleFillRepair` finds loops with `MeshBoundaryLoops`, which is vertex-index
based and has no such exclusion. So Inspect can report zero holes on a file
whose Auto Repair then adds geometry across a seam. Small and sharp. This is a
*different* bug from the `DMesh3.Copy` one that was fixed on 2026-09-06 —
don't assume that fix covered it.

**23. Most of §5.1's Viewport / UX block does not exist** — orthographic
projection, view presets, build plate grid, out-of-bounds warning, wireframe
and x-ray modes, the cross-section slider, unit handling, drag-and-drop and
recent files. Absent from the code, not merely unwired. The largest remaining
v1.0 gap, but **it needs the user's scope decision first** (below).

**A verification gap left behind**: the drain-hole **refusal path** (a hole too
big for the surface) has two tests but no on-screen evidence, because synthetic
clicks landed in another agent's window. Cheap to close next time the app is
open.

**Packaging follow-ups** stay deferred: §9 reads "Linux + Windows first; macOS
once there is revenue". An MSI is buildable on the `windows-latest` runner but
unverifiable from this host, and there is no tagged release yet.

## Waiting on the user

Neither is yours to decide; both change what v1.0 contains.

1. **Item 23**: build the missing Viewport/UX block for v1.0, or move it to
   §5.2?
2. **Registration pins**: §3 names "adding registration pins" as a target-user
   workflow while §5.2 defers pins to v1.x, and plane-cut splitting is already
   v1.0. Promote a minimal peg-and-socket pair into §5.1? Proposed wording is
   in `reports/research/meshmixer-alternatives.md`.

Also note **item 5 (Meshmixer research) is only partially done** — Reddit was
unreachable to the last agent's tooling and Autodesk's forum 403s, so "read a
week of threads" was not met. Its conclusions are directional, not settled.

Push freely; the user has given standing authorisation. Ask before starting
anything not on this list.
