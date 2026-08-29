# Meshwright

A focused, cross-platform desktop tool for repairing and preparing meshes for 3D
printing — the successor to Meshmixer that never arrived.

Not a CAD package. Not a sculpting suite. Not a slicer. It takes a mesh from
"downloaded or scanned" to "ready to slice".

- Inspect: find holes, non-manifold edges, self-intersections, flipped normals
- Repair: one click, or tune every step
- Edit: plane cut, booleans, hollow, drain holes, transforms, decimation

Status: **early design.** See [SPECIFICATION.md](SPECIFICATION.md).

## Planned stack

C# / .NET 9, Avalonia UI, Silk.NET (OpenGL) viewport.

## Licence

To be finalised — permissive (MPL-2.0 or Apache-2.0) core, with paid prebuilt
binaries. See §8 of the specification.
