# M4 — Broad UX audit of the real GUI (investigation only)

- **Date (UTC):** 2026-09-05T22:17Z — 2026-09-06T00:5xZ
- **Commit:** `2200fb50e6c4d934fb0c3f1bf13b3e3f80e22510`, branch `main`, clean tree
- **Build:** `dotnet build -c Release` — success
- **Method:** real GUI on `DISPLAY=:0`, synthetic input via `libXtst`, screenshots via `gnome-screenshot -w`.
  Coordinate mapping calibrated per session: `screen = shot + (90, 7)` with the window at `wmctrl -e 0,90,80,1740,1000`.
- **Meshes:** `samples/broken-cube.stl` (14 tris), `~/Downloads/Menger_sponge_sample.stl` (2112 tris, bounds -1..1, true volume 4.3896),
  `~/Downloads/Eiffel_tower_sample.STL` (139,989 tris).
- **No source file was modified.**

Ground truth for the sample meshes was computed independently from the STL binaries
(triangle count, min/max, signed volume) and used as the invariant against which the
app's Diagnostics panel was checked.

---

## Confirmed findings, ranked by user impact

### 1. Drain Holes deletes whole triangles instead of drilling, and reports success
`32-drain-hole-placed.png`, `33-drain-applied.png`, `34-crop.png`, `35-vp.png`

Steps: load `samples/broken-cube.stl`, Auto Repair (gives a clean 2x2x2 mm cube: 12 tris,
8 verts, 1 shell, volume 8, area 24, 0 issues). Drain Holes tab -> Diameter `0.5` ->
"Place holes with gizmo" -> click one face -> "Apply to all holes".

| invariant | before | after | expected for a Ø0.5 mm bore |
| --- | --- | --- | --- |
| triangles | 12 | **10** | many more (bore walls) |
| vertices | 8 | **8** | many more |
| surface area | 24 | **20** (exactly one 2x2 face) | slightly more than 24 |
| volume | 8 | **6.667** (open-mesh integral, meaningless) | ~7.61 |
| issues | 0 | **1 boundary hole, 4 edges** | 0 |

Reported as: *"Applied 1 drain hole(s). Last: Placed drain hole (Ø0.5mm, removed 2 triangles)."*
The actual opening is 2 mm x 2 mm — 16x the area of the requested circle — and the model is
left open and unprintable. `35-vp.png` shows the missing face after orbiting.

Root cause — `src/Meshwright.Geometry/Edit/DrainHole.cs:118-141`: every triangle whose distance
to the hole centre is `< diameter/2` is removed and **no geometry is added**. The result returns
`DiameterAchieved: diameter` — the *requested* value passed straight through — so the reported
diameter can never disagree with the request, despite `DrainHoleResult.DiameterAchieved`'s doc
comment (line 12) promising "Actual hole diameter achieved... may be slightly smaller than
requested due to mesh discretization". `DepthDrilled = diameter * 2.0` (line 130) is a
hard-coded heuristic, not a measurement.

