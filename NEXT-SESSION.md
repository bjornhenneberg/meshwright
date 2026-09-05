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

- **GPU suite: 8 passing, ~0.5 s.** It used to hang past ten minutes; the
  cause was xunit running three `IClassFixture<GpuTestFixture>` classes in
  parallel against a `Glfw.CreateWindow` path that is not thread-safe on
  Linux. **Always run it under `timeout`** — a hang orphans a test host that
  outlives the session, and five had accumulated on this machine, one for 22
  hours.
- `gh` is authenticated and git has a credential helper, so you can push. The
  token needed `workflow` scope added to push `.github/workflows/` changes;
  it has it now.
- **Stale worktrees**: two old unrelated ones (`agent-a0ef1435...`,
  `agent-a15af9a8...`) and `meshwright.worktrees/progress-check-inquiry`
  predate all recent work — leave them alone unless you know what they are.

## Backlog

Nothing is blocked or half-finished. Pick from these.

**1. A broader UX pass. (Opus or Sonnet, needs the real GUI.)**
The most valuable thing left. With items 12–18 and M4-3 all landed, sit down
with the app for a while — real GUI, no synthetic scripting — and hunt the
next tier of "reports success while being wrong". That instinct found items
17 and 18, and last session it caught a docs page describing two fixed bugs
as current. Launch guidance is in memory under
`reference-running-meshwright-gui` (`DISPLAY=:0`, the app takes a file path
argument). `samples/broken-cube.stl` has one of every defect,
`~/Downloads/Menger_sponge_sample.stl` is clean with holes right through it,
`~/Downloads/Eiffel_tower_sample.STL` is 139,989 triangles for the
responsiveness target in §6.4.

**2. Finish the `docs/usage.html` rough-edges pass. (Sonnet.)**
All 15 entries were verified against source and the two stale ones removed,
so the *subtractive* half is done — do not re-audit those 13. What remains is
additive: find real current limitations missing from the list entirely.
Ground every claim in code actually read.

**3. Item 5, still open since the beginning. (Sonnet.)**
"Read a week of *Meshmixer alternative* threads and turn them into a
prioritised feature list to check against §5.1." The only original backlog
item never started, and the one most likely to change what v1.0 should
contain. Worth doing before more features get built on assumption.

**4. Packaging follow-ups, deliberately deferred.**
An MSI for Windows (currently a zip) and macOS signing/notarisation. §9 defers
notarisation until there is revenue, so confirm that still holds before
starting — this may stay deferred.

Push freely; the user has given standing authorisation. Ask before starting
anything not on this list.
