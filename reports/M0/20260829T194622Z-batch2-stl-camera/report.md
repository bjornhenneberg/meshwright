# M0 Batch 2 Verification — STL Reader + Orbit Camera

Verified: 2026-08-29 (UTC timestamp of this report: 20260829T194622Z)
Scope: `src/Meshwright.IO/Stl/StlReader.cs`, `src/Meshwright.Geometry/TriangleMesh.cs`,
`src/Meshwright.Rendering/Camera/OrbitCamera.cs`, plus their xUnit tests
(`tests/Meshwright.Tests/Stl/StlReaderTests.cs`, `tests/Meshwright.Tests/Camera/OrbitCameraTests.cs`).

## Check 1: `dotnet build` / `dotnet test` — full solution, non-flaky

**Result: PASS**

- `dotnet build` from repo root: **Build succeeded, 0 Warning(s), 0 Error(s)**
  (all 6 projects — Geometry, Rendering, IO, Core, App, Tests — built).
  Full output: [build.log](build.log).
- `dotnet test --no-build` run **3 times in a row** to check for the transient
  failure one implementer previously reported:
  - Run 1: `Passed! - Failed: 0, Passed: 17, Skipped: 0, Total: 17`
  - Run 2: `Passed! - Failed: 0, Passed: 17, Skipped: 0, Total: 17`
  - Run 3: `Passed! - Failed: 0, Passed: 17, Skipped: 0, Total: 17`
  - Consistent across all 3 runs — the previously-reported `StlReaderTests`
    failure does not reproduce now that both implementers are done. No flake
    observed in this sample.
  - Full output: [test.log](test.log).

## Check 2: `StlReader.cs` / `TriangleMesh.cs` — binary/ASCII STL parsing correctness

**Result: PASS**

Read the full source of both files (not just the tests) and checked against
the standard binary/ASCII STL layout:

- **Binary format**: 80-byte header, `uint32` little-endian triangle count at
  offset 80, then per-triangle records of `12 floats (normal + 3 verts) * 4
  bytes + 2-byte attribute count = 50 bytes`. Code's
  `BinaryTriangleRecordSize = 12 * sizeof(float) + 2` and `HeaderSize = 80`
  match this exactly. `ReadVector3` reads three little-endian
  `float`s via `BitConverter.ToSingle` at the correct offsets, in the correct
  order (normal, then vertex A/B/C), and the 2-byte attribute byte count is
  correctly skipped and unused. This matches the format.
- **Binary/ASCII detection heuristic**: `LooksBinary` reads the triangle count
  at offset 80 and checks whether `80 + 4 + count * 50` equals the total
  buffer length exactly. This is the standard heuristic used by libraries
  like Assimp for disambiguating STL variants (ASCII files almost never
  happen to match this length), and is a reasonable, correct choice for M0.
  If the buffer is too short to even hold a header + count, it correctly
  falls through to ASCII parsing (which will then raise a clear error).
  `ReadBinary` re-validates the expected length against the actual buffer
  length before reading and throws `InvalidDataException` on mismatch
  (guards against truncated files), rather than trusting the earlier
  heuristic check blindly.
- **ASCII format**: whitespace-tokenizes the whole buffer and drives a small
  recursive-descent-style parser over `solid [name] ... facet normal nx ny nz
  outer loop vertex x y z (x3) endloop endfacet ... endsolid [name]`. Tokens
  are matched case-insensitively (per spec, keywords are lowercase but this is
  lenient), floats parsed with `CultureInfo.InvariantCulture` (avoids
  locale-dependent decimal separator bugs). Requires exactly 3 vertices per
  facet and throws `InvalidDataException` on any grammar violation, missing
  `solid` keyword, or truncated input. This matches the ASCII STL grammar.
- **`TriangleMesh`**: flat non-indexed triangle soup, validates
  `positions.Length % 3 == 0` and `normals.Length == positions.Length / 3` in
  the constructor. Simple and matches the "M0 stopgap" comment's stated
  scope.

No correctness issues found by reading the code independently of the tests.

