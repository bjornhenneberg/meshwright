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

### Agent A — GPU suite hang: DIAGNOSED, FIXED AND VERIFIED. Nothing left.

Closed out. `tests/Meshwright.Tests.Gpu/AssemblyInfo.cs` adds
`[assembly: CollectionBehavior(DisableTestParallelization = true)]`, and the
suite now runs **8 passing, 0 skipped in 483 ms** instead of hanging past ten
minutes. Cause: three test classes each took their own
`IClassFixture<GpuTestFixture>`, xunit ran the collections in parallel, and
concurrent `Glfw.CreateWindow` calls race because GLFW/GLX window creation on
Linux is not thread-safe. Managed stacks are kept in `reports/M4/gpu-hang/`.
§11 has the row; the §7 header claim about GPU tests being unre-run is fixed.

**Standing lesson: always run the GPU suite under `timeout`.** A hang orphans
a test host that survives the session — five had piled up on this machine, one
for 22 hours. All five are now killed.

### Agent B — Windows/macOS CI + packaging: LANDED, UNVERIFIED BY DESIGN

Done and verified as far as this host allows. Two new CI jobs
(`build-and-test-windows`, `build-and-test-macos`) build Manifold from source
per-platform and run `Meshwright.Tests` only (GPU suite excluded — no runner
GPU). `scripts/package-windows.sh` (zip) and `scripts/package-macos.sh`
(unsigned `.app` zip) added; no MSI, no notarisation.

**Nothing in the Windows/macOS path has ever executed.** That is the honest
state and §11 records it. **The next step for this item is to push and read
the first real CI run.** If the native-build scripts fail, suspect the CMake
generator/target-name assumptions; if Manifold builds but the boolean tests
fail, suspect the DllImport naming convention
(`libmanifoldc.dll`/`libmanifoldc.dylib`, assumed to match .NET's default
probing so that no `SetDllImportResolver` is needed — untested).

Two real bugs were caught by verifying rather than assuming:
- `Directory.Build.props` bundled the Linux `.so` into Windows/macOS
  publishes. Now RID-gated, and checked **in both directions**: a `win-x64`
  publish now carries zero natives, and a `linux-x64` publish and a plain
  build both still carry `libmanifoldc.so` + `libmanifold.so.3`. The
  reverse check is the one that matters — RID gating could easily have
  broken Boolean on Linux, and the tests would still have passed if the
  loose copy in `bin/` had been stale.
- `fetch-corpus.sh` verified checksums with `sha256sum`, absent on stock
  macOS. Both call sites treated a missing tool as a mismatch, so the macOS
  job would have failed with 53 bogus "checksum mismatch" lines blaming the
  corpus. Now falls back to `shasum -a 256`; both branches were exercised
  and the script re-run against the real corpus (53 present, 0 failed).

**First real CI run happened, and both new jobs failed — informatively.**
Run `33992294020`. Linux passed. macOS **built Manifold from source
successfully** and got as far as the corpus step, which settles the batch's
biggest unknown: CI really can build the native library per-platform.

Two failures, fixed in `30ffbcc` (unpushed as of writing — check
`git log --oneline origin/main..main`):

- **Windows: hardcoded CMake generator.** `windows-latest` is now the
  `windows-2025-vs2026` image, shipping **Visual Studio 2026 (18.9.x)**,
  CMake 4.4.2 and Ninja 1.13.2 — no VS 2022 at all, so
  `-G "Visual Studio 17 2022"` found nothing. Fixed by preferring Ninja and
  otherwise letting CMake choose its own newest-installed VS generator
  (`-A x64` only in the non-Ninja branch; Ninja rejects it). Deliberately
  *not* bumped to another hardcoded version — that only moves the breakage
  to the next image refresh.
- **macOS: all 53 corpus files reported "checksum mismatch". Root cause
  still not established.** Ruled out: the downloads are fine (a manifest URL
  was fetched here and its digest matched), upstream has not drifted, the
  manifest is clean LF with valid hashes, and the "no checksum tool" branch
  never fired. Rather than guess a third time, `fetch-corpus.sh` now computes
  the digest explicitly (`sha256sum` / `shasum -a 256` / `openssl`) and
  compares strings, dropping the `--check`/`--status` flag-compatibility
  surface, and reports **"could not compute digest"** separately from
  **"digest differs, expected X got Y"**. All four paths were exercised
  locally. If macOS still fails, the log will name the cause instead of
  repeating one opaque message.

**CI round 2 (run `33992998123`) got further, then round 3 fixed what it
exposed.** Both of round 2's unverifiable assumptions turned out fine:
CMake+Ninja *did* auto-detect MSVC with no vcvars (all 31 objects compiled,
both DLLs linked), and the macOS checksum rewrite *did* fix the corpus step.
Each round then failed one step later:

- **Windows: Ninja keeps the `lib` prefix.** The build succeeded but the
  post-build search looked for `manifoldc.dll`, the Visual Studio generator's
  spelling, while Ninja produced `libmanifoldc.dll`. Fixing the generator had
  changed the output naming — the script's own comment asserting "no `lib`
  prefix on Windows" was true only of the generator it no longer uses. Search
  now accepts either spelling, and the dependency is installed under whatever
  name it was actually built with.
- **macOS: 498/520 passed, 22 failed**, all Manifold boolean tests, with
  `DllNotFoundException: Unable to load shared library 'libmanifoldc' or one
  of its dependencies`. Cause confirmed from the link line
  (`-install_name @rpath/libmanifold.3.dylib`): CMake gives libmanifold a
  *versioned* install name, but the script copied whatever it found to a
  normalised `libmanifold.dylib`, so the exact filename libmanifoldc.dylib
  needs was never shipped. The Linux script had it right all along by
  preserving `libmanifold.so.3`; only macOS normalised. It now reads the
  required name straight off `otool -L` and installs under that name.

**The lesson worth keeping:** that macOS script printed
`==> Verified libmanifoldc.dylib has no absolute build-tree dependency path`
while shipping a library that could not load. It checked dependencies for
absolute paths but never checked they *existed*. It now asserts every
`@rpath`/`@loader_path` dependency resolves in the output directory — the
check was simulated both ways and does reject the exact case that shipped.
A "verified" message that cannot fail the way the bug actually occurs is
worth less than no message at all.

Round 3 (`abbde66`) is unverified for the same structural reason as every
round: no Windows or Mac host here. What it settles is only known after the
next run.

### Agent C — docs "Known rough edges" pass: DONE (after a false start)

Its first return was a placeholder with no work behind it; re-driven, it
produced a fully cited per-entry audit. The stale entries it found are already
fixed and committed (`74edf10`). See the backlog entry below for what is left.

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

**`docs/usage.html` "Known rough edges" — AUDITED AND PARTLY LANDED.**
All 15 entries were re-verified against source with file:line citations. The
two stale ones ("Reset View does nothing" and "Before/After figures are always
identical", both fixed by items 17 and 18) were removed in `74edf10`. The
other 13 were each confirmed still accurate and were left alone — including
Boolean having no way to reposition the secondary mesh, only Auto Repair
having real progress/cancel, OBJ import not welding, import splitting
non-manifold geometry, voxel remesh excluded from Auto Repair, and no
Windows/macOS packaging. What remains is the *additive* half: look for real
current limitations that are missing from the list entirely. Do not re-audit
the 13; they are settled.

**A broader UX pass.** With 12–18 landed, sit down with the real app for a
while (no synthetic scripting) and look for the next tier of "reports success
while being wrong" — the instinct that found items 17 and 18.

Ask before pushing, and before starting anything not on this list.
