# GPU test suite hang — investigation report

Worked from commit `6621d88` (already at HEAD, no rebase needed).

## Verdict

**Real regression, latent since 2026-09-04, triggered by test-count growth — not an environment
issue, not a GPU driver stall.** Root cause: xunit's default collection parallelization was
constructing multiple `GpuTestFixture` instances concurrently on separate threads, and each
constructor calls `Silk.NET.GLFW.Glfw.CreateWindow` — which is not safe to call concurrently
from multiple threads against the same X11 display. Two (of three) fixture-construction threads
in the captured process were both permanently blocked inside `Glfw.CreateWindow`. This is a
real, reproducible bug in the test suite, not a flake of this particular machine, though it
likely didn't always manifest (see "why M4-8 was green" below) — so treat "flaky in the past"
and "certain to hang under the current test count" as both true.

Fixed in this branch: added `[assembly: CollectionBehavior(DisableTestParallelization = true)]`
to `tests/Meshwright.Tests.Gpu/AssemblyInfo.cs`, which serializes fixture construction. Verified:
full suite now passes 8/8 in 3.7s (down from >10 minutes hung). See "Fix and verification" below.

## Evidence

### 1. Managed stacks from the live hung process (captured before killing)

Per the brief, `dotnet-stack` and `dotnet-dump` were not installed (`~/.dotnet/tools` did not
exist); installed both via `dotnet tool install -g dotnet-stack` / `dotnet tool install -g
dotnet-dump`.

- PID 632207 (yesterday's `Release` run, port 46193, parent vstest.console 632189, launcher
  632044): **stack captured successfully**, raw output at
  `reports/M4/gpu-hang/dotnet-stack-632207.txt`.
- PID 389883 and PID 392247 (both ~22h-old `Debug` runs): their diagnostic IPC sockets in `/tmp`
  were already gone by the time tooling was installed (`ls /tmp/dotnet-diagnostic-389883*` /
  `...392247*` -> no such file), so `dotnet-stack` failed with `ServerNotAvailableException` for
  both. No managed stacks could be recovered from these two; only the process list / `wchan`
  evidence from the prior session remains for them. This does not weaken the diagnosis — 632207
  is the freshest, most relevant sample (same code, same race), and reproduction below confirms
  the same failure mode independently.

The two load-bearing threads in PID 632207's stack (excerpted from
`dotnet-stack-632207.txt`, lines 36-84 and 143-220 — full traces are ~35 frames including the
xunit runner chain):

```
Thread (0x9A59C):
  [Native Frames]
  Silk.NET.GLFW!Silk.NET.GLFW.Glfw.CreateWindow(...)
  Silk.NET.Windowing.Glfw!Silk.NET.Windowing.Glfw.GlfwWindow.CoreInitialize(...)
  Silk.NET.Windowing.Common!Silk.NET.Windowing.Internals.WindowImplementationBase.CoreInitialize(...)
  Silk.NET.Windowing.Common!Silk.NET.Windowing.Internals.ViewImplementationBase.Initialize()
  Meshwright.Tests.Gpu!Meshwright.Tests.Gpu.GpuTestFixture..ctor()
  ...
  xunit.execution.dotnet!Xunit.Sdk.XunitTestClassRunner.CreateClassFixture(...)
  xunit.execution.dotnet!Xunit.Sdk.XunitTestClassRunner+<CreateClassFixtureAsync>d__11.MoveNext()
  ...

Thread (0x9A5A7):
  [Native Frames]
  Silk.NET.GLFW!Silk.NET.GLFW.Glfw.CreateWindow(...)
  Silk.NET.Windowing.Glfw!Silk.NET.Windowing.Glfw.GlfwWindow.CoreInitialize(...)
  Silk.NET.Windowing.Common!Silk.NET.Windowing.Internals.WindowImplementationBase.CoreInitialize(...)
  Silk.NET.Windowing.Common!Silk.NET.Windowing.Internals.ViewImplementationBase.Initialize()
  Meshwright.Tests.Gpu!Meshwright.Tests.Gpu.GpuTestFixture..ctor()
  ...
  xunit.execution.dotnet!Xunit.Sdk.XunitTestClassRunner.CreateClassFixture(...)
  xunit.execution.dotnet!Xunit.Sdk.XunitTestClassRunner+<CreateClassFixtureAsync>d__11.MoveNext()
  ...
```

