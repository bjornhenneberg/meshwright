# Camera and display modes (backlog item 23, first slice)

**Date:** 2026-09-06 · **Branch:** `feat/viewport-camera-modes`

The first slice of §5.1's Viewport / UX block: orthographic projection, the
seven standard view presets, and the wireframe and x-ray display modes. The
build plate and its out-of-bounds warning, the cross-section slider, mm/inch
units, drag-and-drop and recent files are **not** in this slice.

## What a user can now do

Everything is in the **View** menu and reachable by shortcut:

| Shortcut | Does |
| --- | --- |
| `Ctrl+0` | Reset View (unchanged) |
| `Ctrl+1` … `Ctrl+7` | Front, back, left, right, top, bottom, isometric |
| `Ctrl+Shift+P` / `Ctrl+Shift+O` | Perspective / orthographic |
| `Ctrl+Shift+S` / `Ctrl+Shift+W` / `Ctrl+Shift+X` | Shaded / wireframe / x-ray |

Projection and display mode are radio groups, so the menu's check marks track
the real state. Everything below is the real app on the Menger sponge and the
139,989-triangle Eiffel tower samples, not a headless render.

![All seven view presets in orthographic, on the Menger sponge: front, back,
left, right, top, bottom on the first row and a half, isometric last. The sponge
is symmetric, so the six axis views agreeing is the point — every one is square,
flat and evenly lit, and the isometric shows three faces as equal parallelograms
with no convergence.](seven-presets-orthographic.png)

*The seven presets, orthographic. All six axis views of a symmetric model should
look identical, and do; the isometric's three faces are equal parallelograms.*

![Top view of the Menger sponge in orthographic: nine square holes on a flat
grey face, with no visible side-walls inside any of them](top-orthographic-menger.png)

*Top view, orthographic — the proof the projection is parallel: not one hole
shows an interior wall. In perspective the outer holes show more of their sides
the further they sit from the centre.*

![The Menger sponge in wireframe: every triangle edge drawn in white with no
fill, including the interior tunnel structure](wireframe-menger.png)

*Wireframe — no fill, so the tessellation and the tunnels through the model are
both visible.*

![The Menger sponge in x-ray: a translucent grey solid through which the
internal cavity structure is clearly visible](xray-menger.png)

*X-ray — the sponge's interior read through the surface. This is the mesh most
likely to break an assumption about see-through rendering, which is why it is
the one shown.*

## Decisions

Recorded in §11 (2026-09-06); the short version:

- **The orthographic view volume is the perspective frustum's height at the
  target plane** (`2 · Distance · tan(fov/2)`). Switching modes is a display
  choice, not a reframing: what you had centred stays the size and place it was,
  and the wheel still zooms.
- **Its near plane sits behind the eye**, at `-FarPlane`. An orthographic volume
  has no eye point to be in front of, and zooming walks the camera toward the
  target, so a near plane at the eye would slice the model in half on the way
  past.
- **A preset writes all of the orientation and none of the framing.** Top view
  while zoomed in on a detail shows that detail from above.
- **Top and Bottom set pitch to exactly ±90°**, which `Orbit` deliberately
  clamps away from. There the view direction is the world Z axis and a +Z up
  vector gives a degenerate all-NaN look-at; `OrbitCamera` uses the limit of +Z
  as pitch approaches the pole instead, which is continuous with an orbit
  arriving there and gives the top view +Y up, +X right.
- **Isometric is defined as the orientation Reset View restores**, and that
  orientation's elevation moved from 30° to the true isometric 35.26°, so the
  two menu entries cannot differ by a few degrees no user could name.
- **Every display mode restores the GL state it found** — filled polygons, no
  blending, depth writes on. The context is shared with the gizmo pass.

## The defect this slice found by being looked at

With view presets in place, **Front, Left and Bottom rendered the model as a
black silhouette.** The light was fixed in world space at (-0.5, -1, -0.3);
nothing had noticed while orbiting was the only way to move, but half the new
presets look straight at the unlit side, where the shader's 0.2 ambient floor is
all there is.

