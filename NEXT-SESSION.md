# Next session

You are working on the Meshwright repo (`/home/bjorn/Code/meshwright`) — a
cross-platform desktop tool for repairing meshes for 3D printing (C# /
.NET 10 / Avalonia / Silk.NET, geometry on a vendored g3Sharp).

**Read `SPECIFICATION.md` first.** §5.1 is v1.0 scope, §7 narrates each
milestone batch, §11 is a dated decision log (read the last ~15 rows — they are
the most useful pages in the repo), and "Immediate next steps" at the end is
the backlog. Items 1–21 and 25 are done; 22, 24, 26 and 27 are open, and 23 is
part-done — its first two slices (camera and display modes; build plate) landed
2026-09-06, and the docs for both are written. **Start with item 23's next
slice, the cross-section preview slider.**

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
   **722 passing, 0 skipped**. Never accept a newly skipped test without a
   stated reason.
2. The GPU suite is `tests/Meshwright.Tests.Gpu` (22 tests, ~0.5 s). **Always
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
   next to it too. The camera slice found its worst defect this way: the tests
   were green and three of the seven new view presets drew a black silhouette.
5. **Embed the screenshots in the markdown**, with a caption saying what to
   look at — `![Top view, orthographic — no hole side-walls, so the projection
   really is parallel](top-orthographic.png)`. A PNG dropped beside a report is
   evidence nobody sees; the `reports/M4/20260905T221759Z-ux-audit/` folder has
   111 of them and its report embeds none. Pictures are the fastest way to show
   a UI change is real and the cheapest way for a reader to catch that it
   isn't, so put before/after pairs side by side and say what changed. Same for
   any markdown with evidence to show, this handoff included.

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

`main` is clean and pushed, at `13c03a2`. **722 tests passing, 0 skipped**; GPU
suite **22** passing. **CI green on Linux, Windows and macOS** on the current
commit, and Pages has deployed it.

The last session touched **documentation only** — no source file changed, so
those test numbers are inherited from the build plate slice rather than re-run.
`docs/usage.html` now covers the build plate and registration pins, and
`README.md` was rewritten: it had become an index into `SPECIFICATION.md`
(milestone codes as the status, "see §8" for the licence, `reports/M4/` for the
platform split), and now answers what a stranger opens a repo to find out. **Keep
it that way** — when you finish a slice, update the README and the site in the
same language a user would use, and mention the spec only under Contributing.
Two build-plate screenshots were copied from the slice's report into
`docs/images/`; do the same rather than re-shooting the app for the site.

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
`reports/M4/20260906T163000Z-registration-pins/report.md` — note that report has
no screenshots at all, which AGENTS.md says it should; if you are in the app with
a pinned split on screen, capture one and add it); the Viewport/UX block is two
slices in.

**23. Finish §5.1's Viewport / UX block — DO THIS FIRST.** One slice per branch.

The **camera and display modes slice is done** (2026-09-06, branch
`feat/viewport-camera-modes`, report in
`reports/M4/20260906T210000Z-viewport-camera-modes/report.md`): orthographic
projection, seven view presets on `Ctrl+1`–`Ctrl+7`, wireframe and x-ray, all in
the View menu and all verified in the running app. Three things from it that the
next slices inherit:

- The light is now a **headlight** derived from the view matrix. It was fixed in
  world space, and the moment presets existed, Front/Left/Bottom rendered the
  model as a black silhouette. If you add geometry with its own shader (a build
  plate grid, say), decide deliberately how it is lit rather than copying the
  old constant.
- `OrbitCamera.ProjectionMode` changes the matrix that `GizmoScale`,
  `ViewportRaycaster` and every gizmo's pick path resolve against. The
  orthographic pick contract is covered by
  `tests/Meshwright.Tests/Camera/OrthographicPickingTests.cs` — extend it rather
  than assuming a new interaction carries over.
- The orthographic near plane is **behind the eye** (`-FarPlane`), so an
  unprojected ray's origin is far back. Anything reasoning about distance along
  a pick ray should not assume the origin is near the model.

The **build plate slice is done** (2026-09-06, branch
`feat/viewport-build-plate`, report in
`reports/M4/20260906T230000Z-viewport-build-plate/report.md`): a grid on the Z=0
plane sized to one of five printer presets in View → Build Plate
(`Ctrl+Shift+B` hides it), and a status-bar warning naming every side the model
overhangs and by how much, with the bed outline turning amber to match. Things
the next slices inherit:

- **The plate never scales to the model** — the bed is drawn at true size and the
  *spacing* adapts in two tiers (major from the bed, minor from the model's
  footprint). Anything else that has to be readable at both the 2 mm sponge and
  the 120 mm tower should follow the same shape rather than resizing itself.
- **The viewport now has three render passes**: build plate (depth test and
  depth writes on, before the mesh), mesh, then gizmo (depth test off). A fourth
  has to pick its place in that order deliberately.
- **A dim UI element needs a contrast floor.** The minor grid shipped at a colour
  that resolved to the background within one 8-bit step — drawn, tested, and
  invisible. If you write a pixel test for visibility, measure *contrast*: the
  first version counted pixels that were "not the clear colour" and passed at the
  broken value.
- **Avalonia's `HotKey` activation does not update a menu item's `IsChecked`.**
  This made `Ctrl+Shift+B` a silent no-op, and had already left `Ctrl+Shift+O`
  switching the projection while the menu kept its dot on Perspective. All View
  menu handlers now derive their state and `RefreshViewMenuChecks` writes the
  check marks back. Tests that raise `Click` directly cannot see this class of
  bug — drive `KeyPressQwerty`.

Remaining, in order:

1. **Cross-section preview slider.** (Note item 26: cutting a model to try it
   will show 1,808 issues on a clean sponge, and that is not your bug.)
2. **Import conveniences**: mm/inch unit detection and scaling, drag-and-drop,
   recent files. **Recent files needs settings persistence, which does not exist
   anywhere in the codebase yet** — skipped for exactly that reason on
   2026-09-04, now a v1.0 dependency. Decided 2026-09-06: JSON in the platform
   config directory (`~/.config/meshwright/settings.json`) via
   `System.Text.Json`, no dependency and no database; bed size, unit preference
   and window state will share it.

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

**An unexplained flake**: one full unit run during the camera slice reported a
single failure and the name was lost to a grep filter; five consecutive clean
runs since, and no reproduction. If you see it, capture the whole output rather
than grepping it away as I did.

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
