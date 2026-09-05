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

### Windows/macOS CI: GREEN ON ALL THREE PLATFORMS. Closed.

`.github/workflows/ci.yml` builds and tests on Linux, Windows and macOS; the
two new jobs build Manifold from source in-job, and all 520 tests pass on
each. Packaging exists for all three. Took four CI rounds, each failing one
step later than the last:

1. Windows hardcoded the `Visual Studio 17 2022` generator; `windows-latest`
   is now `windows-2025-vs2026` (VS 2026, CMake 4.4.2, Ninja 1.13.2).
2. macOS has no `sha256sum`; `fetch-corpus.sh` treated the missing tool as a
   checksum mismatch and blamed the corpus for 53 bogus failures.
3. Switching to Ninja changed the output naming — Ninja keeps CMake's `lib`
   prefix, so the post-build search for `manifoldc.dll` missed the
   `libmanifoldc.dll` that had just been built. Fixing one failure caused
   the next.
4. macOS normalised libmanifold's *versioned* install name
   (`@rpath/libmanifold.3.dylib`) to a flat `libmanifold.dylib`, so the exact
   filename the loader wanted was never shipped.

Then the real one: **all 22 Manifold tests still failed on macOS with the
dylib sitting correctly in `runtimes/osx-arm64/native/`.** CI diagnostics
eliminated every explanation — arm64, ad-hoc signed, `@loader_path` on its
`LC_RPATH`, dependency resolving, and `ctypes.CDLL` of that exact path
printing `dlopen OK`. The library was always loadable; **.NET never looked
there.** `runtimes/<rid>/native/` is a deps.json contract and these loose
files are not in deps.json, so the Linux behaviour was incidental, not
guaranteed. Fixed by also copying the natives flat into the application base
directory, which `DllImport` always searches.

**Method worth reusing:** that fix was verified without a Mac. Hiding
`runtimes/` in the Linux test output reproduces the macOS condition exactly —
the interop tests failed that way before the change and passed on the flat
copy after. When a platform is unavailable, look for the local configuration
that reproduces its constraint.

**Also worth reusing:** rounds 2-4 were each diagnosed by making the failing
thing *report* rather than guessing at it — a self-diagnosing checksum, then
a CI step that printed the native layout and asked dyld directly. Two guesses
had already been wrong by then.

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
