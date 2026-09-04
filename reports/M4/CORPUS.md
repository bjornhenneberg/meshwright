# M4-1: real-world test corpus

53 third-party meshes, fetched on demand and never committed.
`tests/corpus/manifest.tsv` records filename, sha256, url, source, licence and
per-file defect notes; `scripts/fetch-corpus.sh` downloads them into
`tests/corpus/files/` (gitignored) and re-verifies checksums, so re-running is free
and a truncated or upstream-changed file fails loudly instead of silently.

`CorpusSmokeTests` loads every file through the shipping importer and the full
detector set and asserts nothing throws — M4-1's "crash-free on the test corpus"
bar. With the corpus absent it passes trivially, so a clean checkout and a CI run
without it stay green.

## Sources

**Thingi10K via Hugging Face — 24 files, STL.** Real consumer 3D-printing models:
the population Meshwright actually targets. `Thingi10K/Thingi10K` is the
Eurographics-award-winning research dataset of 10,000 Thingiverse models, mirrored
on Hugging Face with all 9,956 meshes individually addressable, plus per-file
licence and per-file defect statistics.

Selection is deliberate, not a sample. Every file is CC-BY, CC0 or public domain —
NC, ND and share-alike models were excluded so the set stays usable if it is ever
redistributed. Files span 800–135,000 faces across three size bands, chosen to
cover every v1.0 detector category, with clean models included as controls: a
corpus of only-broken meshes cannot catch a detector that reports issues on good
geometry.

Thingi10K's own analysis gives independent ground truth per file
(`num_boundary_edges`, `num_self_intersections`, `edge_manifold`, `oriented`,
`num_connected_components`, …), which maps almost one-to-one onto Meshwright's
seven detectors. The manifest's notes column carries it. Nothing yet asserts
Meshwright's output against it — that is the obvious next step, and would turn the
smoke test into a true regression corpus.

**alecjacobson/common-3d-test-models and libigl/libigl-tutorial-data — 29 files,
OBJ.** Standard graphics and scan test meshes. Neither repository carries a blanket
licence and the models have individual provenance, so these are local test input
only; per-model provenance needs checking before any is relied on in a shipped
artefact or a public CI log. The Thingi10K half has no such caveat.

## What could not be obtained

- **Thingiverse and Printables directly**: both return HTTP 403 to automated
  download, and bulk fetching is against Thingiverse's terms. The Hugging Face
  mirror of Thingi10K is how these files are reachable legitimately, and it is
  strictly better anyway — it adds per-file licence and defect ground truth that
  scraping would not have given.
- **Epic (Sketchfab, Fab)**: Sketchfab's download endpoint requires OAuth (HTTP
  401) and fab.com returns 403 to automated access. Even with a token the content
  is glTF art and photogrammetry assets rather than print files, so it is a poorer
  fit than Thingi10K. Not pursued.

## What the corpus found

It paid for itself twice on its first two runs.

**A quadratic self-intersection scan.** `SelfIntersectionDetector` was an all-pairs
O(n²) scan carrying a "broadphase acceleration is out of scope for M1" note — while
`SelfIntersectionRepair` already contained a `DMeshAABBTree3` broadphase and a
written argument for why it cannot miss a true intersection. It cost 2.5 s on a
5,800-triangle mesh; extrapolated quadratically to a 269k-triangle model that is
hours per file, against §6.4's budget of 5 s for a 500k-triangle auto-repair. The
first census run was abandoned after 20 minutes. Both sides now share
`Spatial/SelfIntersectionSearch`. Issue counts identical before and after:

| mesh | triangles | before | after |
| --- | --- | --- | --- |
| suzanne | 966 | 133 ms | 35 ms |
| lg-truck | 4,719 | 2,556 ms | 146 ms |
| cow | 5,804 | 2,521 ms | 122 ms |
| spot | 5,856 | 2,512 ms | 30 ms |

