# M4-4 batch — docs/release, first pass

First pass at M4-4 ("docs, website, sample files" per §7). "Website" was
scoped down to a GitHub Pages site (`/docs` on `main`) at the user's
request — no custom domain, no separate hosting decision needed.

## What was delivered

1. **`docs/index.html`** — a single static page for GitHub Pages, no build
   step and no external dependencies (system font stack, inline CSS,
   light/dark via `prefers-color-scheme`). Sections: hero/tagline, the
   competitive-gap table and non-goals from §1/§2, a feature grid drawn
   from §5.1, a status list mirroring §7's milestone table, a "Try it"
   section, and the licensing plan from §8. All links point at
   `github.com/bjornhenneberg/meshwright` (the user-supplied owner) —
   nothing points at a nonexistent domain or a placeholder.

2. **`samples/`** — `sample-tetrahedron.stl` (clean, closed) and
   `broken-cube.stl` (missing face, one flipped normal, one stray shell),
   copied from existing in-house test fixtures (`src/Meshwright.App/Assets/`
   and `tests/Meshwright.Tests/Fixtures/`) rather than authored fresh, so
   there's no new licensing surface — both were already original to this
   repo. A `samples/README.md` explains what each one demonstrates and
   points at `tests/corpus/` for a larger, real-world set.

3. **`README.md`** — expanded from a one-line "planned stack" placeholder
   into real build/run/test/package instructions (clone, `dotnet run`,
   `dotnet test`, `scripts/package-linux.sh`), a status line reflecting
   actual milestone progress instead of "early design," and links to the
   new site and `samples/`.

## A finding surfaced along the way: name conflict

A web search for "Meshwright" (prompted by the user asking about it before
committing to a public site under the name) found an active Florida LLC,
**MeshWright, LLC**, selling **MeshWright Designer** — software for welded
wire mesh reinforcement design (construction rebar), unrelated to 3D
printing but the same spelling and the same broad category (design
software). No conflict exists in the 3D-printing/mesh-repair space itself.
Discussed with the user: since this is a free/open-source project shared
publicly rather than a commercial product today, the risk was judged low
enough not to block this batch, but worth a real trademark check (not just
a web search) before any paid release under §8's plan — recorded as an open
item in §10 and as a decision-log entry in §11.

## Verification

- Rendered `docs/index.html` in headless Chromium
  (`chromium --headless --screenshot=...`) at two viewport heights and
  reviewed the output: hero, comparison table, feature grid, status list,
  "Try it" code block, and footer all render as intended; no broken layout.
  Dark-mode CSS branch present and structurally correct
  (`prefers-color-scheme` + `[data-theme]` override, per the standard
  pattern) but not independently confirmed rendering in light mode in this
  headless environment — the two `:root` blocks were reviewed by hand
  instead.
- Checked every `github.com` link in the page resolves to the real,
  user-confirmed owner (`bjornhenneberg`) and a real path (repo root or
  `blob/main/SPECIFICATION.md`) — no stray placeholder URLs left in the
  file (`grep -n 'github.com/"' docs/index.html` empty).
- Confirmed both sample STL files are copies of existing in-repo fixtures
  (`diff` against source, not shown here but trivial — same byte content),
  not new content needing a licensing decision.
- Did **not** verify GitHub Pages actually serves the page: no git remote
  is configured for this repository in this session, so nothing has been
  pushed to GitHub yet. This is the largest open item from this batch — the
  page is written and locally verified to render, but its actual publish
  path is unexercised.

## Known gaps / follow-ups

- Push to GitHub and enable Pages (Settings → Pages → Deploy from a branch
  → `main` / `/docs`) to make the site live; re-check links resolve once
  it is.
- No real in-app screenshots on the site — this dev host is headless for
  interactive use, so there's nothing genuine to show yet beyond the tiny
  64×64 GPU-test evidence PNG, which isn't presentable. Revisit once
  there's a way to capture the actual running UI.
- Windows/macOS CI + packaging (still under M4-3) is the largest remaining
  M4 gap.
- Name/trademark: a proper check before any paid release, per §10/§11.