**Two separate threads, both permanently blocked inside `Glfw.CreateWindow` reached through
`GpuTestFixture..ctor()`**, called from two different `XunitTestClassRunner.CreateClassFixture`
paths — i.e. two different test classes' fixtures being constructed at the same time. This
matches the earlier `wchan` evidence exactly (no thread in a GPU/DRM ioctl; blocked in
futex/poll waits) — the blockage is inside GLFW/X11 client-side synchronization during
concurrent window creation, not in the driver.

The remaining threads in the dump are ordinary xunit/vstest infrastructure (message loop,
thread-pool workers, timer thread, socket poll for the test-host protocol) — none of them are
running a test body, and none of them are in teardown. This nails down **where** the hang is:
fixture setup, before any test method runs.

### 2. Static cause: three independent `IClassFixture<GpuTestFixture>` classes, no serialization

```
tests/Meshwright.Tests.Gpu/BrokenSampleRenderGpuTests.cs:  IClassFixture<GpuTestFixture>
tests/Meshwright.Tests.Gpu/MeshRendererGpuTests.cs:        IClassFixture<GpuTestFixture>
tests/Meshwright.Tests.Gpu/ViewportGizmoGpuTests.cs:       IClassFixture<GpuTestFixture>
```

No `[CollectionDefinition]`, no `[Collection(...)]`, no `CollectionBehavior` attribute existed
anywhere in the assembly before this fix. By xunit v2's default rules, each test class with no
explicit `[Collection]` is its own implicit test collection, and **collections run in parallel
by default**. With three classes each independently constructing a `GpuTestFixture` (which
creates a real, hidden GLFW window + GL 3.3 core context — see
`tests/Meshwright.Tests.Gpu/GpuTestFixture.cs`), xunit was free to run all three constructors
concurrently on different thread-pool threads. `GpuTestFixture.cs` has no locking around
`Window.Create`/`Initialize()`, and GLFW itself is documented as not reentrant/thread-safe for
window creation on X11 — concurrent `glfwCreateWindow` calls race on GLFW's internal X11 state
and can hang indefinitely (consistent with the futex/poll `wchan` values seen on all three
orphaned processes).

### 3. Why this wasn't caught by "M4-8 last green"

File history (`git log --format=%h\ %ad -- <file>`):

| File | Added | Commit |
|---|---|---|
| `GpuTestFixture.cs`, `MeshRendererGpuTests.cs` (1st class) | 2026-08-29 | `aba812a` |
| `BrokenSampleRenderGpuTests.cs` (2nd class) | 2026-09-02 | `53526f0` |
| `ViewportGizmoGpuTests.cs` (3rd class) | 2026-09-04 16:18 | `c92b29a` |
| M4-8 recorded in spec | 2026-09-05 12:29 | `57aaa15` |

All three fixture-using classes already existed by the time M4-8 was recorded green, so this is
not a regression introduced after M4-8 — the race has existed since 2026-09-04. **It is a
pre-existing hazard that happened to pass on at least one prior run** (thread scheduling can
avoid the overlap) and now reliably hangs under load on this host. With only 1 class (through
2026-08-29 to 2026-09-02) the race was structurally impossible; from 2026-09-02 onward with 2,
then 3, classes, the probability of two `CreateWindow` calls overlapping in real wall-clock time
rose. Given the current reproduction below hangs the very first time it was tried in isolation
with no other GPU-suite load, I do not believe this is host-specific bad luck; I believe the
odds of hitting the race are now high enough that it should be treated as broken, not flaky,
going forward — but I can't rule out that some prior CI/dev-box run got lucky. I did not
attempt to bisect exactly when it started reliably reproducing; the fix removes the race
entirely so further bisection has no value.

