# Next session

You are running as **Opus, in a dispatcher role**, on the Meshwright repo
(`/home/bjorn/Code/meshwright`) — a cross-platform desktop tool for repairing
meshes for 3D printing (C# / .NET 10 / Avalonia / Silk.NET, geometry on a
vendored g3Sharp).

**Read `SPECIFICATION.md` first.** §5.1 is v1.0 scope, §7 narrates each
milestone batch, §11 is a dated decision log (read the last few rows — they
are the most useful pages in the repo), and "Immediate next steps" at the end
is the backlog. Items 1–18 are done and struck through, and M4-3 closed last
session.

## How to work

Do not implement the backlog yourself. **Dispatch each task to a subagent,
then personally verify the result.**

- **Haiku** — mechanical, fully specified: doc sync, inventory sweeps.
- **Sonnet** — well-scoped implementation against an existing pattern with an
  obvious acceptance test. Most of what's left is this.
- **Opus** — algorithmic/topological reasoning where being subtly wrong looks
  like success. Nothing currently open needs this tier.

Practical notes from last session, all of which cost time:

- Tell every agent the exact commit `main` is at and have it confirm before
  starting. Agents have branched from a stale commit before, and some **work
  directly in the main checkout rather than a worktree** — if you dispatch
  several at once, keep their file scopes disjoint and say so explicitly.
- Tell every agent **not** to edit `SPECIFICATION.md` or `NEXT-SESSION.md`,
  and to put proposed §11 wording in its report instead. You land those
  centrally. This worked well and avoided every conflict.
- An agent may return a placeholder ("I'll wait for the background run")
  without having done the work. Check for actual edits before believing a
  report.

## How to verify

The recurring failure mode here is **work that reports success while being
wrong**. §11 is largely a catalogue of it, and last session added four more:
a docs page advertising two defects that had been fixed, a screenshot caption
telling readers to repair a mesh that loads clean, a build script printing
"==> Verified ..." while shipping an unloadable library, and a GPU suite that
had been "green at M4-8" while actually hanging.

1. Build, run `dotnet test tests/Meshwright.Tests -c Release`. Baseline is
   **520 passing, 0 skipped**. Never accept a newly skipped test without a
   stated reason.
2. Ask what invariant would catch this being wrong, and check the test asserts
   *that*, not merely that the operation ran. For geometry: bounding box,
   volume, shell count and issue count, compared before and after.
3. **Run the actual app and look at it** unless told not to. A passing suite
   is not evidence a feature works, and neither is a screenshot — read the
   words next to it too.
4. Treat a success message as a claim to check. Grepping an agent's own
   evidence files takes a minute and repeatedly turned assertions into facts.

Two techniques that worked when guessing did not, both worth reusing:

- **Reproduce an unavailable platform's constraint locally.** The macOS
  native-loading bug was fixed and verified with no Mac, because hiding
  `runtimes/` in the Linux test output recreates the exact condition.
- **Make the failing thing report instead of theorising about it.** Two
  hypotheses were already wrong before a self-diagnosing checksum and a CI
  step that asked dyld directly settled the cause.

## State

`main` is clean and pushed. **CI is green on Linux, Windows and macOS** — all
520 tests on each, with Manifold built from source in-job on Windows/macOS.
Verified this session: 520 passing / 0 skipped, and the **GPU suite 8 passing
in 265 ms** with no orphaned hosts left behind. **Always run the GPU suite
under `timeout`.**

Last session ended on a **session rate limit**. Two fix agents were dispatched
and both died at the API before touching a file — the tree was left clean, and
the worktree one was auto-removed. Nothing is half-applied; the findings below
are all still open.

- `gh` is authenticated and git has a credential helper, so you can push. The
  token has `workflow` scope.
- **Stale worktrees**: two old unrelated ones (`agent-a0ef1435...`,
  `agent-a15af9a8...`) and `meshwright.worktrees/progress-check-inquiry`
  predate all recent work — leave them alone unless you know what they are.

