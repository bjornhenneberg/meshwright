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
the real state. All of it was exercised in the running app on the Menger sponge
and the 139,989-triangle Eiffel tower samples — see the screenshots beside this
file.

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
all there is. See `front-orthographic-eiffel-BEFORE-headlight.png` against
`front-orthographic-eiffel-AFTER-headlight.png`.

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
