# Next session

You are working on the Meshwright repo (`/home/bjorn/Code/meshwright`) — a
cross-platform desktop tool for repairing meshes for 3D printing (C# /
.NET 10 / Avalonia / Silk.NET, geometry on a vendored g3Sharp).

**Read `SPECIFICATION.md` first.** §5.1 is v1.0 scope, §7 narrates each
milestone batch, §11 is a dated decision log (read the last ~15 rows — they are
the most useful pages in the repo), and "Immediate next steps" at the end is
the backlog. Items 1–21 and 25 are done; 22, 23, 24, 26 and 27 are open.

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
   **581 passing, 0 skipped**. Never accept a newly skipped test without a
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
  shell's own command line contains the pattern. `pkill -f "Meshwright[.]App"`
  is only half the fix — it still matches if the *same command* also mentions a
  real path like `src/Meshwright.App` (chaining a build after the pkill kills
  the shell before the build runs). Put the pkill in a command of its own and
  break the pattern too: `pkill -f 'Meshwr[i]ght\.App'`.
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
Apply. Plane Cut, Transform, Drain Hole and Hollow all have gizmos, and the plane cut
gizmo also carries the registration pin. Boolean is the notable gap — its
secondary mesh is used at its own file coordinates with no way to reposition it.

Gizmos are now rendered with the depth test **disabled**. It was enabled, so any
gizmo inside the solid — which the plane cut gizmo always is, being anchored at
the mesh centre — drew nothing at all.

MainWindow holds exactly **one** gizmo slot, so a new gizmo must go through
`ActivateGizmoOwner`, never by assigning `Viewport.Gizmo` directly. Wire panel
fields *into* the gizmo as well as out of it, and watch the control's
`TextProperty` rather than its `TextChanged` routed event — the routed event
never fires for a panel exercised outside a visual tree, which is how the
drain-hole gizmo placed every hole at a hard-coded 2 mm behind a green suite.

## State

`main` is clean and pushed. **581 tests passing, 0 skipped**; GPU suite 8
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

Both scope questions were decided by the user on 2026-09-06: **build the
Viewport/UX block for v1.0**, and **promote registration pins into v1.0**. §5.1
and §11 are updated. Pins are **done** (item 25, see
`reports/M4/20260906T163000Z-registration-pins/report.md`); the Viewport/UX
block is not started.

**23. Build §5.1's Viewport / UX block — DO THIS FIRST.** The largest remaining
v1.0 gap and a multi-batch job. Nothing of it exists in the code — orthographic projection,
standard view presets, the build plate grid with configurable printer size and
its out-of-bounds warning, wireframe and x-ray display modes, the cross-section
preview slider, mm/inch unit handling, drag-and-drop, recent files. Suggested
order: the camera and display modes first (one subsystem, immediately visible
in the app), then the build plate and out-of-bounds warning, then the
cross-section slider, then the import conveniences. **Recent files needs
settings persistence, which does not exist anywhere in the codebase yet** — it
was skipped for exactly that reason on 2026-09-04, and is now a v1.0
dependency. Decided 2026-09-06: JSON in the platform config directory
(`~/.config/meshwright/settings.json`) via `System.Text.Json`, no dependency
and no database; bed size, unit preference and window state will share it. Do
one slice per branch.

**22. Decimation introduces the invalid geometry it says it declined to
create.** Reducing the clean Menger sponge to 734 triangles produced 67
self-intersections while the panel said further collapses "would have created
invalid geometry" — and the status bar reported the 67 in the same frame. The
local validity test each edge collapse passes has to be checked against
whole-mesh invariants afterwards. Real geometry work, and self-contained.

**24. Hole filling and hole detection disagree about import seams.**
`BoundaryHoleDetector` excludes seams via `PositionTopology.SeamEdges`;
`HoleFillRepair` finds loops with `MeshBoundaryLoops`, which is vertex-index
based and has no such exclusion. Inspect can report zero holes on a file whose
Auto Repair then adds geometry across a seam. Small and sharp. A *different*
bug from the `DMesh3.Copy` one fixed on 2026-09-06 — don't assume that covered
it.

**26. A split leaves both halves in one mesh with coincident cut faces.**
Inspect reports 1,808 issues after splitting the clean Menger sponge, while
each half measured on its own is closed, single-shell and issue-free. Either
separate the halves or make a split produce two documents. Found while
verifying pins (2026-09-06); not caused by them.

**27. A refused operation still counts as a change.** `MeshDocument.ApplyAsync`
calls `RefreshReport` unconditionally, so an operation returning
`Changed: false` still pushes an undo entry and makes MainWindow rebuild every
gizmo — a user whose pin will not fit loses the pin they had positioned.

**A verification gap left behind**: the drain-hole **refusal path** (a hole too
big for the surface) has two tests but no on-screen evidence. Cheap to close
next time the app is open.

## Researching on the web

Reddit is reachable now — see `scripts/browse.py`, which drives a real headed
Chromium over the DevTools protocol. `curl` and `WebFetch` get 403s and
*headless* Chromium gets a "Prove your humanity" challenge; a normal browser
window on `DISPLAY=:0` is served normally. Start the browser once, then run the
script against it (usage is in the file's docstring). **If a bot challenge ever
does appear, stop and tell the user** — do not work around it.

`reports/research/meshmixer-alternatives.md` now has a real Reddit section.
Its strongest finding is one the user should weigh: **macOS demand looks
stronger than §9's "macOS once there is revenue" assumes.** The
highest-engagement thread found in the whole effort is Mac users with no
option — Fusion 360 "almost unusable" on M1, Blender too steep — installing
abandoned Meshmixer from a Wayback Machine snapshot and thanking each other for
the link, years on, despite a publicised security flaw. Windows has 3D Builder
absorbing the simple cases; macOS has nothing.

## Settled, so you don't reopen them

All decided by the user on 2026-09-06 (§11 carries the reasoning):

- **macOS signing/notarisation stays deferred**, but the trigger is now the
  first tagged release rather than "once there is revenue". The Reddit evidence
  argues the other way; this is a deliberate not-yet. Keep shipping unsigned
  `.app` zips with the Gatekeeper workaround documented.
- **Settings are JSON in the platform config directory.** Not a database, not a
  settings library.
- **Pins before the viewport block** — pins are now done, so the viewport block
  is next, starting with the camera and display modes.

Nothing is currently waiting on the user.

Push freely; the user has given standing authorisation. Ask before starting
anything not on this list.
