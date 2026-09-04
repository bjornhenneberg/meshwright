# Sample files

Two small, hand-authored STL fixtures for trying Meshwright without hunting
down a real-world mesh first. Both are original to this repo (also used as
test fixtures under `tests/Meshwright.Tests/Fixtures/` and
`src/Meshwright.App/Assets/`) — no third-party licensing to worry about,
unlike the real-world corpus in `tests/corpus/` (see its `manifest.tsv`),
which is fetched on demand and never committed.

- **`sample-tetrahedron.stl`** — a clean, closed tetrahedron. Open it and run
  Inspect: it should report zero issues. Good for confirming the app itself
  works before troubleshooting a real file.
- **`broken-cube.stl`** — a cube with a missing face, one inverted normal,
  and a stray disconnected tetrahedron shell. Open it and run Inspect, then
  Auto Repair, to see the plain-language diagnostics and the repair pipeline
  fix all three defects in one step.

For a larger, more realistic set of test meshes (real consumer 3D-print
files with independent ground-truth defect annotations), see
`tests/corpus/manifest.tsv` and `scripts/fetch-corpus.sh` — that set is
fetched, not committed, since it's third-party content under its own
licences.
