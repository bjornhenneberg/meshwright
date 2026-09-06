# Build plate and out-of-bounds warning (backlog item 23, second slice)

**Date:** 2026-09-06 · **Branch:** `feat/viewport-build-plate`

The second slice of §5.1's Viewport / UX block: a grid on the Z=0 plane sized to
a configurable printer bed, and the warning that fires when part of the model
falls outside it. The cross-section slider, mm/inch units, drag-and-drop and
recent files are **not** in this slice.

Every picture below is the real app on the 139,989-triangle Eiffel tower and the
2 × 2 × 2 mm Menger sponge samples, not a headless render.

## What a user can now do

| Where | Does |
| --- | --- |
| View → Build Plate → *printer* | Picks the bed: Prusa MINI, Ender 3, Prusa MK4, Bambu X1C, or a 350 mm large-format |
| View → Build Plate → Show Build Plate (`Ctrl+Shift+B`) | Hides and shows the plate |
| Status bar | Names every side the model overhangs, and by how much — and says nothing at all when it fits |

![The View menu with the Build Plate submenu open: Show Build Plate is ticked,
and the five printer presets are listed below it with Ender 3 carrying the radio
dot](build-plate-menu.png)

*The bed is chosen from a preset list rather than typed. §11 records why it stops
there: a free-form custom size belongs in `settings.json`, which recent files
needs anyway, and building that subsystem inside a viewport slice would be a side
quest.*

## The plate

![The Eiffel tower sample standing on a 220 x 220 mm Ender 3 bed, isometric view:
a 10 mm grid recedes to the horizon and the bed's far edges are visible as a
brighter outline](eiffel-ender-isometric.png)

*The Eiffel tower (54.7 × 54.7 × 120.9 mm) on the default Ender 3 bed. Ten
millimetre grid, 22 divisions across, and the bed's own outline drawn brighter
than the grid.*

### Before and after: there was no plate at all

![The same view with the build plate hidden: the tower floats against an empty
dark background with nothing to indicate scale or where the bed
is](plate-hidden-BEFORE.png)

*`Ctrl+Shift+B`, plate hidden — which is exactly what every previous build
looked like. Nothing on screen says how big the model is or whether it would fit
a printer.*

![The same view with the build plate shown: the tower stands on a 10 mm grid that
gives it scale and a ground plane](plate-shown-AFTER.png)

*…and shown again. The status line reads "Build plate shown".*

## The out-of-bounds warning

Both frames below are the same model at the same camera, on the same 220 mm bed.
Only the model's position differs — moved 100 mm in X, then undone.

![Top view, orthographic: the Eiffel tower's footprint sits in the middle of a
220 mm bed whose outline is a pale blue-grey rectangle. The status bar shows only
"Undo (139989 triangles) - 36708 issues found" with no
warning](eiffel-top-in-bounds.png)

*In bounds — **and silent**. This is the half a broken implementation passes by
accident: a warning that is always on says as little as one that never fires, so
the absence here is as much the evidence as the presence below.*

