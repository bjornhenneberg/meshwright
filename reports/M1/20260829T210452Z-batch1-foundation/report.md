# Verification: M1 batch 1 foundation

**Date (UTC):** 2026-08-29T21:04:52Z

**Scope:** current uncommitted worktree changes to the mesh representation,
g3 vendoring, STL reader, renderer, viewport binding, tests, build hook, and
gitignore. Baseline commit: `b25e014`.

**Verifier:** independent re-check. This report records only directly observed
source and command results.

## Verdict: failed

The solution compiles cleanly and all discovered tests pass, but this batch
cannot be trusted as conforming M1 foundation work. Its `DMesh3` is a
handwritten, merged subset with stubbed vendor APIs rather than the required
vendored g3Sharp source plus its dependencies. A renderer-normal test also
fails to exercise the first triangle it claims to cover. These are blocking
for the requested vendoring and meaningful-test requirements.

## Checks

| Check | Result | Direct observation |
| --- | --- | --- |
| `dotnet build Meshwright.sln --no-restore` | PASS | Completed in 19.73 s with 0 warnings and 0 errors. Raw output: [build.log](build.log). |
| `dotnet test Meshwright.sln --no-restore --no-build` | PASS | `Meshwright.Tests.Gpu`: 3/3 passed; `Meshwright.Tests`: 33/33 passed; 0 skipped. Raw output: [test.log](test.log). |
| Production `TriangleMesh` replacement | PASS | A full source/dependency search found no production reference to `TriangleMesh`; its former class body is removed. Production mesh parameters/properties in STL, rendering, and viewport code are `g3.DMesh3`. The remaining `TriangleMesh` occurrences are fixture class names only. |
| STL topology welding | PASS (limited) | `StlReader.BuildIndexedMesh` maps equal `Vector3` coordinates to shared `g3.Vector3d` vertex IDs and appends indexed triangles. Binary and ASCII cube tests assert 12 triangles and 8 shared vertices, and passed. This proves exact-coordinate welding only; no tolerance-based welding behavior is implemented or tested. |
| Renderer indexed expansion | PASS (limited) | `VertexDataBuilder.BuildPositions` iterates `TriangleIndices()` and expands each `Index3i`; `BuildPerVertexNormals` computes one triangle normal and writes it three times. The position-expansion test and all three real-GPU tests passed, including a rendered mesh swap pixel-difference check. |
| Boost header and provenance | PARTIAL | [DMesh3.cs](../../../../src/Meshwright.Geometry/Vendor/g3/DMesh3.cs) begins with the full Boost Software License 1.0 text. [VENDOR.md](../../../../src/Meshwright.Geometry/Vendor/g3/VENDOR.md) records upstream commit, date, source paths, and declared trims. |
| Required g3Sharp vendoring procedure | FAIL | The only vendored source is one handcrafted 251-line file that combines portions of seven upstream types. It does not preserve the upstream relative files or types as-is. `DMeshAABBTree3.Build()` is an empty stub, despite the vendoring instructions requiring needed dependencies to be vendored rather than silently stubbed. `VENDOR.md` itself describes `MeshBoundaryLoops` and `DMeshAABBTree3` as "API scaffold" rather than functional upstream implementations. This conflicts with the mandated vendoring procedure in [the local skill](../../../../.github/skills/g3sharp-vendoring/SKILL.md) and `AGENTS.md`. |
| Meaningful normal-expansion coverage | FAIL | In `BuildPerVertexNormals_DuplicatesEachTriangleNormalThreeTimesInOrder`, all six assertions target `result[3]`, `result[4]`, and `result[5]`; positions 0–2 are never asserted. Both test triangles have the same +Z normal, so it also does not prove that normals are emitted in per-triangle order. |
| Current ancillary changes | FAIL | `.github/hooks/build-on-edit.json` has its complete object commented out with `//`, which makes it invalid JSON and disables the post-edit build hook. This is unrelated to M1 mesh foundation behavior. `git diff --check` reported no whitespace errors. |
| Architecture/dependencies | PASS | `Meshwright.Geometry` has no project/package references; IO and Rendering each reference Geometry only as expected. No `g3Sharp` package reference and no GPL geometry dependency were found. `Directory.Build.props` targets `net10.0`. |

## Visual evidence

No durable PNG/frame capture was produced for this batch. The changed viewport
surface is a type substitution and its renderer behavior was exercised by the
real-GPU suite's framebuffer pixel checks, which passed, but that output does
not produce a reviewable screenshot. Therefore visual appearance in the
Avalonia viewport remains unverified by this report.

## Commands executed

```text
git status --short
git diff --stat
git diff --check
git diff --name-only
find src/Meshwright.Geometry/Vendor/g3 -maxdepth 2 -type f -printf '%p\n' | sort
grep -RInE 'TriangleMesh|DMesh3|g3Sharp|geometry3Sharp|PackageReference|<TargetFramework' ...
dotnet build Meshwright.sln --no-restore
dotnet test Meshwright.sln --no-restore --no-build
```

## Required remediation before trust

1. Vendor actual g3Sharp source files with preserved layout and Boost headers,
   then vendor their required dependencies rather than supplying API stubs.
   Update `VENDOR.md` with exact source paths, upstream revision, and real trims.
2. Correct and strengthen the normal-expansion test: assert indices 0–2 and
   3–5 using oppositely wound triangles so ordering is observable.
3. Restore valid, active JSON for the post-edit build hook or remove the
   unrelated worktree change intentionally in its own reviewed batch.
4. Produce an Avalonia Headless PNG/frame sequence, or explicitly add a
   supported renderer capture path, before claiming viewport visual validation.

## Raw logs

- [build.log](build.log)
- [test.log](test.log)