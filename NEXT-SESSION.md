# Next session

You are running as **Opus, in a dispatcher role**, on the Meshwright repo
(`/home/bjorn/Code/meshwright`) — a cross-platform desktop tool for repairing
meshes for 3D printing (C# / .NET 10 / Avalonia / Silk.NET, geometry on a
vendored g3Sharp).

**Read `SPECIFICATION.md` first.** §5.1 is v1.0 scope, §7 narrates each
milestone batch, §11 is a dated decision log, and "Immediate next steps" at
the end is the backlog. Items 1–18 are done and struck through.

## THIS SESSION ENDED EARLY (session limit). Read this before anything else.

The previous session was cut short mid-flight. **Two dispatched agents were
still running when it ended** and their work was never verified or merged.
Establish what survived before starting anything new:

```bash
git log --oneline -3            # main was at 6621d88 when the session ended
git status --porcelain          # agent work-in-progress may sit here untracked
git branch -a --sort=-committerdate
```

**Both remaining agents were working directly in the main checkout, not in
worktrees** (confirmed — no new agent worktree or branch appeared for them).
So their output is loose in the working tree rather than isolated on a branch.
Expect uncommitted files. Nothing of theirs was committed by the dispatcher.

### Agent A — GPU suite hang investigation: DIAGNOSIS FOUND, NOT LANDED

This one substantially succeeded and its result is worth keeping. **Verified
independently by the dispatcher, not just claimed:**

- Managed stacks were captured from the live hung processes into
  `reports/M4/gpu-hang/dotnet-stack-{632207,389883,392247}.txt`.
- **Two separate threads are blocked inside
  `GpuTestFixture..ctor` -> `Silk.NET.Windowing.GlfwWindow.CoreInitialize`
  -> `Glfw.CreateWindow`** (lines 42-46 and 149-153 of
  `dotnet-stack-632207.txt`). The dispatcher grepped these directly.
- Cause: the assembly has three test classes each taking
  `IClassFixture<GpuTestFixture>`, and xunit runs collections in parallel by
  default, so multiple `GpuTestFixture` instances call `Glfw.CreateWindow`
  concurrently. GLFW/GLX window creation on Linux is not thread-safe; it
  races and hangs forever.
- Corroborating evidence gathered before dispatch: the hung hosts had used
  **22 s of CPU over 94 minutes** (blocked, not spinning), and every thread
  sat in `futex_do_wait`/`poll_schedule_timeout`/`ep_poll` with **no thread in
  a GPU or DRM ioctl** — which rules out a driver stall.
- This is an **environment/concurrency issue, not a geometry regression**, and
  it explains why the suite was green at M4-8: fewer GPU test classes then, so
  less opportunity to race.

The agent's proposed fix is `tests/Meshwright.Tests.Gpu/AssemblyInfo.cs`,
adding `[assembly: CollectionBehavior(DisableTestParallelization = true)]`
with a long explanatory comment. **This was never verified.** To finish:

1. Check whether `reports/M4/gpu-hang/report.md` was ever written (it had not
   been when the session ended).
2. **Run the suite with a bounded timeout so you never add another orphan:**
   `timeout 900 dotnet test tests/Meshwright.Tests.Gpu -c Release`.
   It must complete and report 8 passing. Serializing fixture construction is
   a plausible fix, but "plausible" is exactly what this codebase keeps
   getting burned by — confirm it actually terminates.
3. Consider whether the real fix is one *shared* fixture rather than three
   separate ones, since three windows are created for 8 tests regardless.
4. Land a §11 row. Suggested text:

   | 2026-09-05 | The GPU suite's hangs were xunit parallelism, not the GPU. Three test classes each took their own `IClassFixture<GpuTestFixture>`, and xunit runs collections in parallel, so several fixtures called `Glfw.CreateWindow` concurrently; GLFW/GLX window creation on Linux is not thread-safe and the race hangs forever. Managed stacks off a live hung host showed two threads stopped inside `GpuTestFixture..ctor`. Diagnosis needed the .NET diagnostic IPC sockets in `/tmp` — `ptrace_scope=1` blocks gdb from attaching to a non-descendant without sudo. The tell that it was never a driver stall: 22 s of CPU over 94 minutes, and no thread in a DRM ioctl |

**Housekeeping:** three orphaned GPU test hosts were alive on this machine
(PIDs 632207 from the prior session, 389883 and 392247 from ~22 h earlier).
They were kept alive deliberately as the only live instances of the bug. Once
the fix is confirmed they are safe to kill — check whether the agent already
did. Note `pkill -f "Meshwright.App"` kills the calling shell too; use
`pkill -f "Meshwright[.]App"`.

### Agent B — Windows/macOS CI + packaging: STATUS UNKNOWN