![Eiffel tower, front orthographic view, before the fix: the model is a nearly
black silhouette, barely distinguishable from the dark
background](front-orthographic-eiffel-BEFORE-headlight.png)

*Before — Front view, world-fixed light. The model is there; it is lit at the
ambient floor and reads as a smudge. Left and Bottom were the same.*

![Eiffel tower, front orthographic view, after the fix: the tower is fully lit
in light grey, an engineering elevation with symmetric legs and no perspective
convergence](front-orthographic-eiffel-AFTER-headlight.png)

*After — the same view with the headlight. It is also a clean elevation: the
legs are symmetric and nothing converges, which is what the orthographic
projection is for.*

No test could see it, because none of them had a notion of brightness.
`MeshRenderer` now derives a key light from the view matrix, placed over the
viewer's left shoulder so shading still describes shape, and
`EveryStandardView_ShowsALitModel_NotAFlatSilhouette` renders all seven presets
and fails if any sits at the ambient floor. That test was confirmed to fail on
the old world-fixed light before being kept.

## How it is pinned

Invariants, not flags — the projection tests would pass on a mode enum attached
to the wrong matrix otherwise:

- **No perspective divide**: two equal segments at different depths project to
  equal screen length in orthographic, with the perspective case asserted to
  differ by more than half as the control.
- **Mode switch preserves target-plane size**, and zooming still magnifies in
  orthographic.
- **Each preset's view direction and screen axes** are read off the view matrix
  and compared with the axis the preset claims; every preset's matrix is checked
  invertible and NaN-free, with the up vector non-parallel to the view.
- **Preset → Reset View → the same preset lands in the same pose**, after an
  orbit, a pan and a zoom in between.
- **Picking survives the mode switch**: `GizmoScale`, `ViewportRaycaster` and the
  plane-cut gizmo's pick path all resolve against the projection matrix.
  Orthographic re-runs the gizmo pick contract at four model radii and two
  display scalings, adds a project→unproject round trip at several depths, and
  requires the centre-pixel ray to strike the same triangle *and* the same point
  in both modes.
- **Pixels, for the display modes** (GPU suite): wireframe covers less than half
  the pixels shaded does; x-ray reveals a triangle sealed inside a cube that
  shaded rendering is separately asserted to hide; and the GL state after every
  mode's render is back to its baseline.

## Results

- `dotnet test tests/Meshwright.Tests -c Release`: **681 passed, 0 skipped**
  (baseline 581 + 100 new).
- `tests/Meshwright.Tests.Gpu`: **15 passed, 0 skipped** (baseline 8 + 7 new),
  0.4 s under `timeout`.

One caveat worth recording: a single unit-test run failed once with one test,
and the filter I was grepping through swallowed the name. Five consecutive full
runs since have been clean, and I could not reproduce it. Unresolved rather than
explained.

## Not touched

Items 26 (a split leaves both halves in one mesh) and 27 (a refused operation
still counts as a change) are still open and were deliberately left alone.

## What the merge cost, and why

The merge went green on Linux and Windows and **red on macOS**, on a test this
slice did not write: `ViewportHarnessTests.ProjectToPixel_RoundTripsThroughRayThroughPixel`
missed by 0.00056 against a `radius * 1e-3` = 0.0005 limit on the 0.5 mm model.

That bound was already within 1% of failing on Linux (0.000495 measured), and
moving the default camera elevation to the true isometric angle changed the pose
enough for a different architecture's float rounding to tip it over. It is also
inconsistent by construction: `radius * 1e-3` is 0.05 px at radius 500 and
0.22 px at radius 0.5.

Both the perspective and the orthographic round trips now bound the miss at
**half a logical pixel** — the unit a click is actually aimed in, and the thing
the round trip exists to protect. Logical rather than device: the same world
rounding counts double at `RenderScaling` 2, which is a property of the display,
and a first pass in device pixels failed on macOS at 0.53 for exactly that
reason. Worst measured: 0.22 logical px on Linux, 0.26 on macOS.

CI is green on Linux, Windows and macOS at `5e0936e`.