**A hard crash on a real print file.** Thingi10K 84929 killed the entire import
with `IntrLine2Triangle2.GetInterval: too many intersections!` from vendored g3
code: a fully-collinear triangle puts all three vertices on the intersection line
at once, which the exact predicate cannot represent. `SelfIntersectionSearch` now
excludes zero-area triangles from the exact test — semantically right, since a
triangle with no interior cannot pass through another and
`DegenerateTriangleDetector` already reports it in its own right. The threshold is
deliberately far tighter than that detector's scale-relative sliver threshold: the
job is only to exclude what the predicate cannot process, so thin-but-real
triangles are still tested. Pinned by unit tests that do not need the corpus.

## Ground truth

`CorpusGroundTruthTests` checks Meshwright's detectors against Thingi10K's independent
analysis of the same files, recorded per file in the manifest's notes column. Exact
counts are deliberately not asserted: two implementations legitimately disagree about
how to count one defect (a self-intersection can be one face pair or many; a hole's
size in edges depends on how the boundary is walked). What is asserted is the direction
that actually harms a user — **a mesh the reference calls clean in a category must not
be reported as defective in that category**. False positives are the dangerous class for
a repair tool, because they push a user into "repairing" geometry that was fine. A
weaker converse guard catches wholesale misses: a file with 50+ reference defects in a
category must not come back silent.

Assertions run only on losslessly-imported meshes — see below for why that qualifier is
load-bearing.

## The import was silently discarding geometry — now fixed

The ground-truth comparison found the most serious bug of the corpus work.
`DMesh3` is an indexed, edge-based mesh: an edge belongs to at most two triangles, so
`AppendTriangle` refuses a third and returns `NonManifoldID`, and it needs three distinct
vertex ids per triangle. **Both readers discarded that return value**, so importing
silently dropped exactly the geometry Meshwright exists to diagnose. 14 of the 24 real
print files lost triangles and two lost about 73%; thingi10k-204394 kept 9,516 of 34,752,
which is why its reference count of 34,905 self-intersections came back as 16.

`NonManifoldMeshBuilder` now keeps that geometry by **splitting the mesh at the offending
vertices instead of dropping the triangle**. Duplicating a vertex gives the triangle a
fresh edge to attach to, so it lands at exactly the right position while the topology
stays legal: the geometry is complete and only the connectivity is cut, which is an
honest description of a non-manifold junction — there is no single consistent surface
there to be connected to. Degenerate triangles survive the same way, as zero-area
triangles the degenerate detector then reports.

This is the representation `NonManifoldDetector` was already written for. Its comment has
always said a true non-manifold edge "can only appear in this data structure as several
distinct edge ids that share the same pair of vertex *positions*" — but nothing produced
that shape, so it could only ever report defects the importer had already thrown away.
Every corpus file now loads 100% of its triangles, asserted per file against the
reference's own face count.

### The consequence, and the second half of the fix

Cutting connectivity leaves *seams*: edges the structure calls boundaries although another
edge sits at the same two positions and the surface plainly continues. Detectors reasoning
only about vertex ids then invent defects — the first run after the change reported 13,348
phantom holes on one closed file, and split single shells into thousands of fragments. A
fix that trades lost geometry for false reports is not a fix.

So the topology-derived detectors now reason about positions, extending the pattern
`NonManifoldDetector` already set (`PositionTopology`). `BoundaryHoleDetector` ignores
loops made entirely of seam edges; `DisconnectedShellDetector` joins components across
coincident edge positions. Agreement with the reference improved sharply as a result:

| file | shells before | shells after | reference components |
| --- | --- | --- | --- |
| thingi10k-204394 | 4,757 | 31 | 32 |
| thingi10k-92067 | 183 | 1 | 1 |
| thingi10k-96639 | 917 | 1 | 1 |

The same rule means a *crack* — two open edges at identical positions, from near-coincident
vertices welding did not merge — is not reported as a hole. That is deliberate: the surface
is geometrically continuous there, and `DuplicateVertexDetector` reports the actual cause
with a repair that addresses it. Calling it a hole would point the user at the wrong fix.
The ground-truth check accepts either category as "we noticed this surface is not closed".

## Census

All seven v1.0 detector categories fire. The 53-file corpus loads and diagnoses in
~24 s. Consumer print files are markedly dirtier than research meshes, as expected
— Thingi10K 87345 alone carries 6,746 self-intersections, 619 non-manifold edges,
158 boundary holes, 141 shells and 129 inverted normals across 20k triangles.
