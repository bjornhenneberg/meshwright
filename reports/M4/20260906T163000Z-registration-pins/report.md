# Registration pins on plane-cut faces (backlog item 25)

**Date:** 2026-09-06 · **Branch:** `feat/registration-pins`

A peg-and-socket pair on the two mating faces of a plane-cut split, so the
halves align and stay together when they are put back together. One shape,
configurable diameter and clearance; the dovetail / finger-joint / magnet-pocket
catalogue stays in v1.x.

## What it does

`PlaneCut.Cut` takes an optional `RegistrationPinOptions` (diameter, clearance,
depth, centre). In `CutMode.Split` with a cap it produces:

- **Peg half** (positive side): the cap is punched out at the peg diameter and a
  cylinder of that diameter, one depth long, grows out of the hole with a flat
  end disc.
- **Socket half** (negative side): the same cap punched out at the peg diameter
  *plus twice the clearance*, and a bore of that diameter sunk one depth *plus*
  one clearance into the material.

`PlaneCutResult.Pin` reports what was built, every "achieved" figure measured off
the finished geometry.

## The two requirements this feature was promoted on

**Generated, not booleaned.** The pin circle joins the cut cross-section as one
more loop, so `CutCrossSection`'s existing parity nesting punches it out of the
cap the same way it punches out a tunnel through a Menger sponge. A cylinder wall
and an end disc are then stitched onto the boundary that leaves. Nothing is
intersected against anything.

Measured, not assumed:

| Mesh | Triangles | Unpinned split | Pinned split |
| --- | --- | --- | --- |
| `Menger_sponge_sample.stl` | 2,112 | 63 ms | 89 ms |
| `Eiffel_tower_sample.STL` | 139,989 | 290 ms | 318 ms |

The ~28 ms difference on the large mesh is almost entirely the AABB tree built
for the break-out check. The complaint behind the promotion was a boolean union
that had run for twenty minutes.

**A round trip.** Clearance goes on the socket alone: the peg prints at the
diameter that was asked for, and the hole it drops into is the one that grows.
The socket is also one clearance deeper than the peg is long, so the mating faces
meet flush rather than the peg bottoming out.

## Design points that were decided rather than guessed

- **Where the pin goes.** Automatically, at the cross-section's *pole of
  inaccessibility* — the point furthest from any of its edges. A centroid is
  meaningless on a cut that is sixteen disjoint squares (a level-2 sponge at its
  centre plane) and lands in a void. Inside-ness uses the same even-odd parity
  rule as the cap, so a pin cannot land where the cap left a hole.
- **How clearance is applied.** Radially to the socket bore and axially to its
  depth; the peg is exactly as requested.
- **Too small to host a pin.** Refuse the whole cut with the mesh untouched, and
  name the largest diameter that fits. Three refusal paths: the cross-section is
  too thin; the socket would bore out through the model's own wall below the cut;
  the mode is Keep/Discard or the cut is uncapped, so there is no mating face.

## Verification

581 unit tests (562 baseline + 19 new), 0 skipped; GPU suite 8 passing.

Invariants asserted, measured before and after — never existence:

- peg and socket coaxial, mating with **exactly** the requested clearance
  (Ø4.000 / Ø4.400 measured, 0.200 clearance, 5.000 depth);
- volume up on the peg half by the 32-gon prism's exact area × length, down on
  the socket half by its own, to six decimal places, with the ratio bounded;
- both halves closed shells, no new self-intersections, bounding box unmoved;
- the diameter a refusal names actually fits when retried;
- on the Menger sponge, the pin lands in material, the socket axis stays inside
  the solid at every depth, and neither half gains an issue the unpinned cut did
  not already have;
- a pinned cut costs the same order as an unpinned one and adds a bounded number
  of triangles.

Panel wiring is asserted from typed field to measured mesh: typing `6` puts a
3.000 mm-radius peg end in the document, typing `3` puts a 1.500 mm one there,
and a blank depth means one diameter. The diameter box is watched on
`TextProperty`, not `TextChanged`.

## In the running app

Verified live on `Menger_sponge_sample.stl` (2 mm cube, 2,112 triangles):

- Ø4 mm pin on a 2 mm model — *"The cut cross-section is too thin to host a Ø4 mm
  pin with 0.2 mm clearance at any diameter"*, Before and After both 2112 / 4.39.
- Pin placed by clicking the plane gizmo in the viewport — *"Pin placed at
  (-0.3, -0, 0), Ø0.4 mm"*, the diameter coming from the textbox, and the amber
  pin circle drawn on the plane at that point.
- Applying with the pin in a void — *"The requested pin position is not inside the
  cut cross-section"*, mesh untouched.
- Ø0.1 mm with 0.01 mm clearance at automatic placement — *"Split into two shells
  with 260 cap triangles (2112 -> 2820 triangles, 1410 on the negative side).
  Registration pin: Ø0.1 mm peg 0.1 mm long on the positive half, Ø0.12 mm socket
  0.11 mm deep on the negative half (0.01 mm clearance)."*

## Fixed on the way

Gizmos were depth-tested against the mesh, so a gizmo *inside* the solid drew
nothing — which is every plane cut gizmo, since it is anchored at the mesh centre
and sized to a tenth of the viewport. `MeshViewportControl`'s comment said
"render active gizmo on top of the mesh" while leaving `DepthTest` enabled. Found
by placing a pin in the real app and seeing nothing while the panel correctly
reported the placement.

## Found on the way, not fixed

Two new backlog items (26 and 27), both pre-existing and neither caused by pins:

- A split leaves both halves in one mesh with coincident cut faces, so Inspect
  reports 1,808 issues on a clean sponge that measures issue-free one half at a
  time.
- A refused operation still raises `Changed`, so it pushes an undo entry and
  resets every gizmo — a user whose pin will not fit loses the pin they placed.