![The same view after translating the model 100 mm in X: the tower's footprint
straddles the right-hand bed edge, the whole bed outline has turned amber, and
the status bar reads "Outside the build volume: 17.33 mm past the right
edge."](eiffel-top-out-of-bounds.png)

*Out of bounds. The bed outline turns amber and the status bar quantifies it:
**17.33 mm past the right edge**. The model's bounds are max x = 127.3 and the
bed's half-width is 110, so 17.33 is the arithmetic exactly right. The warning
names the side, not just the fact.*

### It agrees with Drop to Z=0 about where the bed is

The Menger sponge is modelled centred on the origin in all three axes, so it
loads 1 mm *below* the bed.

![The sponge on load, isometric: the grid plane passes through the middle of the
cube's lower half and the status bar reads "Outside the build volume: 1 mm below
the bed."](sponge-minor-grid-AFTER.png)

*On load — the plate cuts through the model because the model really is 1 mm
under it, and the warning says so.*

![The sponge after Drop to Z=0: it sits squarely on the grid, the warning is
gone, and the panel reports "Dropped to Z=0: moved up by 1 mm so lowest point is
at Z=0."](sponge-dropped-to-z0.png)

*After Drop to Z=0 the cube sits **on** the grid, and the warning disappears.
`AlignToBedOperation`/`DropToZ0Operation` define what "on the bed" means, and
the plate is drawn at the plane they move models to.*

## Edge-on, which is where a plate is easiest to lose

![The sponge in front view, orthographic: the entire build plate has collapsed to
a single horizontal line running the full width of the viewport, exactly level
with the base of the cube](sponge-front-orthographic-edge-on.png)

*Front + orthographic. Front, Back, Left and Right all look **exactly along** the
Z=0 plane, so the plate is one line — and that line is still drawn, at the
model's base, spanning the viewport. A GPU test asserts non-zero coverage for all
seven presets in both projections, and a second one asserts the drawn row is the
projection of world Z=0 to within two pixels.*

## The design problem: a 2 mm part on a 220 mm bed

The task named this as the decision to make deliberately, and it is the one place
this slice could have gone wrong quietly.

**The plate never scales to the model.** A bed that resized itself to whatever
was loaded would be decoration, and the single question it exists to answer —
does this part fit my printer — would become unanswerable. What adapts is the
line spacing, in two tiers: **major** lines from the bed alone (10 mm on a 220 mm
bed, always), **minor** lines from the model's footprint so a part spans at least
four divisions. Every line is still at a whole multiple of a real millimetre
spacing.

For the Eiffel tower there is no second tier — 120 mm already spans twelve major
divisions. For the sponge the minor tier is 0.5 mm.

### …and the defect that found

The first implementation drew that minor tier at 0.35 of the plate colour. The
tests were green — the lines were in the vertex buffer, at the right spacing, at
Z=0, in the right count. Opening the app showed what the suite could not:

![The Menger sponge on the bed with the minor grid at 0.35 intensity: the
viewport is essentially empty apart from two bright major lines crossing under
the model and the distant bed outline. The fine grid is not
visible.](sponge-minor-grid-BEFORE.png)

*Before. The only lines visible are the two 10 mm major lines through the origin
and the bed's far outline. The 0.5 mm grid **is** being drawn — the plate colour
(0.42, 0.46, 0.52) at 0.35 resolves to (0.147, 0.161, 0.182), and the viewport
clears to (0.15, 0.15, 0.18). It matched the background to within one 8-bit step.*

![The same scene with the minor grid at 0.65 intensity: a fine 0.5 mm grid is
clearly visible across the whole viewport, giving the 2 mm cube a ground plane to
stand on](sponge-minor-grid-AFTER.png)

*After. Same geometry, same spacing, one constant changed.*

The GPU test that now pins this had to be written twice. The first version
counted pixels that were "not the clear colour" — and **passed at the broken
value**, because a one-step difference is still a difference. It now measures the
*contrast* of the pixels the minor tier adds over a major-only frame, and
requires at least 20/255. Verified both ways: it fails at 0.35 and passes at
0.65.

## A pre-existing defect this slice ran into

`Ctrl+Shift+B` did nothing at all in the running app, while clicking the same
menu item worked. Avalonia updates a menu item's `IsChecked` when the item is
**clicked**, but not when it is activated by its **HotKey**, so a handler that
reads `IsChecked` flips a value that never changed.

The same root cause was already shipped in the camera slice: pressing
`Ctrl+Shift+O` really did switch the viewport to orthographic, while the View
menu went on showing the radio dot next to **Perspective** — a control describing
a state the app was not in, which is the failure §11 records three of on
2026-09-06. Confirmed in the running app: the status bar read "Orthographic
projection" and the menu's dot had not moved.

Fixed for all of them. Handlers derive their state (the toggle flips its own
flag, the radios read their `Tag`), and `RefreshViewMenuChecks` writes every
check mark back from the live viewport, so the menu cannot disagree with the
screen. Tests that raise `Click` directly cannot see this class of bug, so the
three regression tests drive `KeyPressQwerty` instead.

## Tests

**Unit: 722 passing, 0 skipped** (baseline 681, +41).
**GPU: 22 passing, 0 skipped** (baseline 15, +7).

| Suite | Command | Result |
| --- | --- | --- |
| Unit | `dotnet test tests/Meshwright.Tests -c Release` | 722 passed, 0 failed, 0 skipped |
| GPU | `timeout 400 dotnet test tests/Meshwright.Tests.Gpu -c Release` | 22 passed, 0 failed, 0 skipped |

### What the new tests assert

`BuildPlateFitTests` — the warning in both directions:

- comfortably inside → **no warning at all** (`Fits`, empty overhang list, null message);
- **exactly on every edge** → fits (a 220 × 220 × 250 part on a 220 × 220 × 250 printer is what that printer is sold to make);
- straddling one edge → the correct side, the correct millimetres, and only that side;
- sunk below Z=0, and taller than the build height, each caught on its own;
- **the same, unmoved bounds change verdict when the bed changes size** — fits on the 350 mm bed, four overhangs on the 220 mm one;
- multiple sides listed worst-first, with the exact message text pinned;
- the verdict comes from the mesh's real `CachedBounds`, checked with a box far off the bed and the same box moved onto it.

`BuildPlateGridTests` — the spacing rule:

- the major grid is between 5 and 25 divisions across on every bed from 20 mm to 1000 mm;
- the major grid depends on the bed alone: the tower and the sponge see the same 10 mm;
- a 120 mm model gets no minor tier; a 2 mm model gets one finer than the major and spans at least four of its divisions;
- a smaller model gets a finer grid than a larger one;
- an absurdly small model cannot ask for an unbounded grid (division ceiling, and a bound on the buffer length);
- **every vertex is at Z=0** and inside the bed, at three different model sizes;
- no line is drawn twice, so a minor line never overpaints a major one;
- the outline is the bed rectangle, on a deliberately non-square bed.

`BuildPlateMenuTests` and `ViewMenuTests` — the wiring, menu item to viewport:

- the menu offers every preset and each one reaches the viewport;
- changing the bed changes the verdict on a model that has not moved, in both directions and back;
- the warning names direction and amount, and says nothing about sides that are fine;
- hiding the plate takes the warning with it;
- an operation (Drop to Z=0) clears the warning, so the verdict follows edits and not only loads;
- **the shortcut toggles the plate**, and **a shortcut moves the check mark**, both driven through `KeyPressQwerty`.

`BuildPlateGpuTests` — what only the framebuffer can settle:

- the plate is visible in **all seven presets × both projections**, including the four that view it edge-on;
- the grid's drawn rows are the projection of world Z=0, at three camera heights, framed tightly enough that one pixel is under a millimetre;
- the model is drawn over the plate: every pixel the mesh covers alone is byte-identical with the plate drawn first;
- out of bounds changes the bed outline and adds warm pixels;
- the minor grid is **visible**, by contrast against the background, not merely present;
- the pass leaves the GL state it found — fill polygons, no blending, depth writes and depth test on, line width 1.

### Falsification

Every new assertion was checked against a deliberately broken build, not just
observed to pass:

| Break | Caught by |
| --- | --- |
| Grid built at Z=5 instead of Z=0 | `EveryLineLiesOnTheZ0PlaneInsideTheBed` (3 cases) and — after the framing was tightened from 6 mm/px to 0.85 mm/px, which it needed — `TheGridSitsOnWorldZ0_WhereverTheCameraIsPointed` |
| Plate drawn after the mesh with the depth test off | `TheModelIsDrawnOverThePlate_NotUnderIt` |
| Minor intensity back to 0.35 | `TheMinorGridForATinyModel_IsActuallyVisibleAgainstTheBackground` |
| `RefreshViewMenuChecks` removed, handler reading `IsChecked` | `AShortcutMovesTheCheckMark_NotJustTheViewport`, `TheShortcutTogglesThePlate_NotJustTheMenuItem`, `HidingThePlate_TakesTheWarningWithIt` |

The Z=0 GPU test is worth calling out: at the whole bed's framing, one pixel is
6.4 mm, and a plate drawn 5 mm off passed it. It only became a real test once the
camera was framed tightly enough for the error to exceed the tolerance.

## Decisions recorded in §11 (2026-09-06)

- The plate is drawn at true size and **never scales to the model**; the spacing adapts in two tiers instead.
- The bed is **centred on the origin in X and Y** and rises from Z=0.
- The plate renders **before the mesh, depth-tested and depth-writing**, and is **unlit** — the mesh pass's headlight does not carry over, because a line has no surface to shade.
- Bed size is **configurable by preset and held in-session**; a custom size waits for `settings.json`.
- A dim UI element needs a **contrast floor**, and the test for one must measure contrast, not difference.
- Avalonia's `HotKey` activation does not update `IsChecked`; View menu check marks are now derived from live state.

## Not touched

Backlog items 22, 24, 26 and 27 are untouched, as instructed. In particular the
Menger sponge screenshots show 0 issues throughout because nothing here cuts or
splits it.

## Files

- `src/Meshwright.Geometry/Printing/BuildVolume.cs` — the bed, its box, and the presets
- `src/Meshwright.Geometry/Printing/BuildPlateFit.cs` — the out-of-bounds test and its plain-language message
- `src/Meshwright.Geometry/Printing/BuildPlateGrid.cs` — the two-tier spacing rule and the line geometry
- `src/Meshwright.Rendering/GL/BuildPlateRenderer.cs` — the unlit line pass
- `src/Meshwright.App/Views/MeshViewportControl.cs` — third render pass, before the mesh
- `src/Meshwright.App/MainWindow.axaml{,.cs}` — the menu, the warning text, and `RefreshViewMenuChecks`
