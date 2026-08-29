---
name: g3sharp-vendoring
description: 'Vendor a g3Sharp (geometry3Sharp) type into Meshwright.Geometry/Vendor/g3/. Use when pulling in DMesh3, MeshNormals, MeshConnectedComponents, MeshBoundaryLoops, Reducer, Remesher, DMeshAABBTree3, MarchingCubes, MeshSignedDistanceGrid, or any of their dependencies, per SPECIFICATION.md §6.2.'
---

# g3Sharp Vendoring

Meshwright vendors selected g3Sharp (geometry3Sharp) types instead of taking
it as a package reference or forking the whole project (SPECIFICATION.md
§6.2). This skill is the checklist for doing that consistently.

## When to Use

- Adding a new g3Sharp type listed in §6.2, or a type one of those depends on.
- Never for anything not on that list — check with the user first if a needed
  type isn't already named in §6.2.

## Procedure

1. Locate the source file(s) for the type in an upstream g3Sharp checkout.
2. Copy the file(s) as-is into `Meshwright.Geometry/Vendor/g3/`, preserving
   the original relative structure where practical.
3. Keep the Boost licence header intact at the top of every vendored file —
   do not strip or reword it.
4. Trim only what's necessary to compile: remove `using`s and members that
   pull in parts of g3Sharp outside the vendored set (solvers, curve tooling,
   implicit surfaces, its own I/O, etc.), per §6.2. If a dependency is
   required to compile, vendor it too rather than stubbing it out silently.
5. Do not rename types/namespaces gratuitously — minimize the diff from
   upstream so future updates are easy to diff against.
6. Append an entry to `Meshwright.Geometry/Vendor/g3/VENDOR.md` (create it if
   it doesn't exist) recording: type name, upstream source path/commit or
   release reference, date vendored, and any trims made in step 4.
7. Add or update xUnit tests exercising the vendored type through Meshwright's
   own code paths, not just upstream's own tests.

## Output

When done, report: which file(s) were added, what (if anything) was trimmed
and why, and the `VENDOR.md` entry added.