Never reported. Check the working tree for changes to `.github/workflows/`
and `scripts/`. It was told to check the premise first (neither platform is
verifiable on this Linux Mint host) and to scope the work as "write the config,
flag it explicitly as unverified" if the constraint still holds — that framing
is pre-approved. The crux it was asked to settle: the native Manifold
dependency. `manifoldc.dll`/`libmanifoldc.dylib` cannot be built here, so
either CI builds them per-platform or Boolean cannot ship on those platforms —
and the packaging must say so rather than shipping a package that crashes when
the user clicks Boolean. Treat anything it left behind as unreviewed.

### Agent C — docs "Known rough edges" pass: PRODUCED NOTHING

Returned a placeholder ("I'll wait for the background test run") without doing
the work. No branch, no edits. **Re-dispatch from scratch.** Task below.

## How to work

Do not implement the backlog yourself. **Dispatch each task to a subagent,
then personally verify the result.**

- **Haiku** — mechanical, fully specified: doc sync, inventory sweeps.
- **Sonnet** — well-scoped implementation against an existing pattern with an
  obvious acceptance test. Most of what's left is this.
- **Opus** — algorithmic/topological reasoning where being subtly wrong looks
  like success. Nothing currently open needs this tier.

**Two hazards, both hit this session:**
- Agents may branch from the session's *starting* commit rather than live
  `main`. Tell each agent the exact commit `main` is at and have it confirm
  its worktree matches before starting.
- **Agents may not use a worktree at all** and will edit the main checkout
  directly. Both surviving agents did. If you dispatch several at once, expect
  their edits to land in the same tree, and keep their file scopes disjoint.
  Telling every agent "do not edit SPECIFICATION.md, propose §11 wording in
  your report instead" worked well and is worth repeating — the dispatcher
  lands spec changes centrally.

## How to verify

The recurring failure mode here is **work that reports success while being
wrong** — §11 has a long list. For each returned task:

1. Build, run `dotnet test tests/Meshwright.Tests -c Release`. Baseline is
   **520 passing, 0 skipped** — re-confirmed this session. Never accept a
   newly skipped test without an explicit reason.
2. Ask what invariant would catch this being wrong, and check the test asserts
   that, not merely that the operation ran. Bounding box, volume, shell count
   and issue count, before vs. after, are what work for geometry.
3. **Run the actual app and look at it** unless told not to. Launch guidance is
   in memory under `reference-running-meshwright-gui` (`DISPLAY=:0`, app takes
   a file path argument). `samples/broken-cube.stl` has one of every defect,
   `~/Downloads/Menger_sponge_sample.stl` is a clean 2112-triangle mesh with
   holes through it, `~/Downloads/Eiffel_tower_sample.STL` is 139,989
   triangles for the responsiveness invariant.
4. Treat a success message as a claim to check, not a result. Grepping the
   agent's own evidence files took under a minute this session and turned an
   assertion into a fact.

## State

`main` at `6621d88`, clean and level with `origin/main` — last session's twelve
commits are pushed. 520 tests pass, 0 skipped.

**Stale worktrees**: `.claude/worktrees/agent-*` has six directories from an
earlier session, all merged into `main` — safe to `git worktree remove`. Two
older unrelated ones (`agent-a0ef1435...`, `agent-a15af9a8...`) and
`meshwright.worktrees/progress-check-inquiry` predate that; leave them alone.

## Backlog

**Finish Agent A and Agent B above** — that is the top of the list.

**Retake `docs/images/decimate.png`. (Sonnet — needs the GUI.)**
Deliberately held all of this session so it would not fight the GPU work for
`DISPLAY=:0`; dispatch it when nothing else is using the display. It was
captured from a mesh produced by the *old, broken* plane cut (fixed as item
12), so it shows a model that had already lost geometry before the decimation
screenshot was taken. The message it illustrates is still correct; the mesh is
not. Retake against current `main`.

**Re-dispatch the `docs/usage.html` "Known rough edges" pass. (Sonnet.)**
Read the section end to end against what the app actually does now; entries
were touched piecemeal as items 12–18 landed and none has been reviewed as a
whole. Every claim kept or written must be traceable to code actually read.
Worth checking specifically: whether Boolean works at all on non-Linux
platforms, OBJ import deliberately not welding vertices, import splitting
non-manifold geometry at the offending vertices, voxel remesh being excluded
from Auto Repair, and that progress/Cancel is real only for
`AutoRepairPipeline` and an honest indeterminate spinner everywhere else.

**A broader UX pass.** With 12–18 landed, sit down with the real app for a
while (no synthetic scripting) and look for the next tier of "reports success
while being wrong" — the instinct that found items 17 and 18.

Ask before pushing, and before starting anything not on this list.
