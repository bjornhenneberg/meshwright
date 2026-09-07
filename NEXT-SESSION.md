# Next session

You are working on the Meshwright repo (`/home/bjorn/Code/meshwright`) — a
cross-platform desktop tool for repairing meshes for 3D printing (C# /
.NET 10 / Avalonia / Silk.NET, geometry on a vendored g3Sharp).

**Read `SPECIFICATION.md` first.** §5.1 is v1.0 scope, §7 narrates each
milestone batch, §11 is a dated decision log (read the last ~15 rows — they are
the most useful pages in the repo), and "Immediate next steps" at the end is
the backlog. Items 1–21 and 23–27 are done; **22, 28 and 29 are open**. Two
landed 2026-09-07: **item 26** (`fix/split-separate-halves`,
`reports/M4/20260907T000000Z-split-separate-halves/report.md`) and **item 24**
(`fix/hole-fill-seams`, `reports/M4/20260907T020000Z-hole-fill-seams/report.md`),
with the docs and the site updated in the same pass.

**Nothing is queued.** Pick from the backlog — **22 is the obvious next one**:
it is the last correctness gap of the three, it is real geometry, and it is the
same shape as the two just closed (the app telling the user something untrue
about geometry it just made). 28 and 29 are both platform/rendering work.

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
   **830 passing, 0 skipped**. Never accept a newly skipped test without a
   stated reason.
2. The GPU suite is `tests/Meshwright.Tests.Gpu` (28 tests, ~0.5 s). **Always
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
- A **ComboBox popup is its own X window**, so `gnome-screenshot -w` on the app
  window does not show it — capture the whole screen (`gnome-screenshot -f`) and
  work in screen coordinates for that one click.
- Keyboard: `XTestFakeKeyEvent` with keycodes from
  `XKeysymToKeycode(XStringToKeysym(name))`. The modifier's keysym name is
  **`Control_L`**, not `ctrl` — an invalid name resolves to keycode 0 and the
  chord silently becomes a bare keypress, so `Ctrl+Z` typed a literal `z` into
  whatever had focus and the undo I thought I had done had not happened.
- To capture a **before** screenshot of a defect you have just fixed, build the
  pre-fix commit in a throwaway `git worktree` and run that. It costs one build
  and gives a real before/after pair.

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

`main` carries the split-separation slice (item 26) and the hole-fill seam fix
(item 24). **830 tests passing, 0 skipped**; GPU suite **28** passing, both re-run on the merge commit.

**That merge went red on Windows CI** and needed a follow-up
(`fix/settings-tests-windows-paths`). Four `AppSettingsTests` asserted on
literal `"/tmp/a.stl"` strings, and `Path.GetFullPath` turns those into
`C:\tmp\a.stl` on Windows: the product was right and the expectations were
not. Worth knowing because **CI is the only place Windows and macOS ever run,
and only a push to `main` or a PR triggers it** — so an unportable test is
invisible until the merge, and the cost is a red `main` until you notice. If a
new test hard-codes a path, a separator, a line ending or a case comparison, ask
what it does on Windows before merging; `Path.Combine`/`Path.GetTempPath` and
asserting against `Path.GetFullPath(input)` rather than a literal is usually the
whole fix. Do not merge and walk away — wait for the run.

`README.md`, `docs/index.html` and `docs/usage.html` are current as of the
hole-fill seam fix. `README.md` was rewritten on 2026-09-06: it had become an
index into `SPECIFICATION.md` (milestone codes as the status, "see §8" for the
licence, `reports/M4/` for the platform split), and now answers what a stranger
opens a repo to find out. **Keep it that way** — when you finish a slice, update
the README and the site in the same language a user would use, and mention the
spec only under Contributing. Screenshots for the site are copied out of the
slice's own report into `docs/images/` rather than re-shot.

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
and §11 are updated. Both are **done** — pins as item 25
(`reports/M4/20260906T163000Z-registration-pins/report.md`, which now has its
screenshots, captured during the item 26 slice), and the Viewport/UX block as
item 23, in four slices.

**23. ~~Finish §5.1's Viewport / UX block~~ — done 2026-09-06**, all four slices.

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

The **cross-section slider is done** (2026-09-06, branch
`feat/viewport-cross-section`, report in
`reports/M4/20260906T233000Z-viewport-cross-section/report.md`): `Ctrl+Shift+C`
opens a bar under the viewport with an axis picker, a millimetre slider and Flip
Side. Things the next slices inherit:

- It is **not a fourth render pass** — it is a fragment discard inside the mesh
  pass and its flagged-edge overlay, so it costs the same at any triangle count.
  The build plate and the gizmo are deliberately **not** clipped.
- **Avalonia's framebuffer has no stencil attachment** (probed in the running
  app: `stencilSize=0`, `depthSize=24`). Anything wanting stencil — a capped
  section, an outline pass, a masked overlay — needs its own FBO and a blit.
  This is why the section has no cap; that is now item 28.
- **A world-millimetre value carried across a model change is meaningless.** The
  section inherited 0 mm from the sample tetrahedron and opened a 40 mm cube on
  an empty viewport. Found by opening the app after the suite was green — the
  third time in three viewport slices that the defect was only visible on screen.