## What last session did

A UX pass on the real GUI (backlog item 1), the additive `docs/usage.html`
pass (item 2), and the Meshmixer-alternative research (item 5) all ran and
landed. The two follow-up *fix* dispatches did not.

- **`docs/usage.html`** gained six rough-edges entries and lost a stale one
  that had gone wrong the moment M4-3 landed. Every claim was re-checked in
  source before landing; one bullet the agent wrote was corrected because it
  contradicted `NonManifoldDetector`, which groups edges by position.
- **`reports/research/meshmixer-alternatives.md`** exists, with a caveat worth
  reading: Reddit was unreachable to the agent's tooling and Autodesk's forum
  403s, so the brief — "read a week of *Meshmixer alternative* threads" — was
  substantially not met. ~18 pages, 8 domains, no hobbyist voices in their own
  words. **Treat item 5 as partially done.** Its one substantive finding is
  real and independently checked: §3 names "adding registration pins" as a
  target-user workflow while §5.2 defers pins to v1.x, and plane-cut splitting
  is already v1.0. Whether to promote a minimal peg-and-socket pair into §5.1
  is a scope call for the user; proposed wording is in the report.
- **`reports/M4/20260905T221759Z-ux-audit/`** — 112 screenshots and a report.
  Twelve confirmed defects. Five §11 rows and backlog items 19–24 were landed
  centrally from it.

## Backlog

Items 19–24 in §11's "Immediate next steps" are all new, all confirmed, and
none are started. Best first:

**19. Drain Holes is destructive and reports success.** The worst thing in the
app right now — it deletes every triangle within the radius and adds nothing,
so a Ø0.5 mm request took a whole 2 × 2 mm face and left the model open, while
reporting the diameter it had been handed. Needs a real drilling
implementation, not a patched message. **Opus, in a worktree.** The invariants
that catch it: surface area removed ≈ πr², and vertex count must *increase*
(the old code left it unchanged, proof nothing was constructed).

**20 + 21. The inert-control and false-reporting cluster.** Plane Cut's "Add
Cap" checkbox (and `PlaneCut.Cut` has no uncapped path to reach at all), the
Transform panel printing `bounds.Extents` — half the box size — in the panel
used for scaling to a print bed, Hollow's gizmo status lying from startup,
Auto Repair's double-counted flip total, Decimate's unit label, stale panel
result lines, and "Drop to Z=0" printing the other button's name. **Sonnet**;
one agent can take the lot, but keep it out of the drain-hole files if 19 is
running concurrently. Full detail per defect, with file and line, is in the
audit report.

**22. Decimation introduces the invalid geometry it says it declined to
create** — 67 self-intersections from a clean mesh. Real geometry work: the
local validity test each collapse passes has to be checked against whole-mesh
invariants afterwards. **Opus.**

**23. Most of §5.1's Viewport / UX block does not exist.** Orthographic, view
presets, build plate grid, out-of-bounds warning, wireframe, x-ray,
cross-section slider, unit handling, drag-and-drop, recent files — absent from
the code, not merely unwired. This is the largest remaining v1.0 gap and it
needs **a scope decision from the user before any dispatch**: build for 1.0,
or move to §5.2.

**24. Hole filling and hole detection disagree about import seams.**
`BoundaryHoleDetector` excludes seams by position; `HoleFillRepair` finds
loops by vertex index and does not, so Inspect can report zero holes on a file
whose Auto Repair run adds geometry across a seam. Small, sharp, and exactly
the shape this project keeps getting bitten by. **Sonnet.**

**Packaging follow-ups** stay deferred: §9 still reads "Linux + Windows first;
macOS once there is revenue", so notarisation is deferred by the spec's own
terms. An MSI is buildable on the `windows-latest` runner but unverifiable
from this host, and there is no tagged release yet.

Push freely; the user has given standing authorisation. Ask before starting
anything not on this list.
