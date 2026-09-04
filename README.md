# Meshwright

A focused, cross-platform desktop tool for repairing and preparing meshes for 3D
printing — the successor to Meshmixer that never arrived.

Not a CAD package. Not a sculpting suite. Not a slicer. It takes a mesh from
"downloaded or scanned" to "ready to slice".

- Inspect: find holes, non-manifold edges, self-intersections, flipped normals
- Repair: one click, or tune every step
- Edit: plane cut, booleans, hollow, drain holes, transforms, decimation

Status: **in active development** — M0 (Skeleton), M1 (Inspect), M2 (Repair)
and M3 (Edit) are complete; M4 (Polish and release) is underway. No public
release yet. See [SPECIFICATION.md](SPECIFICATION.md) for the full roadmap,
and the project site at
[bjornhenneberg.github.io/meshwright](https://bjornhenneberg.github.io/meshwright/)
for an overview.

## Stack

C# / .NET 10 (LTS), Avalonia UI, Silk.NET (OpenGL) viewport, Manifold (MIT)
for booleans.

## Building from source

Ubuntu 24.04 / Linux Mint 22.x:

```bash
sudo apt install dotnet-sdk-10.0
git clone https://github.com/bjornhenneberg/meshwright.git
cd meshwright
dotnet run --project src/Meshwright.App
```

Booleans need Manifold's native library, already built and committed at
`runtimes/linux-x64/native/` — see `scripts/build-manifold-native.sh` if you
need to rebuild it. Two small sample meshes to try Inspect/Repair on
immediately live in [`samples/`](samples/README.md).

To run the test suite:

```bash
dotnet test Meshwright.sln
```

The real-world test corpus (`tests/corpus/`) isn't committed — run
`scripts/fetch-corpus.sh` first if you want those tests to do more than
pass trivially; see `tests/corpus/manifest.tsv` for what it fetches and why.

To build a self-contained Linux `.deb`:

```bash
./scripts/package-linux.sh
```

## Licence

To be finalised — permissive (MPL-2.0 or Apache-2.0) core, with paid prebuilt
binaries. See §8 of the specification.