The **import conveniences slice is done** (2026-09-06, branch
`feat/import-conveniences`, report in
`reports/M4/20260906T2359Z-import-conveniences/report.md`), and with it item 23.
Things the next slices inherit:

- **`settings.json` exists.** `~/.config/meshwright/settings.json`
  (`%APPDATA%\meshwright\` on Windows), plain `System.Text.Json`, one
  `AppSettings` type. Anything that should outlive a session adds a property and
  is written by whoever changes it; every property has a default and nothing in
  `SettingsStore` throws, so a new one costs a line. **`MESHWRIGHT_SETTINGS_FILE`
  overrides the path**, and the unit suite sets it from a module initializer —
  without that, hundreds of tests that build a `MainWindow` would read and
  rewrite the settings of whoever is running them.
- **Avalonia's X11 backend has no drag-and-drop, in either direction.** Measured
  three ways (§11) after a real GTK drag onto the running app did nothing. Any
  future in-app drag — reordering a list, dragging a mesh onto the Boolean
  panel's second slot — will not work on Linux either. Item 29 sketches the fix.
- **A refusal is now free** (item 27, done). An operation returning
  `Changed: false` costs the user nothing: no undo entry, no cleared redo, no
  gizmo rebuild, no lost placement. New operations should refuse that way rather
  than throwing.
- `IDataObject` and `DragEventArgs.Data` are **obsolete** in Avalonia 11.3.20 —
  use `IDataTransfer`/`DataTransferItem`. `IStorageFile` cannot be implemented
  by application code, so a test that needs one reaches Avalonia's internal
  `BclStorageFile` by reflection.
- **The drain-hole refusal path now has on-screen evidence** (the standing
  verification gap, closed): see the report's §5.

**26. ~~A split leaves both halves in one mesh with coincident cut faces.~~ —
done 2026-09-07.** The halves are moved apart along the cut normal before being
merged, by the peg protrusion plus `max(1 mm, 5% of the extent along the
normal)`. Report:
`reports/M4/20260907T000000Z-split-separate-halves/report.md`. Things the next
slices inherit:

- **Two documents was considered and rejected**, deliberately: it changes the
  single-document model (`MeshDocument`, the one undo stack, the one viewport
  upload, the Boolean panel's secondary mesh, Export) and has no undo semantics.
  The file-per-part gap that left is closed by **File → Export Parts**, which
  writes `<base>-part1.stl`, `-part2.stl`, largest first, via the new
  `MeshShells.Separate`. If a future slice wants multiple documents, it is a
  milestone, not a corner of something else.
- **`MeshDiagnosticsReport` now distinguishes defects from notes.** `Defects` /
  `DefectCount` are everything at Warning or above; `Info` findings describe the
  model rather than fault it. The status line and the viewport's red highlight
  both read `Defects`. **Anything new that surfaces "issues" to a user should
  read `DefectCount`, not `Issues.Count`** — the first version of this fix left
  the status line saying "0 issues found" while the viewport painted half the
  model red, because those two disagreed about what an issue was.
- **A disconnected shell at or above 1% of total volume is a `SeparatePart`, not
  debris.** Same threshold `SmallShellRemovalRepair` defaults to, on purpose:
  what Auto Repair may delete and what Inspect calls debris must not drift.
- The separation is a **pure translation along the cut normal**, and the test
  that pins it re-mates the halves and requires the original bounding box back to
  nine decimal places. That is what keeps registration pins meaningful, and it is
  the assertion to preserve if the gap rule ever changes.
- The pre-fix build was rebuilt in a throwaway `git worktree` to capture the
  "before" screenshot. Cheap, and worth doing whenever a fix's evidence is a
  before/after pair.

**24. ~~Hole filling and hole detection disagree about import seams.~~ — done
2026-09-07.** Both sides now call `PositionTopology.OpenBoundaryLoops`. Report:
`reports/M4/20260907T020000Z-hole-fill-seams/report.md`. Worth knowing:

- **`tests/corpus/files/` is fetched on this machine** (87 files; the directory
  is gitignored, so CI skips the corpus tests). It is the fastest way to find
  out whether a defect is a corner case or the norm — 14 of the 24 files with
  ground truth carry seam-only boundary loops. Scan it with a throwaway test
  before assuming a bug is rare.
- **`thingi10k-92067.stl` is a good adversarial fixture**: 1,386 triangles,
  1,037 shells, 15,012 issues, and not one hole. Small enough to open instantly,
  broken enough that almost any repair defect shows up on it.
- A corpus-wide agreement test needs a **second assertion that the corpus still
  contains the case** — otherwise it passes vacuously the day the corpus thins.

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

**29. Drag-and-drop cannot work on Linux.** Avalonia's X11 backend has no
drag-and-drop implementation, so a file dropped from a file manager never
reaches the window — the window is not even advertised to X11 as a drop target.
The handling is written and is correct for Windows and macOS. A Linux fix means
our own XDND receiver (an `InputOnly` child window carrying `XdndAware`, on our
own display connection, handling `XdndEnter`/`Position`/`Drop` and
`XConvertSelection` for `text/uri-list`) — platform work with real risk to the
viewport's input path, so its own slice. Watch upstream first.


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