The class comment (lines 30-33) acknowledges the simplification ("The hole walls are the
remaining mesh geometry — not perfectly cylindrical, but acceptable for v1.0"), but nothing
that reaches the user says the model has been opened.

### 2. Countersink Depth is inert but reported as applied
`37-countersink-placed.png`, `39-crop.png`, `39-diag.png`

Same placement with Countersink Depth = `1.0` produced a **byte-identical result**
(10 tris, 8 verts, volume 6.667, area 20, 1 boundary hole) yet reported
*"Placed drain hole (Ø0.5mm, 1mm countersink, removed 2 triangles)."*

`countersinkDepth` appears in `DrainHole.cs` only at lines 54, 64, 71, 73 and 141 — parameter,
validation, and echo into the result record. It never changes geometry. The algorithm comment at
line 33 describes a chamfer step ("If countersink is requested, apply a shallow chamfer by
tapering the hole diameter") that does not exist.

### 3. The "Add Cap" checkbox does nothing
`04-planecut-tab.png`, `05-planecut-nocap-scrolled.png`, `06-planecut-nocap-applied.png`, `07-crop.png`

Menger sponge, Plane Cut, Keep Positive Side, cap type Planar, **Add Cap unchecked**, Apply.

Result: a fully closed solid — 2112 -> 1280 triangles, volume exactly **2.195** (half of 4.39),
1 shell, **0 issues**, and the panel's own message reads *"Cut plane kept positive side with
**96 cap triangles** (2112 -> 1280 triangles)"*.

`src/Meshwright.App/Views/Edit/PlaneCutPanel.axaml.cs:230` reads
`bool addCap = AddCapCheckBox?.IsChecked ?? true;` and the variable is never used again
(it is the only occurrence of `addCap` in `src/`). None of `PlaneCutKeepSideOperation`,
`PlaneCutDiscardSideOperation`, `PlaneCutSplitOperation` accepts an uncapped option — their
constructors take `(planePoint, planeNormal, capMode)` only. §5.1 requires "cut with **optional**
cap"; the optionality is unreachable.

### 4. Decimation creates self-intersections while claiming it stopped to avoid invalid geometry
`12-crop.png`, `13-decimate-applied.png`

Menger sponge (clean, **0 issues**) -> Decimate -> Triangle Count, target 100 -> Apply.

| invariant | before | after |
| --- | --- | --- |
| triangles | 2112 | 734 |
| volume | 4.39 | 3.147 |
| issues | **0** | **67 self-intersections** |

Panel message: *"Reduced from 2112 to 734 triangles (34.8% of original). Short of the
100-triangle target: **further collapses would have created invalid geometry**. Repairing the
mesh first usually allows a deeper reduction."*

The mesh had nothing to repair beforehand, and the operation itself produced the invalid
geometry it says it stopped short of creating. The status bar simultaneously and correctly
reads "67 issues found", so the panel directly contradicts the rest of the app. The honest
shortfall reporting from the 2026-09-05 decision-log row does hold; the *reason* it gives is false.

### 5. The Transform panel reports half the model's true size
`09-transform-tab.png`, `10-crop.png`

Menger sponge loaded. Diagnostics: "Bounds: 2 x 2 x 2" (correct — ground truth is -1..1 on every axis).
Transform panel: *"Before: 2112 tris, 4.39 mm³, **bounds: 1 x 1 x 1**"*.

All four format sites in `src/Meshwright.App/Views/Edit/TransformPanel.axaml.cs`
(lines 250-252, 315-317, 323-325, 477-479, 485-487) pass `bounds.Extents`.
`src/Meshwright.Geometry/Vendor/g3/math/AxisAlignedBox3d.cs:119-122` defines `Extents` as
**half** the box size; `Diagonal` (line 115) is the full size. `MainWindow.UpdateDiagnosticsPanel`
correctly uses `BoundingBox.Width/Height/Depth`.

This is the panel a user scales and moves in, on a tool whose job includes deciding whether a
part fits a print bed.

### 6. The Hollow panel's gizmo status lies from startup
`26-crop.png`

With the Hollow gizmo never activated and the Wall Thickness textbox reading `2.0`, the panel
displays *"Wall thickness set via gizmo: 0.3mm"*.

`HollowPanel.axaml.cs:41-46` — `SetGizmo` calls `UpdateGizmoStatusDisplay()` unconditionally at
wire-up time, and that method (lines 97-101) always writes "Wall thickness set via gizmo: {N}mm"
regardless of `_gizmo.WasTouched`. The 0.3 is `HollowGizmo.ComputeDefaultWallThickness(mesh)`.

Apply does correctly use the textbox value (`WasTouched` is false), so the *value* is right and
only the message is wrong — but the message names a different number from the one that will be
used, and the project's own gizmo-first rule (§11, 2026-09-04: "a touched gizmo's values win
outright on Apply") gives the user every reason to believe 0.3 mm is what they will get.
`PlaneCutPanel.SetGizmo` does **not** have this bug (it only subscribes).

### 7. Placed drain holes are listed and drawn at a hard-coded 2 mm
`32-crop.png`

With Diameter set to `0.5` **before** placing, the Placed Holes list reads
*"Hole 1: Ø2mm @ (-0.1, 1, -0)"*.

`src/Meshwright.App/Gizmos/DrainHoleGizmo.cs:166-170` constructs every hole with
`diameter: 2.0, // Default 2mm`, ignoring the panel field; `DrainHolePanel` only overwrites
`hole.Diameter` at Apply time (lines 197, 257). The viewport marker is drawn at
`hole.Diameter / 2.0 * 0.3f` (line 125) — a fudge factor, not the true radius.

So in a deliberately gizmo-first UI, drain-hole size is the one parameter the gizmo never shows,
and the list actively shows the wrong number.

### 8. Auto Repair reports flipping more triangles than the mesh contains
`21-autorepair-brokencube.png`, `22-crop.png`

broken-cube -> Auto Repair -> *"Removed 1 small disconnected shell (4 triangles). Filled 1 hole
(planar), adding 2 triangles. Unified normals: **flipped 13 triangles** across 1 shell."*
The mesh has **12** triangles at that point (and after).

`src/Meshwright.Geometry/Repair/NormalUnificationRepair.cs`: `UnifyShell` adds the per-triangle
flips from `MakeWindingConsistent` (line 46, here 1) and then, if the shell came out
inward-facing, adds `triangleIds.Length` again for the global re-flip (line 63) — so a triangle
flipped twice is counted twice and the total can exceed the triangle count.

The repair itself is correct: 2 shells -> 1, 5 issues -> 0, cube restored to 2x2x2 / volume 8.

### 9. Decimate's unit label never changes
`14-crop.png`

Switching Mode to "Percentage" leaves the unit label reading "triangles", so the UI presents
"Target: `100` triangles" for a 100-*percent* target (live preview correctly says
"Target: 734 triangles (100% of current)"). `DecimatePanel.axaml.cs` only ever assigns
`TargetUnitLabel.Text = "triangles"`, in `UpdateLivePreview`'s no-mesh branch.

### 10. Panel result messages are never cleared on file load or undo
`19-eiffel-t60` / `eif60.png`, `46-crop.png`

After opening the Eiffel tower, the Decimate panel still read *"Reduced from 2112 to 734
triangles..."* from the Menger sponge. Two file loads later, with a 12-triangle cube loaded, the
Plane Cut panel still read *"Cut plane kept positive side with 96 cap triangles
(2112 -> 1280 triangles)"*. The live stats (`Before:`/`After:`, live preview) do refresh
correctly via `MeshDocument.Changed`; only the italic result lines are stale.

### 11. "Drop to Z=0" is an alias for "Align to Bed", and announces itself as one
`10-crop.png`

`TransformPanel.axaml.cs:497-503` — `OnDropToZ0Click` calls `OnAlignToBedClickCore()` with the
comment `// DropToZ0 is an alias for AlignToBed`. §5.1 lists them as separate operations
("align to bed, drop to Z=0"); in print tooling "align to bed" normally implies orienting a face
down, "drop to Z=0" a pure translation. Both here are the translation.

Clicking **Drop to Z=0** on a mesh spanning z = -1..1 printed
*"Aligned to bed: moved down by -1 mm so lowest point is at Z=0"* — the other button's name, and
a move *up* described as a move down by a negative number
(`AlignToBedOperation.cs`: `$"Aligned to bed: moved down by {minZ:0.##} mm..."`).

The translation itself is correct: exported the result and confirmed min z = 0.0, max z = 2.0,
volume 4.38957 and 2112 triangles preserved.

### 12. Hollow takes ~20 s on a 12-triangle cube
`27-hollow-applied.png`, `28-hollow-wait30.png`, `29-hollow-t01..t40.png`

Hollow with wall thickness 2.0 on the 2x2x2 mm, 12-triangle cube. Frames captured at
t = 1, 3, 8, 15, 25, 40 s: still "Working: Hollow..." at 15 s, finished by 25 s.

The outcome is exemplary and honest — *"Could not hollow to 2mm wall thickness — the mesh has no
interior that thick anywhere (too thin/small for this request). Mesh left unchanged."* with the
mesh bit-identical (12 tris, volume 8) — and the busy indicator behaves exactly as §11 item 13
documents (indeterminate bar, "Working: Hollow...", Cancel disabled). But the cost is independent
of mesh complexity and the user waits ~20 s to be told nothing happened, which is a "fixed in one
click" (§4) problem rather than a correctness one.

---

## §5.1 items with no reachable control

Same shape as the M2 Repair-UI gap recorded on 2026-09-05. Verified by reading
`MainWindow.axaml` (the whole View menu is one `Reset View` item) and by
`grep -rniE 'wireframe|xray|x-ray|buildplate|crosssection|orthographic|viewpreset|DisplayMode|RecentFile|inch' src/`,
which matches nothing outside incidental comments.

| §5.1 item | status |
| --- | --- |
| Display modes: shaded / wireframe / x-ray / error-highlight | only shaded + error-highlight exist, and neither is switchable; no wireframe or x-ray code anywhere |
| Orthographic projection | `OrbitCamera.GetProjectionMatrix` (line 118) only ever returns `CreatePerspectiveFieldOfView`. `GizmoScale` has an orthographic branch that cannot be reached from the UI |
| Standard view presets (front/top/right/...) | absent; View menu has only Reset View |
| Build plate grid with configurable printer size | absent |
| Out-of-bounds warning | absent — and the Diagnostics "Bounds" line gives dimensions only, never min/max, so nothing in the UI says where the model sits relative to Z=0 |
| Cross-section preview slider | absent |
| Recent files list | absent (acknowledged in §11, 2026-09-04 — no settings persistence) |
| Drag-and-drop file open | absent — no `DragDrop` handler in `MainWindow` |
| Unit handling (detect/assign mm vs inch, scale on import) | absent; several labels hard-code "mm"/"mm³" for numbers that are simply file units |

---

## Areas checked and found sound

- **Import.** Uppercase `Eiffel_tower_sample.STL` is listed in the Open dialog and opens — the
  2026-09-05 file-picker case-variant fix holds (`17-crop.png`). 139,989 triangles loaded and
  fully diagnosed in **under 1.6 s**, comfortably inside §6.4's 3 s budget (`19-eiffel-t1.png`).
- **STL export round-trip.** Header `Meshwright binary STL export`, 2112 triangles, exact bounds
  and volume, positive signed volume (correct winding). Used as the ground-truth check on Drop to Z=0.
- **Diagnostics accuracy.** App figures match independently computed ground truth exactly
  (Menger: 2112 tris / 4.3896 / 2x2x2 vs app 2112 / 4.39 / 2x2x2). Plain-language summaries are
  genuinely plain: *"2857 non-manifold edges, 126 holes, 31780 self-intersections, 46 flipped
  faces, 41 degenerate triangles, 1858 duplicate vertex locations found."*
- **Auto Repair on broken-cube.** Correct and honestly itemised (bar the flip count in finding 8):
  14 -> 12 triangles, 2 -> 1 shell, 5 -> 0 issues, cube restored to 2x2x2 / volume 8.
- **All four individually-runnable repair operations reachable on a clean mesh** — Fill Holes,
  Unify Normals, Resolve Self-Intersections, Remove Small Shells — each reported
  *"...: nothing to change."* and left the mesh bit-identical (`41-*`, `43-sum.png`, `44-sum.png`,
  `45-sum.png`). Notably **Remove Small Shells did not eat the only shell** at threshold 0.01.
- **Plane cut geometry, including the multi-loop cap.** Cutting the Menger sponge at z=0 gave
  volume exactly 2.195 (half of 4.3896), 1 shell, **0 issues** — the 2026-09-05 parity-nesting cap
  fix holds on the mesh that originally broke it.
- **Booleans.** cube (volume 8) − Menger sponge (4.39) = **3.61** exactly, 1404 triangles, 1 shell,
  0 issues (`25-crop.png`, `25-diag.png`). Manifold interop is working. Apply is correctly
  disabled until a secondary mesh is loaded.
- **Hollow's refusal path.** Honest and non-destructive (see finding 12).
- **Undo/redo.** Correct across operations and file loads; four Ctrl+Z then two Ctrl+Y behaved
  consistently and the status line tracks "Undo/Redo available" (`49-*`, `50-*`). Viewport,
  diagnostics and panel stats all refresh from the single `MeshDocument.Changed` subscription.
- **Busy indicator.** Indeterminate bar + "Working: <op>..." + disabled Cancel, exactly as §11
  item 13 describes for opaque operations.
- **Gizmos.** The plane-cut gizmo renders, hit-tests, drags and reports live values
  (`47-vp.png`, `48-vp.png`, `48-status.png`: *"Plane set via gizmo: point (0, 0, 0),
  normal (-0.23, 0.02, 0.97)"*). The drain-hole gizmo picks the surface accurately.
- **Reset View** exists on the toolbar and in the View menu with Ctrl+0.

---

## Suspected (code reading only — not observed failing)

- **Uncapped, non-virtualised issues list.** `MainWindow.axaml.cs:580` does
  `IssuesList.ItemsSource = report.Issues.Select(...).ToList()` into a plain `ItemsControl`,
  which does not virtualise by default in Avalonia — 36,708 `TextBlock`s for the Eiffel tower.
  Load stayed fast so no stall was observed; scrolling that list was not tested.
- **Open-mesh "Volume" reported without caveat.** broken-cube shows "Volume: 5.329" and the
  drain-holed cube "Volume: 6.667". The divergence-theorem integral over an open surface is not a
  volume. In finding 1 it was the only number that would have warned the user the model had been
  opened. Unverified whether this is a deliberate choice.
- **No-op operations may consume undo entries.** Four Ctrl+Z after four "nothing to change"
  operations all stayed at 12 triangles; indistinguishable from correct behaviour on a mesh that
  never changed.

---

## Proposed §11 decision-log wording (for the dispatcher to land — not written by this agent)

> | 2026-09-06 | A control that reads a value it never uses is worse than a missing control: it is a promise the app breaks silently. A UX audit of the real GUI found three — Plane Cut's "Add Cap" checkbox (parsed into a local and discarded; no cut operation has an uncapped path at all, so §5.1's "optional cap" is unreachable), Drain Holes' "Countersink Depth" (validated, echoed into the result summary as "1mm countersink", never used to change geometry), and the drain-hole gizmo's diameter (hard-coded to 2.0 at placement, so the Placed Holes list and the viewport marker both describe a hole the user did not ask for). Each shipped behind a green suite because the tests drive the operations directly and never assert that a UI control changes the operation's output. |

> | 2026-09-06 | Drain Holes does not drill; it deletes whole triangles inside the requested radius and adds nothing. On a coarse mesh a Ø0.5 mm request removed a full 2 x 2 mm face — 16x the requested area — leaving the model open (0 issues -> 1 boundary hole, surface area 24 -> 20, vertex count unchanged at 8) while reporting "Applied 1 drain hole(s)... (Ø0.5mm, removed 2 triangles)". The honesty failure is one line: `DrainHoleResult.DiameterAchieved` is documented as the achieved diameter but returns the *requested* one verbatim, so it can never disagree with the request, and `DepthDrilled` is `diameter * 2.0` — a constant dressed as a measurement. A field named for what was achieved must be measured from the result, never copied from the input. |

> | 2026-09-06 | Decimation names the target it missed (correct, per 2026-09-05) but the reason it gives is false: reducing the clean Menger sponge to 734 triangles introduced 67 self-intersections while the summary said further collapses "would have created invalid geometry". A quality-collapse loop that refuses individual edge collapses on a local validity test still has to be checked against the whole-mesh invariants afterwards — the app's own status bar reported the 67 issues in the same frame the panel denied them. |
