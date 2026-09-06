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

`main` is clean and pushed. **562 tests passing, 0 skipped** (baseline was 520;
+24 drain hole, +18 panel fixes), and the **GPU suite 8 passing in 474 ms**.
Both verified by the dispatcher, not just reported. **Always run the GPU suite
under `timeout`.**

**CI is green on Linux, Windows and macOS** on this exact commit — 562 tests
on each (run 34027380886).

- `gh` is authenticated and git has a credential helper, so you can push. The
  token has `workflow` scope.
- **Stale worktrees**: two old unrelated ones (`agent-a0ef1435...`,
  `agent-a15af9a8...`) and `meshwright.worktrees/progress-check-inquiry`
  predate all recent work — leave them alone unless you know what they are.

## What last session did

A UX pass on the real GUI found twelve defects; ten are now fixed, in two
parallel dispatches, plus the additive `docs/usage.html` pass and the
Meshmixer research. Backlog items 19, 20 and 21 are closed; §11 gained eleven
rows. `reports/M4/20260905T221759Z-ux-audit/` has the 112 screenshots.

The one finding worth carrying forward as a lesson: **two agents hit the same
wall independently**, one drilling a drain hole and one opening an uncapped
cut, and that is what exposed `DMesh3.Copy` never advancing `Timestamp` —
which silently staled `CachedIsClosed` and made *any* newly opened geometry
invisible to hole detection across nine call sites. Neither would have found
it alone; each would have written a local workaround. Fixed centrally in the
vendored file, deviation recorded in `VENDOR.md`, and the drain-hole
workaround then removed to prove the central fix carries its 64 tests.

**Item 5 (Meshmixer research) is only partially done.** Reddit was unreachable
to the agent's tooling and Autodesk's forum 403s, so "read a week of threads"
was not met — ~18 pages, no hobbyist voices in their own words. Treat its
conclusions as directional. See `reports/research/meshmixer-alternatives.md`.

## Two decisions waiting on the user

Neither is an agent's to make; both change what v1.0 contains.

1. **Item 23, the Viewport / UX gap.** Build the missing block for 1.0, or
   move it to §5.2?
2. **Registration pins.** §3 names "adding registration pins" as a target-user
   workflow while §5.2 defers pins to v1.x, and plane-cut splitting is already
   v1.0. Promote a minimal peg-and-socket pair into §5.1? Proposed wording is
   in the research report.

## Backlog

**22. Decimation introduces the invalid geometry it says it declined to
create** — 67 self-intersections from a clean mesh, while the summary claims
further collapses would have created invalid geometry. The local validity test
each collapse passes has to be checked against whole-mesh invariants
afterwards. **Opus.** Best remaining item.

**24. Hole filling and hole detection disagree about import seams.**
`BoundaryHoleDetector` excludes seams via `PositionTopology.SeamEdges`;
`HoleFillRepair` finds loops with `MeshBoundaryLoops`, which is vertex-index
based, so Inspect can report zero holes on a file whose Auto Repair adds
geometry across a seam. Note this is a *different* bug from the `DMesh3.Copy`
one that was just fixed, though both made openings behave oddly. **Sonnet.**

**Two verification gaps left by last session's fixes**, worth closing cheaply
before building on them:
- The drain-hole **refusal path** (a hole too big for the surface) has two
  tests but no on-screen evidence — synthetic clicks landed in the other
  agent's window mid-batch.
- **Never run two GUI-driving agents at once again.** XTest pointer events go
  to whichever window has focus, and `pkill -f "Meshwright[.]App"` is not
  scoped to your own process — each agent killed the other's window at least
  once. Serialise GUI work, or give one agent the display and the other
  headless tasks.

**23** needs the scope decision above before any dispatch.

**Packaging follow-ups** stay deferred: §9 still reads "Linux + Windows first;
macOS once there is revenue". An MSI is buildable on the `windows-latest`
runner but unverifiable from this host, and there is no tagged release yet.

Push freely; the user has given standing authorisation. Ask before starting
anything not on this list.