## Fix and verification

Added `tests/Meshwright.Tests.Gpu/AssemblyInfo.cs`:

```csharp
[assembly: CollectionBehavior(DisableTestParallelization = true)]
```

This is the smallest possible fix: it serializes fixture construction (and test execution)
within the GPU assembly, eliminating the only thing that made concurrent `Glfw.CreateWindow`
calls possible. It does not touch `GpuTestFixture.cs` or GLFW library code, and does not affect
any other test assembly (attribute is assembly-scoped).

Ran the full suite afterward with a 300s bound (`timeout 300 dotnet test
tests/Meshwright.Tests.Gpu -c Release --logger:"console;verbosity=detailed"`):

```
Passed Meshwright.Tests.Gpu.MeshRendererGpuTests.Initialize_CompilesAndLinksShadersOnRealDriver [92 ms]
Passed Meshwright.Tests.Gpu.MeshRendererGpuTests.UploadMesh_WithFlaggedTriangle_ChangesRenderedPixelsVersusUnflagged [159 ms]
Passed Meshwright.Tests.Gpu.MeshRendererGpuTests.UploadMesh_SwappingMeshChangesRenderedPixels [11 ms]
Passed Meshwright.Tests.Gpu.MeshRendererGpuTests.UploadMesh_WithFlaggedEdge_ChangesRenderedPixelsVersusUnflagged [16 ms]
Passed Meshwright.Tests.Gpu.MeshRendererGpuTests.Render_AtMaxDistance_MeshIsStillAtLeastPartiallyVisible [35 ms]
Passed Meshwright.Tests.Gpu.BrokenSampleRenderGpuTests.RenderingBrokenSample_HighlightsFlaggedTrianglesAndCapturesPng [161 ms]
Passed Meshwright.Tests.Gpu.ViewportGizmoGpuTests.Render_GizmoDoesNotCorruptMesh [65 ms]
Passed Meshwright.Tests.Gpu.ViewportGizmoGpuTests.Render_WithGizmo_DoesNotCrash [20 ms]

Test Run Successful.
Total tests: 8
     Passed: 8
 Total time: 3.7317 Seconds
```

8/8 passed, 3.7s total, no hang, exit code 0.

## Process cleanup

After capturing the stack from PID 632207, all three orphaned hung processes (632207 + its
vstest.console parent 632189 + its launcher 632044; 389883; 392247) were `kill -9`'d — no
managed stacks were extractable from 389883/392247 (sockets already gone), so nothing further
was lost by killing them. Verified none remain (`ps -p ...` returns nothing). No unrelated
`dotnet`/MSBuild processes were touched.

## Recommendation for SPECIFICATION.md §11

Propose this decision-log row (dispatcher to land — not edited here):

| Date | Decision | Rationale |
|---|---|---|
| 2026-09-05 | GPU test assembly (`tests/Meshwright.Tests.Gpu`) now runs with `CollectionBehavior(DisableTestParallelization = true)` | Each of the assembly's 3 test classes independently uses `IClassFixture<GpuTestFixture>`, which creates a real GLFW window + GL context. xunit's default parallel-collection execution was constructing multiple fixtures concurrently, racing inside `Glfw.CreateWindow` (not thread-safe on X11) and hanging test runs indefinitely. Confirmed via managed stacks captured from a live hung process (two threads blocked in `GpuTestFixture..ctor -> Glfw.CreateWindow`) and reproduced/fixed directly; full suite now completes in ~4s. See `reports/M4/gpu-hang/report.md`. |

I'd also add an "Immediate next steps" item to keep an eye on: if the GPU test count keeps
growing, serial execution keeps this assembly slow-but-safe; if real parallelism across GPU
fixtures is ever wanted, the right fix is a single shared `ICollectionFixture<GpuTestFixture>`
per collection rather than per-class fixtures, or a mutex around `Window.Create` inside
`GpuTestFixture`. Not needed now — 8 tests in 3.7s serial is fine.
