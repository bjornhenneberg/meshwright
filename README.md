# Meshwright

Repair and prepare meshes for 3D printing. Meshwright takes a file from
"downloaded or scanned" to "ready to slice": find what is broken, fix it, and
cut, hollow or simplify it to fit the printer. No account, no cloud, no
telemetry.

Meshmixer was discontinued in 2021 and nothing replaced it. This is meant to be
the replacement — not a CAD package, not a sculpting suite, not a slicer.

![The Meshwright window: an Eiffel Tower model in the 3D viewport with defects
highlighted in red, the Repair panel on the left, and a diagnostics panel on the
right listing every problem found](docs/images/overview.png)

> **Pre-1.0.** There are no packaged downloads yet — [building from
> source](#building-from-source) is three commands. See [what works and what
> doesn't](#status) before you rely on it.

## What it does

- **Inspect** — non-manifold edges, boundary holes, self-intersections, flipped
  normals, degenerate triangles, duplicate vertices and stray shells.
  Diagnostics run on load and after every operation, and anything locatable is
  highlighted on the model rather than only counted.
- **Repair** — one-click Auto Repair, or run the steps yourself: hole filling
  (flat, planar or surface-following), normal unification, small-shell removal,
  self-intersection resolution, and voxel remesh as a last resort.
- **Edit** — plane cut, booleans (union, difference, intersection), transforms,
  hollowing to a wall thickness, and drain holes. A cut can generate
  registration pins, so a model split to fit the bed goes back together aligned.
- **Simplify** — quadric edge-collapse decimation to a triangle count or a
  percentage, which says when it cannot reach the target instead of reporting a
  shortfall as success.
- **See it on the bed** — a build plate sized to your printer, with a warning
  naming any side the model hangs over and by how much. Orthographic or
  perspective, seven view presets, wireframe and x-ray.
- **See inside it** — a cross-section slider that opens the model along X, Y or
  Z without cutting anything, so you can check a hollow's wall thickness or find
  the shell hiding in the middle of a part.

Every spatial parameter is set with a gizmo in the viewport, with text boxes as
the precision fallback. Everything is undoable. STL and OBJ in and out.

The [usage guide](https://bjornhenneberg.github.io/meshwright/usage.html) walks
through the whole workflow with screenshots.

## Status

Inspect, repair, edit and simplify all work, and are covered by a test suite
that runs on Linux, Windows and macOS. What is **not** there yet:

- **No release.** No tagged version, no packaged download, no installer;
  building from source is the only way to run it.
- **Nothing is remembered between launches** — there is no settings file, so the
  build plate resets to a 220 mm bed each time and the File menu has no
  recent-files list.
- **Import is literal.** No unit detection (a model authored in inches loads
  25.4x undersized with no prompt), no drag-and-drop, and only STL and OBJ —
  3MF and PLY are not implemented.
- **The cross-section does not cap the face it opens**, so a solid part reads as
  an open shell, and it only cuts along X, Y or Z. Plane Cut gives you the real
  capped face when you want it.
- **No headless or batch command line.**

The usage guide's [known rough edges](https://bjornhenneberg.github.io/meshwright/usage.html#known)
is the honest list, including the places where an operation can surprise you.

## Platform support

| | Builds | Test suite | Booleans | Packaging |
| --- | --- | --- | --- | --- |
| Linux (x64) | yes | yes | yes | `.deb`, built and started here |
| Windows (x64) | in CI | in CI | CI builds the native library; nothing prebuilt is committed | zip, never launched on a real machine |
| macOS (arm64/x64) | in CI | in CI | same | zip, unsigned, never launched on a real machine |

Everything here is developed and used on Linux. Windows and macOS are built and
tested by CI on every push, but nobody has yet launched a packaged build on
either — treat them as untested rather than supported.

Two consequences worth knowing before filing a bug:

- **Booleans are disabled in a Windows or macOS package built outside CI.**
  Manifold's native library is only committed for `linux-x64`, and the packaging
  scripts for the other platforms rely on CI to build it. A package without it
  throws on a boolean rather than silently doing the wrong thing, and ships a
  `NOTICE.txt` saying so. Every other feature is unaffected.
- **macOS builds are unsigned and un-notarised**, so Gatekeeper refuses the
  first launch — right-click the app and choose Open, or clear the quarantine
  attribute.

## Building from source

Ubuntu 24.04 / Linux Mint 22.x:

```bash
sudo apt install dotnet-sdk-10.0
git clone https://github.com/bjornhenneberg/meshwright.git
cd meshwright
dotnet run --project src/Meshwright.App
```

The app takes a file path as an argument. Two small sample meshes — one clean,
one deliberately broken — live in [`samples/`](samples/README.md) if you want
something to try Inspect and Auto Repair on immediately.

Booleans need Manifold's native library, already built and committed at
`runtimes/linux-x64/native/`; `scripts/build-manifold-native.sh` rebuilds it.

For a self-contained package: `scripts/package-linux.sh` builds a `.deb`, and
`scripts/package-windows.sh` / `scripts/package-macos.sh` build a zip, with the
caveats above.

### Tests

```bash
dotnet test Meshwright.sln
```

The real-world test corpus (`tests/corpus/`) is not committed — run
`scripts/fetch-corpus.sh` first if you want those tests to do more than pass
trivially; `tests/corpus/manifest.tsv` says what it fetches and why.

## Built with

C# and .NET 10, [Avalonia](https://avaloniaui.net/) for the UI,
[Silk.NET](https://dotnet.github.io/Silk.NET/) for the OpenGL viewport,
[Manifold](https://github.com/elalish/manifold) for booleans, and geometry types
vendored from [g3Sharp](https://github.com/gradientspace/geometry3Sharp).

## Licence

Not finalised. The plan is a permissive core (MPL-2.0 or Apache-2.0) with paid
prebuilt binaries, with inspection and diagnostics free either way. Until a
`LICENSE` file lands, assume nothing.

## Contributing

`SPECIFICATION.md` holds the scope, architecture and decision log, and
`AGENTS.md` the working rules. Read both before opening a pull request.
