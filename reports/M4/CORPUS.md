# M4-1: real-world test corpus

29 third-party meshes, fetched on demand and never committed. `tests/corpus/manifest.tsv`
records filename, sha256, upstream repo and URL; `scripts/fetch-corpus.sh` downloads
them into `tests/corpus/files/` (gitignored) and re-verifies checksums, so re-running is
free and a truncated or upstream-changed file fails loudly instead of silently.

`CorpusSmokeTests` loads every file through the shipping importer and the full detector
set and asserts nothing throws — M4-1's "crash-free on the test corpus" bar. With the
corpus absent it passes trivially, so a clean checkout and a CI run without it stay green.

## Why the files are not committed

Neither upstream repository carries a blanket licence, and the models have individual
provenance (research datasets, scans, donated art). Fetching them keeps licensing with
the upstream projects, keeps ~54 MB of binaries out of git history permanently, and keeps
the set reproducible by checksum. **Before any of these is relied on in a shipped artefact
or a public CI log, per-model provenance needs checking** — local test input is a much
weaker claim than redistribution.

## Sources, and what is missing

- `alecjacobson/common-3d-test-models` — 21 models: standard graphics/scan test meshes.
- `libigl/libigl-tutorial-data` — 8 models, several deliberately defective.

SPECIFICATION.md §7 names Thingiverse and Printables. Neither is represented:
Thingiverse returns HTTP 403 to automated download and bulk fetching is against its
terms, and Printables returned 403 as well. **Those need fetching by hand, or an API
key** — this is the main remaining gap in M4-1, since consumer print files are exactly
the population Meshwright targets and are likely dirtier than research meshes.

The corpus is also OBJ-only. STL coverage still rests on the synthetic
`BrokenSample.stl`; a real-world STL set should come with the Thingiverse half.

## Defect census

All seven v1.0 detector categories fire, so the corpus exercises the whole of Inspect.
Totals across 29 meshes: 15,309 self-intersections, 245 boundary holes, 137 non-manifold
edges, 126 disconnected shells, 94 inverted normals, 633 duplicate vertices, 4 degenerate
triangles. The Stanford bunny's 5 boundary holes are its well-known unscanned base.

Roughly a third of the models are clean (armadillo, bimba, igea, nefertiti, fandisk,
homer, cheburashka, spot, rocker-arm, decimated-max, elephant) and the rest carry real
defects, which is the mix a regression corpus wants — a corpus of only-broken meshes
cannot catch a detector that reports issues on good geometry.

## Performance finding

The corpus immediately exposed a performance bug that no synthetic fixture had.
`SelfIntersectionDetector` was an all-pairs O(n^2) scan, documented as "broadphase
acceleration is out of scope for M1" — while `SelfIntersectionRepair` already contained a
`DMeshAABBTree3` broadphase with a correctness argument for it. The detector cost 2.5s on
a 5,800-triangle mesh; extrapolating quadratically to the corpus's 269k-triangle models
put a single file in the hours, against §6.4's budget of 5s for a 500k-triangle
auto-repair. The first census run was abandoned after 20 minutes.

Both sides now share `Spatial/SelfIntersectionSearch`, so detection and repair cannot
disagree about what a self-intersection is. Measured on corpus files, issue counts
identical before and after:

| mesh | triangles | before | after |
| --- | --- | --- | --- |
| suzanne | 966 | 133 ms | 35 ms |
| lg-truck | 4,719 | 2,556 ms | 146 ms |
| cow | 5,804 | 2,521 ms | 122 ms |
| spot | 5,856 | 2,512 ms | 30 ms |

The whole 29-mesh corpus now loads and diagnoses in ~13s.
