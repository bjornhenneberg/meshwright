# Next session brief — Meshwright

Paste this as the opening prompt of the next Claude Code session. It is a
pointer into `SPECIFICATION.md` rather than a second source of truth — when the
two disagree, the spec wins, and this file should be rewritten or deleted once
its backlog is done.

---

You are running as **Opus, in a dispatcher role**, on the Meshwright repo
(`/home/bjorn/Code/meshwright`) — a cross-platform desktop tool for repairing
meshes for 3D printing (C# / .NET 10 / Avalonia / Silk.NET, geometry on a
vendored g3Sharp).

**Read `SPECIFICATION.md` first.** §5.1 is v1.0 scope, §7 narrates each
milestone batch (read the M4-8 and M4-9 entries — they're the reason for how
this session works), §11 is a dated decision log, and "Immediate next steps"
at the end is the backlog. Items 1–18 are all done and struck through. What's
left is below.

## How to work

Do not implement the backlog yourself. **Dispatch each task to a subagent,
then personally verify the result.** Pick the model by the kind of judgment
the task needs:

- **Haiku** — mechanical, fully specified: doc sync, inventory sweeps, the
  screenshot retake below.
- **Sonnet** — well-scoped implementation against an existing pattern, with
  an obvious acceptance test. Most of what's left is this.
- **Opus** — algorithmic/topological reasoning where being subtly wrong looks
  like success. Nothing currently open needs this tier.

**Known hazard: subagent worktrees branch from this session's *starting*
commit, not live `main`.** Last session, two agents dispatched *after* other
merges landed were still working from seven commits back, silently editing
code that had already been rewritten, and their baseline test counts didn't
match because of it. If you land anything mid-session and then dispatch more
work, tell each new agent the exact commit `main` is at and ask it to confirm
its worktree matches before it starts, or have it rebase onto `main` before
reporting done.

## How to verify

The recurring failure mode in this codebase is **work that reports success
while being wrong** — §11 has a long, growing list of instances, most
recently: tests asserting `point.Y == 1` after a Z-up decision had already
been made, and a "before/after" readout that compared a mutated mesh with
itself. So, for each returned task:

1. Build, run `dotnet test tests/Meshwright.Tests -c Release`. Baseline is
   **520 passing, 0 skipped**. Never accept a newly skipped test without an
   explicit reason.
2. Ask what invariant would catch this being wrong, and check the test
   asserts that — not merely that the operation ran. Bounding box, volume,
   shell count and issue count, compared before and after, are what work for
   geometry.
3. **Run the actual app and look at it — unless the user has said not to.**
   A passing suite is not evidence a feature works; §11 has at least three
   rows about exactly this. Launch guidance is in memory under
   `reference-running-meshwright-gui` (`DISPLAY=:0`, the app takes a file path
   argument, `samples/broken-cube.stl` has one of every defect,
   `~/Downloads/Menger_sponge_sample.stl` is a clean 2112-triangle mesh with
   holes right through it — good for the cut/gizmo work already done,
   `~/Downloads/Eiffel_tower_sample.STL` is 139,989 triangles for the
   responsiveness invariant).
4. Treat a success message as a claim to check, not a result.

## State

Twelve commits landed last session (all on `main`, pushed — check
`git log --oneline origin/main..main` to confirm nothing is ahead). 520 tests
pass, 0 skipped. The 8 GPU tests were last run green at M4-8 and have not been
re-run since — the GPU suite hung past ten minutes last session, and two
already-hung GPU test hosts from earlier sessions were found on this machine,
so it looks environmental rather than code-caused, but this is unconfirmed
either way and worth a look before trusting it again.

**Stale worktrees**: `.claude/worktrees/agent-*` has six directories left over
from last session's dispatched agents, all already merged into `main` — safe
to `git worktree remove` if they're in the way. There are also two older,
unrelated worktrees (`agent-a0ef1435...`, `agent-a15af9a8...`) and a
`meshwright.worktrees/progress-check-inquiry` directory that predate last
session; leave those alone unless you know what they are.

## Backlog — everything left under "Immediate next steps"

Items 1–18 are done. What remains:

**Retake `docs/images/decimate.png`. (Haiku or Sonnet — needs the GUI.)**
It was captured from a mesh produced by the *old, broken* plane cut (fixed as
item 12), so it shows a model that had already lost geometry before the
decimation screenshot was taken. The message it illustrates is still correct;
the mesh in the picture is not. Retake it against current `main`.

**Windows/macOS CI + packaging. (Sonnet, but check the premise first.)**
Still open under M4-3. Deliberately deferred in earlier sessions because
neither platform could be verified on this dev host (Linux Mint). Check
whether that constraint still holds before starting — if it does, this may
be better scoped as "write the CI config and packaging scripts, flag them as
unverified" rather than claiming they work.

**Investigate the hung GPU suite.** Not yet a numbered item — read
`tests/Meshwright.Tests.Gpu`, figure out why `dotnet test` on it hangs past
ten minutes on this host, and whether the two already-hung test host
processes found last session are a clue or a coincidence. Decide whether it's
a real regression, an environment issue, or a pre-existing flake, and write
up what you find (add a §11 row if it's worth one) before deciding whether
it's worth an "Immediate next steps" item of its own.

**Also worth doing, not yet in the spec:**
- A broader UX pass: with 12–18 all landed, this is a good point to sit down
  with the app for a while (real GUI, no synthetic scripting) and look for
  the next tier of "reports success while being wrong" — the same instinct
  that found items 17 and 18 last session.
- Re-read `docs/usage.html`'s "Known rough edges" section end to end against
  what the app actually does now; several entries were touched piecemeal
  across last session's fixes and are due a coherent pass.

Ask before pushing (should already be pushed from last session — verify, but
don't assume), and before starting anything not on this list.
