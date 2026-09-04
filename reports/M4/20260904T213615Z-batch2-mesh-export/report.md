# M4: Mesh export

Implements SPECIFICATION.md §5.1 export scope (binary STL, ASCII OBJ) end to end.
Before this batch, `StlWriter`/`ObjWriter` existed (M2) but nothing in
`Meshwright.App` referenced them — a user could open and repair a mesh but not
get it back out.

## What changed

- `src/Meshwright.IO/MeshExporter.cs` — new extension-to-writer dispatch,
  mirroring `MeshImporter`'s shape (`SupportedExtensions`/`SupportedPatterns`,
  `CanExport`, `ExportFile`/`Export(Stream, ...)`), so the save-file picker's
  filter and the writer set cannot drift apart the same way import's picker and
  reader set are pinned by an existing test.
- `src/Meshwright.App/MainWindow.axaml{,.cs}` — a File > Export... menu item and
  toolbar button, wired to `SaveFilePickerAsync`, format chosen by the picked
  file's extension, errors surfaced on the status line the same way
  `OnOpenFileClick` already does. `ExportFileForTesting(path)` bypasses the OS
  picker for headless tests, mirroring `LoadFileForTesting`.

## Tests added

- `tests/Meshwright.Tests/MeshExporterTests.cs` — dispatch by extension
  (case-insensitive), unsupported-format and no-extension rejection with the
  format named in the message, `SupportedPatterns`/`SupportedExtensions`
  parity (mirrors the existing `MeshImporterTests` pinning of the import side).
- `tests/Meshwright.Tests/MainWindowTests.cs` — `ExportFileForTesting` writes
  the loaded mesh to disk with the loaded triangle count, and rejects an
  unsupported extension without creating a file.
- `tests/Meshwright.Tests/Corpus/CorpusExportRoundTripTests.cs` — every M4-1
  corpus mesh (53 files, `tests/corpus/files/`), exported to both STL and OBJ
  in memory and reimported through the shipping importer.

### Round-trip invariant

A bit-identical round trip is not the right invariant: import now splits a mesh
at non-manifold junctions rather than dropping triangles, and STL is
triangle-soup with no shared-vertex indexing, so vertex count can legitimately
differ between what was exported and what comes back. What was checked instead:
**exporting loses no triangles and reimporting drops none** — both writers
serialize every triangle currently in the mesh, and re-importing (STL or OBJ)
must produce the identical triangle count with zero triangles dropped. This
holds across all 53 corpus meshes for both formats (106 checks).

## Caution encoded from AGENTS.md

`ObjWriter` compiled and ran for the first time only recently (the `Wavefront/`
rename fixed the `obj/` MSBuild-exclusion bug) — it is far less battle-tested
than its age suggests. The corpus round-trip exercises it against real,
messy print files rather than only hand-built tetrahedra, which is the OBJ
writer's first real-world exercise.

## Results

- Full unit suite: 432/432 passing (417 baseline + 15 new: 9 in
  `MeshExporterTests`, 2 in `MainWindowTests`, 1 in
  `CorpusExportRoundTripTests`, plus corpus/GPU counted elsewhere — see below).
- `CorpusExportRoundTripTests` alone: 6/6 (both the pre-existing corpus smoke
  tests and the new round-trip test), all 53 corpus files present locally.
- GPU test suite (`Meshwright.Tests.Gpu`) not re-run: this batch touches no
  rendering code.

```
$ dotnet test tests/Meshwright.Tests/Meshwright.Tests.csproj -c Debug
Passed!  - Failed: 0, Passed: 432, Skipped: 0, Total: 432
```

## Not done

- No drag-and-drop or recent-files list for export (§5.1 lists these for
  import; export has no equivalent requirement stated).
- No unit-scale prompt on export — v1.0 scope only calls for unit
  detection/scaling on *import*.