## Check 3: `OrbitCamera.cs` — pitch clamping, orbit/pan/zoom, dependency scope

**Result: PASS**

- **Pitch clamping**: `MinPitch = -π/2 + 0.01`, `MaxPitch = π/2 - 0.01`,
  applied via `Math.Clamp` inside `Orbit()`. This keeps pitch strictly inside
  the open interval `(-π/2, π/2)`, so `cos(Pitch)` in the `Position` getter
  never reaches exactly zero — this prevents the camera from ever reaching
  the poles where yaw becomes degenerate (gimbal flip / loss of a well-defined
  right vector). Confirmed by the two `Orbit_ClampsPitch_At*Pole` tests, and
  independently verified the math: at `Pitch = π/2 - 0.01`, `cosPitch ≈
  0.01 > 0`, so `right = normalize(cross(forward, UnitY))` stays well-defined.
- **Orbit**: adds `deltaYaw`/`deltaPitch` to `Yaw`/`Pitch` (yaw unclamped,
  correct since yaw wraps freely), position derived via spherical
  coordinates from `Target`/`Distance`/`Yaw`/`Pitch` — standard arcball
  layout.
- **Zoom**: clamps `Distance` to `[MinDistance, MaxDistance]` via
  `Math.Clamp` — bounded, reasonable.
- **Pan**: computes camera-local `right`/`up` from `forward = normalize(Target
  - Position)`, moves `Target` scaled by `Distance` so pan speed feels
  consistent regardless of zoom level — reasonable and matches common CAD
  viewer conventions. Confirmed by test that camera-to-target offset is
  preserved after pan (i.e., `Position` moves with `Target`, since `Position`
  is always re-derived from `Target`).
- **Dependency scope**: `OrbitCamera.cs` itself has exactly two `using`
  directives — `System` and `System.Numerics` — and uses only
  `Vector3`/`Matrix4x4`/`MathF`/`Math.Clamp`. **Zero references to
  Silk.NET, OpenGL, or Avalonia in this file.** Matches the stated scope
  (pure `System.Numerics` math).
  - Note (not a defect introduced by this batch): the containing project,
    [Meshwright.Rendering.csproj](../../../src/Meshwright.Rendering/Meshwright.Rendering.csproj),
    does carry `PackageReference`s for `Silk.NET.OpenGL` and `Silk.NET.Maths`.
    Confirmed via `git log` that this csproj was committed as-is in the M0
    batch-1 scaffolding commit (`47c13a0`) and was **not touched** by batch 2
    — `git diff` against `HEAD` for that file is empty. This is expected
    scaffolding for the renderer work `Meshwright.Rendering` will host later
    (§6.3), not something batch 2 introduced, and `OrbitCamera.cs` itself
    doesn't use either package.

## Check 4: Module boundaries respected

**Result: PASS**

`git status --porcelain` shows only the expected new/untracked files for this
batch:

```
?? src/Meshwright.Geometry/TriangleMesh.cs
?? src/Meshwright.IO/Stl/StlReader.cs
?? src/Meshwright.Rendering/Camera/
?? tests/Meshwright.Tests/Camera/
?? tests/Meshwright.Tests/Stl/
```

No changes under `src/Meshwright.App/`, `src/Meshwright.Core/`, or any other
`src/Meshwright.Rendering/` file besides the new `Camera/` folder. One
unrelated pre-existing modification was present in the working tree
(`.github/hooks/build-on-edit.json`, tracked, editor tooling config) — not
part of this batch's scope and not attributable to either implementer's
STL/camera work; flagged here for visibility only, not a boundary violation
by this batch.

## Summary

| Check | Result |
|---|---|
| 1. Build + test (non-flaky, 3x) | PASS |
| 2. StlReader / TriangleMesh correctness | PASS |
| 3. OrbitCamera pitch clamp / orbit / pan / zoom / dependency scope | PASS |
| 4. Module boundaries | PASS |

**Overall: verified.**

Raw logs: [build.log](build.log), [test.log](test.log).
