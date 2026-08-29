# M0 Batch 4 Verification — Full Skeleton Integration

Timestamp: 2026-08-29T20:02:48Z
Scope: `src/Meshwright.App/Views/MeshViewportControl.cs`, `src/Meshwright.App/MainWindow.axaml(.cs)`,
`src/Meshwright.App/Assets/SampleMesh.stl`, wiring of Avalonia + Silk.NET GL + `OrbitCamera` + `StlReader`.
This is the final M0 milestone batch (§7 M0: "Solution structure, Avalonia window, Silk.NET viewport
rendering a loaded STL with orbit/pan/zoom").

## Verdict: Verified with one caveat (GL rendering unverifiable on this host — expected/documented, not a defect)

## Checks

### 1. `dotnet build` on the full solution — PASS
0 Warning(s), 0 Error(s). Raw output: [build.log](build.log).

### 2. `dotnet test` — PASS
`Total tests: 22, Passed: 22, Failed: 0`. Raw output: [test.log](test.log).
Includes the two headless-Avalonia tests (`MainWindowTests.Constructing_MainWindow_DoesNotThrow`,
`MainWindowTests.SampleMeshResource_IsEmbeddedAndParsesAsStl`) plus all camera/STL/vertex-builder/
scaffold tests from earlier batches — nothing regressed.

### 3. `MeshViewportControl.cs` — PASS
Read in full at [MeshViewportControl.cs](../../../src/Meshwright.App/Views/MeshViewportControl.cs).
- Derives from `Avalonia.OpenGL.Controls.OpenGlControlBase` (`sealed class MeshViewportControl :
  OpenGlControlBase`).
- `OnOpenGlInit(GlInterface gl)` constructs the Silk.NET GL binding via
  `Silk.NET.OpenGL.GL.GetApi(gl.GetProcAddress)` — binds to Avalonia's own proc-address resolver
  rather than creating an independent GL context. Confirmed no other `GL.GetApi`/context-creation
  call exists in the file.
- `OnOpenGlRender(GlInterface gl, int fb)` binds the framebuffer Avalonia hands back
  (`_gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)fb)`) before setting viewport/clear/draw
  state, with an inline comment citing why (`OpenGlControlBase` does not bind it for you). This
  matches Avalonia's own OpenGL sample pattern.
- `OnOpenGlDeinit(GlInterface gl)` disposes the renderer and nulls both `_renderer` and `_gl`.
- Pointer/scroll wiring:
  - Left-drag → orbit (`_isOrbiting`), unless Shift is held, in which case it pans instead
    (`bool panModifier = e.KeyModifiers.HasFlag(KeyModifiers.Shift)`).
  - Middle- or right-button drag → pan unconditionally.
  - `OnPointerMoved` computes delta since last position and calls `_camera.Orbit(dx, -dy)` or
    `_camera.Pan(dx, dy)` scaled by tuned sensitivity constants.
  - `OnPointerWheelChanged` calls `_camera.Zoom(...)` scaled by current `_camera.Distance` so the
    step feels consistent at any zoom level.
  - Pointer capture is taken on press and released on release, so drags continue correctly outside
    the control bounds.
  - This is a reasonable, idiomatic button/modifier mapping for orbit/pan/zoom; no defects found.

### 4. `MainWindow.axaml.cs` — PASS
Read in full at [MainWindow.axaml.cs](../../../src/Meshwright.App/MainWindow.axaml.cs).
- Constructor calls `LoadSampleMesh()` after `InitializeComponent()`.
- `LoadSampleMesh()` pulls the embedded resource `Meshwright.App.Assets.SampleMesh.stl` via
  `Assembly.GetExecutingAssembly().GetManifestResourceStream(...)`, parses it with `StlReader.Read`,
  assigns the result to `Viewport.Mesh`, and updates `StatusText` with the triangle count. Falls back
  to a status message (not a crash) if the resource is missing.
- `OnOpenFileClick` uses `TopLevel.GetTopLevel(this)?.StorageProvider` (an `IStorageProvider`),
  calls `OpenFilePickerAsync` with an `*.stl` file-type filter, reads the picked file, parses it with
  `StlReader.Read`, and reassigns `Viewport.Mesh` — wrapped in a try/catch that reports failures via
  `StatusText` instead of throwing. This is a complete, working open-file path.
- `MainWindow.axaml` confirms `Viewport` is a `views:MeshViewportControl` and `OpenFileButton` is
  wired to `OnOpenFileClick`, so the code-behind matches the markup.

### 5. Sample STL asset — PASS
`file` reports `src/Meshwright.App/Assets/SampleMesh.stl` as ASCII text; manual inspection shows a
well-formed ASCII STL (`solid sample_tetrahedron` ... four `facet normal` / `outer loop` / `vertex`×3
/ `endloop` / `endfacet` blocks). The `Meshwright.App.csproj` embeds it
(`<EmbeddedResource Include="Assets\SampleMesh.stl" />`).
The test `MainWindowTests.SampleMeshResource_IsEmbeddedAndParsesAsStl` does assert something
meaningful, not just "doesn't throw": it asserts `mesh.TriangleCount == 4`, which matches the actual
tetrahedron geometry in the file (4 facets) — confirmed by counting `facet normal` occurrences in the
asset and cross-checking against the parsed count.

### 6. Visual evidence attempt (Avalonia.Headless) — PARTIAL / DOCUMENTED GAP
- Ran `dotnet test --filter MainWindowTests` directly: both tests pass in isolation
  (see [test.log](test.log), full-suite run includes them; a standalone filtered run was also
  executed and observed passing).
- Investigated the installed `Avalonia.Headless` 11.3.20 package via reflection and found
  `HeadlessWindowExtensions.CaptureRenderedFrame`/`GetLastRenderedFrame` and
  `AvaloniaHeadlessPlatform.ForceRenderTimerTick` exist. The project's shared `TestAppBuilder` uses
  `UseHeadlessDrawing = true` (implicit default) via a bare `AvaloniaHeadlessPlatformOptions`, which
  Avalonia explicitly refuses to capture from (`NotSupportedException`: requires `.UseSkia()` and
  `UseHeadlessDrawing = false`). Rather than mutate the shared `TestAppBuilder` used by every other
  test in the suite (out of scope for this verification pass, and not something I should do per the
  "don't fix bugs beyond trivial typos" constraint), I built a **separate, disposable** console app
  referencing `Meshwright.App` with its own `AppBuilder` (`.UseSkia()` +
  `UseHeadlessDrawing = false`), constructed `MainWindow`, forced render ticks while draining
  `Dispatcher.UIThread`, and captured `GetLastRenderedFrame()` to PNG. That scratch app was deleted
  after use; no files under the repo were left behind by this step.
- Screenshot captured: [mainwindow_headless_frame.png](mainwindow_headless_frame.png) — shows the
  window chrome rendering correctly (the "Open STL..." button and the status text
  "Loaded sample tetrahedron (4 triangles)", proving `LoadSampleMesh()` ran and the STL parsed with
  the expected triangle count end-to-end). The `MeshViewportControl` area itself renders as blank
  white, **not** the renderer's clear color (`0.15, 0.15, 0.18`) — i.e. `OnOpenGlInit`/`OnOpenGlRender`
  did not actually execute, because this dev host has no GPU/X11 display and Avalonia's headless Skia
  backend does not provide a real OpenGL context for `OpenGlControlBase` to bind into.
- **This is the expected, documented gap called out in AGENTS.md** ("no display server on CI/this dev
  host") — actual GL triangle rendering, and therefore true orbit/pan/zoom visual behavior, could not
  be verified pixel-by-pixel in this environment. It is not being silently skipped: the code path was
  reviewed line-by-line in check 3 above and is structurally correct (proc-address binding, framebuffer
  binding, dispose, input wiring), and the surrounding UI/data pipeline (STL load → `Viewport.Mesh` →
  status text) was confirmed to execute correctly end-to-end via the captured frame. Full GL
  correctness would require a machine with a real GPU/X11 (e.g. Xvfb + software GL) to close this gap.

### 7. Scope check against SPECIFICATION.md §7 M0 — PASS
M0 is defined as: "Solution structure, Avalonia window, Silk.NET viewport rendering a loaded STL with
orbit/pan/zoom." Reviewed all changed/new files in this batch
(`MeshViewportControl.cs`, `MainWindow.axaml`, `MainWindow.axaml.cs`, `Meshwright.App.csproj`,
`Meshwright.Tests.csproj`, `MainWindowTests.cs`, `TestAppBuilder.cs`, `Assets/SampleMesh.stl`):
- No mesh analysis/error-highlighting (M1), no repair operations or undo stack (M2), no edit tools —
  plane cut, booleans, transforms, hollow, drain holes, decimation (M3), and no packaging/CLI work
  (M4/`Meshwright.Cli`) appear anywhere in this batch's diff. `git status` confirms no changes under
  `Meshwright.Core`, `Meshwright.Geometry`, or `Meshwright.IO` beyond what earlier batches already
  introduced.
- The `Meshwright.App.csproj`/`Meshwright.Tests.csproj` diffs add only the embedded-resource wiring
  and the `Avalonia.Headless`/`Avalonia.Headless.XUnit` test packages needed for `MainWindowTests` —
  nothing unrelated.

## Files in this report
- [build.log](build.log) — full `dotnet build Meshwright.sln` output.
- [test.log](test.log) — full `dotnet test Meshwright.sln` output (22/22 passed).
- [mainwindow_headless_frame.png](mainwindow_headless_frame.png) — headless Skia-rendered frame of
  `MainWindow` showing UI chrome + sample-mesh status text; GL viewport area blank (documented gap).

## Unverifiable / known gaps
- True OpenGL rendering (clear color, uploaded mesh geometry, and the visual effect of
  orbit/pan/zoom) could not be observed pixel-by-pixel: this host has no GPU/X11 display, and
  Avalonia's headless Skia backend does not provide `OpenGlControlBase` a real GL context to render
  into. This matches AGENTS.md's explicit acknowledgment that there's no display server here.
  Verified instead by full code review of `MeshViewportControl.cs` (check 3) plus confirmation that
  everything upstream/downstream of the GL call (mesh loading, `Viewport.Mesh` assignment, camera
  math already unit-tested in batch 2/3) is wired and exercised correctly.
