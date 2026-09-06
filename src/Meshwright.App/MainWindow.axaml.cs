using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using g3;
using Meshwright.App.Gizmos;
using Meshwright.App.Views.Edit;
using Meshwright.Core;
using Meshwright.Core.Operations;
using Meshwright.Core.Settings;
using Meshwright.Geometry.Diagnostics;
using Meshwright.Geometry.Printing;
using Meshwright.IO;
using Meshwright.IO.Stl;
using Meshwright.IO.Units;
using Meshwright.Rendering.Camera;
using Meshwright.Rendering.Gizmos;
using Meshwright.Rendering.GL;

namespace Meshwright.App;

public partial class MainWindow : Window
{
    private const string SampleMeshResourceName = "Meshwright.App.Assets.SampleMesh.stl";

    private readonly MeshDocument _document = new();

    /// <summary>Where the remembered state lives. See <see cref="AppSettings"/>.</summary>
    private readonly SettingsStore _settingsStore;

    private readonly AppSettings _settings;

    /// <summary>
    /// The printer bed the viewport draws and the out-of-bounds warning measures against.
    /// Restored from and written back to <see cref="AppSettings.BuildVolumeName"/>.
    /// </summary>
    private BuildVolume _buildVolume = BuildVolume.Default;

    private bool _showBuildPlate = true;

    /// <summary>
    /// The window's size and position as it last was while <em>not</em> maximized. Avalonia does
    /// not expose a maximized window's restore bounds, and <see cref="Window.Position"/> and
    /// <see cref="Layoutable.Width"/> read back the maximized geometry, so remembering them at
    /// close time would leave a user who maximizes once with a window permanently the size of
    /// their screen. This is updated only while the state is Normal.
    /// </summary>
    private WindowPlacement? _normalPlacement;

    /// <summary>
    /// The mm/inch offer currently on screen, or null when the bar is hidden. Only ever an offer:
    /// nothing is scaled until <see cref="OnAcceptUnitSuggestionClick"/> runs.
    /// </summary>
    private UnitScaleSuggestion? _pendingUnitSuggestion;

    // Cross-section preview state. The position is a world coordinate in millimetres along
    // _crossSectionAxis, not a fraction of the model, so the number beside the slider is the
    // number a user could measure.
    private bool _showCrossSection;
    private CrossSectionAxis _crossSectionAxis = CrossSectionAxis.Z;
    private bool _crossSectionFlipped;
    private double _crossSectionPosition;

    /// <summary>Guards the slider's own PropertyChanged while <see cref="RefreshCrossSection"/>
    /// writes the range and value back into it, so rescaling for a new mesh cannot be mistaken
    /// for the user dragging.</summary>
    private bool _updatingCrossSectionControls;

    // Gizmos for interactive operations
    private DrainHoleGizmo? _drainHoleGizmo;
    private PlaneCutGizmo? _planeCutGizmo;
    private TransformGizmo? _transformGizmo;
    private HollowGizmo? _hollowGizmo;

    /// <summary>The currently active panel's "reset your own gizmo-active UI state" method, so
    /// it can be told to stand down when a different panel takes the single viewport gizmo
    /// slot (see <see cref="ActivateGizmoOwner"/>).</summary>
    private Action? _deactivateCurrentGizmoOwner;

    public MainWindow()
        : this(null)
    {
    }

    /// <summary>
    /// <paramref name="settingsStore"/> lets a test point the persisted state at its own file.
    /// Left null, the real one in the platform config directory is used — which is also what the
    /// unit suite gets, via the <c>MESHWRIGHT_SETTINGS_FILE</c> override its module initializer
    /// sets, so that running the tests can never read or rewrite the developer's own settings.
    /// </summary>
    public MainWindow(SettingsStore? settingsStore)
    {
        _settingsStore = settingsStore ?? new SettingsStore();
        _settings = _settingsStore.Load();
        _buildVolume = ResolveBuildVolume(_settings.BuildVolumeName);
        _showBuildPlate = _settings.ShowBuildPlate;

        InitializeComponent();
        RestoreWindowPlacement();
        InitializeEditPanels();
        InitializeBuildPlateMenu();
        InitializeCrossSectionControls();
        InitializeDragAndDrop();
        RefreshRecentFilesMenu();

        // Every mesh change refreshes the UI from one place. The Edit panels apply their
        // operations straight to the document, so without this they'd change the mesh with
        // nothing on screen moving — the viewport and diagnostics would sit on pre-operation
        // state until an unrelated load/undo/redo happened to refresh them.
        _document.Changed += (_, _) =>
        {
            RefreshFromDocument();
            SetStatus(_document.LastChangeDescription ?? "Updated");
        };

        // One place to keep a second operation from starting while one is running, grey out
        // Undo/Redo, and show/hide the busy indicator (§6.3, backlog item 13) — every Edit
        // panel calls MeshDocument.ApplyAsync directly rather than routing through here, so this
        // is the one subscription that has to catch all of them, mirroring the Changed handler
        // above (§11, 2026-09-05).
        _document.BusyChanged += (_, _) => RefreshBusyState();
        _document.Progress += (_, progress) => ShowOperationProgress(progress);

        LoadSampleMesh();
        RefreshBuildPlate();
        RefreshCrossSection();
        RefreshViewMenuChecks();

        // A settings file that could not be read is worth one sentence. Silently starting with
        // defaults is how a user loses their recent files and their printer and never finds out
        // which of the two of you dropped them.
        if (_settingsStore.LoadWarning is { } warning)
        {
            StatusText.Text = warning;
        }
    }

    /// <summary>Current text of the undo/redo status indicator, exposed for testing.</summary>
    public string? UndoRedoStatusMessage => UndoRedoStatusText.Text;

    /// <summary>Invokes the Undo menu action directly, bypassing the keyboard shortcut/menu
    /// click, for use by tests that can't drive UI input headlessly.</summary>
    public void TriggerUndoForTesting() => PerformUndo();

    /// <summary>Invokes the Redo menu action directly, bypassing the keyboard shortcut/menu
    /// click, for use by tests that can't drive UI input headlessly.</summary>
    public void TriggerRedoForTesting() => PerformRedo();

    /// <summary>Initialize all edit operation panels with the document and gizmos.</summary>
    private void InitializeEditPanels()
    {
        // Bind all panels to the document
        RepairPanel.SetDocument(_document);
        PlaneCutPanel.SetDocument(_document);
        TransformPanel.SetDocument(_document);
        HollowPanel.SetDocument(_document);
        DrainHolePanel.SetDocument(_document);
        DecimatePanel.SetDocument(_document);
        BooleanPanel.SetDocument(_document);

        // Create and wire up the drain hole gizmo
        _drainHoleGizmo = new DrainHoleGizmo(_document.Mesh ?? new DMesh3());
        DrainHolePanel.SetGizmo(_drainHoleGizmo);

        // Wire gizmo activation callbacks: when the panel wants to show/hide the gizmo,
        // update the viewport accordingly. Viewport.Gizmo is a single slot shared by all
        // three panels below, so activating one must force-deactivate whichever other
        // panel previously held it - otherwise that panel's button/status text keeps
        // claiming its gizmo is live after the viewport has silently moved on to a
        // different one. ActivateGizmoOwner (below) is the arbiter for that hand-off.
        DrainHolePanel.SetGizmoActivationCallback(
            onActivate: () => ActivateGizmoOwner(_drainHoleGizmo, DrainHolePanel.ForceDeactivateGizmo),
            onDeactivate: () => DeactivateGizmoOwner(DrainHolePanel.ForceDeactivateGizmo));

        // Create and wire up the plane cut gizmo
        _planeCutGizmo = new PlaneCutGizmo(ComputeMeshCenter(_document.Mesh));
        PlaneCutPanel.SetGizmo(_planeCutGizmo);

        PlaneCutPanel.SetGizmoActivationCallback(
            onActivate: () => ActivateGizmoOwner(_planeCutGizmo, PlaneCutPanel.ForceDeactivateGizmo),
            onDeactivate: () => DeactivateGizmoOwner(PlaneCutPanel.ForceDeactivateGizmo));

        // Create and wire up the transform gizmo (move/rotate/scale)
        _transformGizmo = new TransformGizmo(ComputeMeshCenter(_document.Mesh));
        TransformPanel.SetGizmo(_transformGizmo);
        TransformPanel.SetGizmoActivationCallback(
            onActivate: () => ActivateGizmoOwner(_transformGizmo, TransformPanel.ForceDeactivateGizmo),
            onDeactivate: () => DeactivateGizmoOwner(TransformPanel.ForceDeactivateGizmo));

        // Create and wire up the hollow shell-preview gizmo
        (Vector3 hollowAnchor, Vector3 hollowNormal) = HollowGizmo.ComputeSurfaceAnchor(_document.Mesh);
        _hollowGizmo = new HollowGizmo(hollowAnchor, hollowNormal, HollowGizmo.ComputeDefaultWallThickness(_document.Mesh));
        HollowPanel.SetGizmo(_hollowGizmo);
        HollowPanel.SetGizmoActivationCallback(
            onActivate: () => ActivateGizmoOwner(_hollowGizmo, HollowPanel.ForceDeactivateGizmo),
            onDeactivate: () => DeactivateGizmoOwner(HollowPanel.ForceDeactivateGizmo));
    }

    /// <summary>
    /// Hands the single <see cref="MeshViewportControl.Gizmo"/> slot to <paramref name="gizmo"/>.
    /// If a different panel currently holds it, that panel is told to reset its own
    /// "active" UI state first (via <paramref name="forceDeactivateSelf"/>) - without going
    /// through its own deactivation callback, which would just call back into this method
    /// and fight over the slot it's already losing.
    /// </summary>
    private void ActivateGizmoOwner(IViewportGizmo? gizmo, Action forceDeactivateSelf)
    {
        if (_deactivateCurrentGizmoOwner is not null && _deactivateCurrentGizmoOwner != forceDeactivateSelf)
        {
            _deactivateCurrentGizmoOwner();
        }

        Viewport.Gizmo = gizmo;
        _deactivateCurrentGizmoOwner = forceDeactivateSelf;
    }

    /// <summary>Clears the viewport gizmo slot, but only if the panel deactivating is the one
    /// that currently owns it (a panel that was already displaced by another one activating
    /// has nothing to clear).</summary>
    private void DeactivateGizmoOwner(Action forceDeactivateSelf)
    {
        if (_deactivateCurrentGizmoOwner != forceDeactivateSelf)
        {
            return;
        }

        Viewport.Gizmo = null;
        _deactivateCurrentGizmoOwner = null;
    }

    private static Vector3 ComputeMeshCenter(DMesh3? mesh)
    {
        if (mesh is null || mesh.TriangleCount == 0)
        {
            return Vector3.Zero;
        }

        Vector3d center = mesh.CachedBounds.Center;
        return new Vector3((float)center.x, (float)center.y, (float)center.z);
    }

    /// <summary>Diagnostics report for the currently loaded mesh, exposed for testing.</summary>
    public MeshDiagnosticsReport? CurrentReport => _document.Report;

    /// <summary>Current status bar text, exposed for testing.</summary>
    public string? StatusMessage => StatusText.Text;

    /// <summary>Current diagnostics summary text, exposed for testing.</summary>
    public string? SummaryMessage => SummaryText.Text;

    /// <summary>The Boolean operations panel, exposed for testing since it isn't otherwise
    /// reachable from outside the generated partial class.</summary>
    public BooleanPanel BooleanPanelForTesting => BooleanPanel;

    /// <summary>Loads a mesh file by path through the real load pipeline, bypassing the file
    /// picker dialog. Used for the command-line file argument and by integration tests that
    /// can't drive an OS file picker headlessly. Reports failure on the status line rather
    /// than throwing, since both callers are outside any user-visible error context.</summary>
    public void OpenFileFromPath(string path)
    {
        try
        {
            MeshImportResult import = MeshImporter.ImportFileWithDiagnostics(path);
            ApplyLoadedMesh(import.Mesh, StatusFor($"Loaded {Path.GetFileName(path)}", import));
            RememberRecentFile(path);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to load {Path.GetFileName(path)}: {ex.Message}";

            // A path that no longer opens should not keep its place at the top of the list; the
            // menu entry would otherwise go on failing every time it is picked.
            ForgetRecentFile(path);
        }
    }

    /// <inheritdoc cref="OpenFileFromPath"/>
    public void LoadFileForTesting(string path) => OpenFileFromPath(path);

    /// <summary>Most recent import's warning about triangles the mesh could not hold, or null.</summary>
    public string? ImportWarning { get; private set; }

    /// <summary>
    /// Appends an import warning to the status line when part of the file could not be loaded.
    /// Silence here would tell a user "no problems found" about a mesh a quarter of which never
    /// made it in — see <see cref="MeshImportResult"/>.
    /// </summary>
    private string StatusFor(string prefix, MeshImportResult import)
    {
        ImportWarning = import.Warning;
        return import.Warning is null ? prefix : $"{prefix} — warning: {import.Warning}";
    }

    private void LoadSampleMesh()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using Stream? stream = assembly.GetManifestResourceStream(SampleMeshResourceName);
        if (stream is null)
        {
            StatusText.Text = "Sample mesh resource not found.";
            return;
        }

        MeshImportResult import = StlReader.ReadWithDiagnostics(stream);

        // No unit offer for the built-in sample: it is a 1-unit tetrahedron nobody chose to open,
        // so asking about its units on every launch would be asking a question with no answer.
        ApplyLoadedMesh(import.Mesh, StatusFor("Loaded sample tetrahedron", import), offerUnitScaling: false);
    }

    private async void OnOpenFileClick(object? sender, RoutedEventArgs e)
    {
        // The toolbar button isn't disabled by IsEnabled alone as reliably as the menu item
        // (RefreshBusyState sets both, but this guard is cheap insurance against loading a new
        // mesh out from under a running operation).
        if (_document.IsBusy)
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is not { } storageProvider)
        {
            return;
        }

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open mesh file",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Mesh files") { Patterns = MeshImporter.SupportedPatterns.ToArray() },
                new FilePickerFileType("STL files") { Patterns = new[] { "*.stl" } },
                new FilePickerFileType("OBJ files") { Patterns = new[] { "*.obj" } },
            },
        });

        if (files.Count == 0)
        {
            return;
        }

        // Prefer the real path when the picker can give one: it is what the recent-files list has
        // to store, and a stream cannot be reopened next session. The stream branch below stays
        // for the pickers that hand back no path at all (a portal-brokered or remote location),
        // where the file opens normally but cannot be remembered.
        if (files[0].TryGetLocalPath() is { } localPath)
        {
            OpenFileFromPath(localPath);
            return;
        }

        try
        {
            await using Stream stream = await files[0].OpenReadAsync();
            MeshImportResult import = MeshImporter.ImportWithDiagnostics(stream, files[0].Name);
            ApplyLoadedMesh(import.Mesh, StatusFor($"Loaded {files[0].Name}", import));
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to load {files[0].Name}: {ex.Message}";
        }
    }

    private void ApplyLoadedMesh(DMesh3 mesh, string statusPrefix, bool offerUnitScaling = true)
    {
        _document.Load(mesh);

        // A newly opened mesh is the one time the camera should be repositioned for the user;
        // edits deliberately leave the view alone (see MeshViewportControl.Mesh).
        Viewport.FrameMesh();
        SetStatus(statusPrefix);

        ShowUnitSuggestion(offerUnitScaling ? ImportUnits.Suspect(mesh) : null);
    }

    private void OnResetViewClick(object? sender, RoutedEventArgs e) => Viewport.FrameMesh();

    /// <summary>
    /// View menu -> Front/Back/Left/Right/Top/Bottom/Isometric. The <c>Tag</c> carries the
    /// <see cref="StandardView"/> name, so adding a preset is one XAML line and cannot drift from
    /// a parallel switch statement here.
    /// </summary>
    private void OnStandardViewClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && Enum.TryParse(tag, out StandardView view))
        {
            Viewport.SetStandardView(view);
            StatusText.Text = $"{view} view";
        }
    }

    private void OnProjectionModeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && Enum.TryParse(tag, out ProjectionMode mode))
        {
            Viewport.ProjectionMode = mode;
            StatusText.Text = mode == ProjectionMode.Orthographic ? "Orthographic projection" : "Perspective projection";
            RefreshViewMenuChecks();
        }
    }

    private void OnDisplayModeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && Enum.TryParse(tag, out MeshDisplayMode mode))
        {
            Viewport.DisplayMode = mode;
            StatusText.Text = mode switch
            {
                MeshDisplayMode.Wireframe => "Wireframe display",
                MeshDisplayMode.XRay => "X-ray display",
                _ => "Shaded display",
            };
            RefreshViewMenuChecks();
        }
    }

    /// <summary>
    /// Fills the View -> Build Plate submenu from <see cref="BuildVolume.Presets"/>. Generated
    /// rather than written out in XAML so a printer added to the list appears in the menu without
    /// a second edit that could disagree with it.
    /// </summary>
    private void InitializeBuildPlateMenu()
    {
        foreach (BuildVolume volume in BuildVolume.Presets)
        {
            var item = new MenuItem
            {
                Header = volume.Name,
                Tag = volume,
                ToggleType = MenuItemToggleType.Radio,
                GroupName = "BuildVolume",
                IsChecked = volume == _buildVolume,
            };
            item.Click += OnBuildVolumeClick;
            BuildPlateMenuItem.Items.Add(item);
        }
    }

    private void OnBuildVolumeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: BuildVolume volume })
        {
            _buildVolume = volume;
            _settings.BuildVolumeName = volume.Name;
            SaveSettings();
            RefreshBuildPlate();
            RefreshViewMenuChecks();
            StatusText.Text = $"Build plate: {volume.Name}";
        }
    }

    private void OnToggleBuildPlateClick(object? sender, RoutedEventArgs e)
    {
        // Flips the flag rather than reading the menu item's own IsChecked, because that value is
        // not trustworthy here: Avalonia toggles it when the item is clicked but not when the item
        // is activated by its HotKey, so a handler that reads it makes the shortcut a silent no-op
        // (found by pressing Ctrl+Shift+B in the running app and watching nothing happen).
        // RefreshViewMenuChecks then writes the check mark back from the real state, which keeps
        // both entry points consistent.
        _showBuildPlate = !_showBuildPlate;
        _settings.ShowBuildPlate = _showBuildPlate;
        SaveSettings();
        RefreshBuildPlate();
        RefreshViewMenuChecks();
        StatusText.Text = _showBuildPlate ? "Build plate shown" : "Build plate hidden";
    }

    /// <summary>
    /// Writes every View menu check mark from the state it claims to describe.
    ///
    /// <para>
    /// Avalonia updates a <see cref="MenuItemToggleType"/> item's <c>IsChecked</c> when the item is
    /// clicked, but not when it is activated by its <c>HotKey</c>. Before this existed,
    /// <c>Ctrl+Shift+O</c> really did switch the viewport to orthographic while the menu went on
    /// showing the dot next to Perspective - a control describing a state the app was not in,
    /// which is the failure §11 (2026-09-06) records three of. The check marks are therefore
    /// derived, never assumed.
    /// </para>
    /// </summary>
    private void RefreshViewMenuChecks()
    {
        PerspectiveMenuItem.IsChecked = Viewport.ProjectionMode == ProjectionMode.Perspective;
        OrthographicMenuItem.IsChecked = Viewport.ProjectionMode == ProjectionMode.Orthographic;

        ShadedDisplayMenuItem.IsChecked = Viewport.DisplayMode == MeshDisplayMode.Shaded;
        WireframeDisplayMenuItem.IsChecked = Viewport.DisplayMode == MeshDisplayMode.Wireframe;
        XRayDisplayMenuItem.IsChecked = Viewport.DisplayMode == MeshDisplayMode.XRay;

        ShowCrossSectionMenuItem.IsChecked = _showCrossSection;

        ShowBuildPlateMenuItem.IsChecked = _showBuildPlate;
        foreach (MenuItem item in BuildPlateMenuItem.Items.OfType<MenuItem>())
        {
            if (item.Tag is BuildVolume volume)
            {
                item.IsChecked = volume == _buildVolume;
            }
        }
    }

    /// <summary>
    /// Fills the axis picker and subscribes to the two cross-section controls.
    ///
    /// <para>
    /// Both subscriptions are on the Avalonia properties rather than on the
    /// <c>SelectionChanged</c>/<c>ValueChanged</c> routed events they each also raise. A routed
    /// event does not fire for a control exercised outside a visual tree, which is how the
    /// drain-hole gizmo shipped placing every hole at a hard-coded 2 mm behind a green suite
    /// (§11, 2026-09-06) - and it would make a headless test of this slider pass without the
    /// slider ever moving the plane.
    /// </para>
    /// </summary>
    private void InitializeCrossSectionControls()
    {
        CrossSectionAxisCombo.ItemsSource = Enum.GetValues<CrossSectionAxis>();
        CrossSectionAxisCombo.SelectedItem = _crossSectionAxis;

        CrossSectionAxisCombo.PropertyChanged += (_, e) =>
        {
            if (e.Property == ComboBox.SelectedItemProperty && e.NewValue is CrossSectionAxis axis && axis != _crossSectionAxis)
            {
                _crossSectionAxis = axis;

                // A new axis means a new travel: park the plane mid-model rather than carrying a
                // Z millimetre count over onto X, where it may be outside the model entirely.
                _crossSectionPosition = double.NaN;
                RefreshCrossSection();
                StatusText.Text = $"Cross-section along {axis}";
            }
        };

        CrossSectionSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty && !_updatingCrossSectionControls)
            {
                _crossSectionPosition = CrossSectionSlider.Value;
                RefreshCrossSection();
            }
        };
    }

    private void OnToggleCrossSectionClick(object? sender, RoutedEventArgs e)
    {
        // Flips the field, never the menu item's own IsChecked: Avalonia leaves that value alone
        // when the item is activated by its HotKey, so reading it would make Ctrl+Shift+C a silent
        // no-op the way Ctrl+Shift+B once was. RefreshViewMenuChecks writes the mark back.
        _showCrossSection = !_showCrossSection;

        // Switching it on always opens the model down the middle. The position is a world
        // millimetre, so one carried over from whatever was loaded before is only accidentally
        // meaningful here: a 40 mm cube inherited a position of 0 from the sample mesh and the
        // first thing the feature ever showed was an all-but-empty viewport - the section working
        // exactly as asked, and looking like it had deleted the model. Found in the running app.
        if (_showCrossSection)
        {
            _crossSectionPosition = double.NaN;
        }

        RefreshCrossSection();
        RefreshViewMenuChecks();
        StatusText.Text = _showCrossSection
            ? $"Cross-section along {_crossSectionAxis}"
            : "Cross-section off";
    }

    private void OnFlipCrossSectionClick(object? sender, RoutedEventArgs e)
    {
        _crossSectionFlipped = !_crossSectionFlipped;
        RefreshCrossSection();
        StatusText.Text = $"Cross-section showing {CrossSectionPositionText.Text}";
    }

    /// <summary>
    /// Rescales the slider to the model's extent along the current axis and pushes the resulting
    /// plane to the viewport.
    ///
    /// <para>
    /// The slider's travel is the model's own bounding box along that axis, so both ends of it are
    /// useful: at one end nothing is hidden and at the other the model is gone, with every
    /// intermediate position landing inside the part. A fixed range in millimetres would put the
    /// whole of the 2 mm Menger sponge in the first pixel of the slider and leave the 120 mm tower
    /// off the end of it.
    /// </para>
    /// </summary>
    private void RefreshCrossSection()
    {
        CrossSectionBar.IsVisible = _showCrossSection;

        g3.AxisAlignedBox3d bounds = _document.Mesh is { TriangleCount: > 0 } mesh
            ? mesh.CachedBounds
            : new g3.AxisAlignedBox3d(g3.Vector3d.Zero, 1.0);
        (double min, double max) = CrossSectionRange.Along(bounds, _crossSectionAxis);

        // A model flat along this axis (a plate sectioned along Z) would otherwise give the slider
        // no travel at all and pin it to one value.
        if (max - min < 1e-9)
        {
            min -= 0.5;
            max += 0.5;
        }

        if (double.IsNaN(_crossSectionPosition) || _crossSectionPosition < min || _crossSectionPosition > max)
        {
            _crossSectionPosition = (min + max) / 2.0;
        }

        _updatingCrossSectionControls = true;
        try
        {
            CrossSectionSlider.Minimum = min;
            CrossSectionSlider.Maximum = max;
            CrossSectionSlider.SmallChange = (max - min) / 200.0;
            CrossSectionSlider.LargeChange = (max - min) / 20.0;
            CrossSectionSlider.Value = _crossSectionPosition;
            CrossSectionAxisCombo.SelectedItem = _crossSectionAxis;
        }
        finally
        {
            _updatingCrossSectionControls = false;
        }

        // States which half survives, not just where the plane is. "Z = 0.49 mm" is true of both
        // sides of the same plane, so on its own it cannot tell a user which half Flip Side just
        // gave them.
        CrossSectionPositionText.Text =
            $"{_crossSectionAxis} {(_crossSectionFlipped ? "\u2265" : "\u2264")} {_crossSectionPosition:0.##} mm";

        Viewport.CrossSection = _showCrossSection
            ? new CrossSectionPlane(_crossSectionAxis, (float)_crossSectionPosition, _crossSectionFlipped)
            : null;
    }

    /// <summary>The section plane the viewport is actually drawing, or null when there is none.
    /// Exposed for testing: the assertion that matters is that moving the slider moves <em>this</em>,
    /// not merely that the slider's own value changed.</summary>
    public CrossSectionPlane? ActiveCrossSection => Viewport.CrossSection;

    /// <summary>Whether the cross-section bar under the viewport is on screen, exposed for testing.</summary>
    public bool CrossSectionBarVisible => CrossSectionBar.IsVisible;

    /// <summary>The millimetre readout beside the slider, exposed for testing.</summary>
    public string? CrossSectionPositionLabel => CrossSectionPositionText.Text;

    /// <summary>Drives the slider the way a drag does, for tests that cannot deliver pointer input.</summary>
    public void SetCrossSectionSliderForTesting(double position) => CrossSectionSlider.Value = position;

    /// <summary>
    /// Pushes the selected bed to the viewport and evaluates the current mesh against it. One
    /// evaluation feeds both the warning text and the bed outline colour, so the two cannot
    /// disagree about whether the model fits.
    /// </summary>
    private void RefreshBuildPlate()
    {
        BuildPlateFitResult fit = BuildPlateFit.Evaluate(_document.Mesh, _buildVolume);

        Viewport.BuildVolume = _showBuildPlate ? _buildVolume : null;
        Viewport.ModelFitsBuildVolume = fit.Fits;

        BuildPlateWarningText.Text = fit.Message ?? string.Empty;

        // Hidden when the model fits, and also when the plate itself is hidden: warning about a
        // bed the user has switched off would be warning about something not on screen.
        BuildPlateWarningText.IsVisible = _showBuildPlate && !fit.Fits;
    }

    /// <summary>The bed the out-of-bounds warning is measured against, exposed for testing.</summary>
    public BuildVolume SelectedBuildVolume => _buildVolume;

    /// <summary>The visible out-of-bounds warning text, or null when none is shown. Exposed for
    /// testing: the assertion that matters is that it is <em>absent</em> for a model that fits.</summary>
    public string? BuildPlateWarning => BuildPlateWarningText.IsVisible ? BuildPlateWarningText.Text : null;

    private async void OnExportFileClick(object? sender, RoutedEventArgs e)
    {
        if (_document.IsBusy)
        {
            return;
        }

        if (_document.Mesh is not { } mesh)
        {
            StatusText.Text = "Nothing to export";
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is not { } storageProvider)
        {
            return;
        }

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export mesh file",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("STL files") { Patterns = new[] { "*.stl" } },
                new FilePickerFileType("OBJ files") { Patterns = new[] { "*.obj" } },
            },
        });

        if (file is null)
        {
            return;
        }

        try
        {
            await using Stream stream = await file.OpenWriteAsync();
            MeshExporter.Export(stream, mesh, file.Name);
            StatusText.Text = $"Exported {file.Name}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to export {file.Name}: {ex.Message}";
        }
    }

    /// <summary>Exports the current mesh to a path through the real export pipeline, bypassing the
    /// save-file picker dialog; used by integration tests that can't drive an OS file picker
    /// headlessly.</summary>
    public void ExportFileForTesting(string path)
    {
        if (_document.Mesh is not { } mesh)
        {
            StatusText.Text = "Nothing to export";
            return;
        }

        try
        {
            MeshExporter.ExportFile(path, mesh);
            StatusText.Text = $"Exported {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to export {Path.GetFileName(path)}: {ex.Message}";
        }
    }

    private void OnExitClick(object? sender, RoutedEventArgs e) => Close();

    private void OnUndoClick(object? sender, RoutedEventArgs e) => PerformUndo();

    private void OnRedoClick(object? sender, RoutedEventArgs e) => PerformRedo();

    private void OnMainWindowKeyDown(object? sender, KeyEventArgs e)
    {
        // MenuItem.HotKey already handles plain Ctrl+Z (undo) and Ctrl+Y (redo). Ctrl+Shift+Z is
        // an additional, Mac-style redo accelerator that isn't expressible as a second HotKey on
        // the same MenuItem, so it's handled here instead.
        if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) && e.Key == Key.Z)
        {
            PerformRedo();
            e.Handled = true;
        }
    }

    private void PerformUndo()
    {
        // MeshDocument.Undo() already refuses while IsBusy (an operation may be mutating the
        // mesh on a background thread right now), but the menu item's HotKey isn't guaranteed to
        // respect IsEnabled, so check explicitly for an honest status message rather than the
        // misleading "Nothing to undo".
        if (_document.IsBusy)
        {
            StatusText.Text = "Can't undo while an operation is running.";
            return;
        }

        if (_document.Undo())
        {
            SetStatus("Undo");
        }
        else
        {
            StatusText.Text = "Nothing to undo";
            RefreshUndoRedoState();
        }
    }

    private void PerformRedo()
    {
        if (_document.IsBusy)
        {
            StatusText.Text = "Can't redo while an operation is running.";
            return;
        }

        if (_document.Redo())
        {
            SetStatus("Redo");
        }
        else
        {
            StatusText.Text = "Nothing to redo";
            RefreshUndoRedoState();
        }
    }

    /// <summary>Refreshes the viewport, gizmos, Edit panels, and diagnostics panel from the
    /// document's current mesh/report. Runs on every <see cref="MeshDocument.Changed"/>, so it
    /// covers loads, undo/redo, and operations applied by the Edit panels alike. The status line
    /// is left to the caller that knows what just happened.</summary>
    private void RefreshFromDocument()
    {
        if (_document.Mesh is not { } mesh || _document.Report is not { } report)
        {
            return;
        }

        Viewport.Mesh = mesh;
        Viewport.Report = report;

        // Update gizmos with the new mesh
        if (_drainHoleGizmo is not null)
        {
            _drainHoleGizmo.Dispose();
        }
        _drainHoleGizmo = new DrainHoleGizmo(mesh);
        DrainHolePanel.SetGizmo(_drainHoleGizmo);

        if (_planeCutGizmo is not null)
        {
            _planeCutGizmo.Dispose();
        }
        _planeCutGizmo = new PlaneCutGizmo(ComputeMeshCenter(mesh));
        PlaneCutPanel.SetGizmo(_planeCutGizmo);

        if (_transformGizmo is not null)
        {
            _transformGizmo.Dispose();
        }
        _transformGizmo = new TransformGizmo(ComputeMeshCenter(mesh));
        TransformPanel.SetGizmo(_transformGizmo);

        if (_hollowGizmo is not null)
        {
            _hollowGizmo.Dispose();
        }
        (Vector3 hollowAnchor, Vector3 hollowNormal) = HollowGizmo.ComputeSurfaceAnchor(mesh);
        _hollowGizmo = new HollowGizmo(hollowAnchor, hollowNormal, HollowGizmo.ComputeDefaultWallThickness(mesh));
        HollowPanel.SetGizmo(_hollowGizmo);

        // Clear any active gizmo from the viewport. The owning panel has to be told as well,
        // or its button and status text go on claiming a gizmo that is no longer on screen.
        _deactivateCurrentGizmoOwner?.Invoke();
        _deactivateCurrentGizmoOwner = null;
        Viewport.Gizmo = null;

        // Update all panels with statistics from the new mesh
        RepairPanel.SetDocument(_document);
        PlaneCutPanel.SetDocument(_document);
        TransformPanel.SetDocument(_document);
        HollowPanel.SetDocument(_document);
        DrainHolePanel.SetDocument(_document);
        DecimatePanel.SetDocument(_document);
        BooleanPanel.SetDocument(_document);

        UpdateDiagnosticsPanel(report);
        RefreshUndoRedoState();

        // The model may have moved or grown: re-test it against the bed, and re-scale the
        // cross-section slider to the new extents. Every operation, undo and redo comes through
        // here, so no edit can leave a stale verdict or a section plane sitting outside the model.
        RefreshBuildPlate();
        RefreshCrossSection();
    }

    /// <summary>Sets the status line to "&lt;what just happened&gt; (N triangles) — N issues found".</summary>
    private void SetStatus(string prefix)
    {
        if (_document.Mesh is not { } mesh || _document.Report is not { } report)
        {
            StatusText.Text = prefix;
            return;
        }

        StatusText.Text = $"{prefix} ({mesh.TriangleCount} triangles) — {report.Issues.Count} issues found";
    }

    private void RefreshUndoRedoState()
    {
        UndoMenuItem.IsEnabled = _document.CanUndo;
        RedoMenuItem.IsEnabled = _document.CanRedo;
        UndoRedoStatusText.Text = (_document.CanUndo, _document.CanRedo) switch
        {
            (true, true) => "| Undo and Redo available",
            (true, false) => "| Undo available",
            (false, true) => "| Redo available",
            (false, false) => string.Empty,
        };
    }

    /// <summary>Whether the busy indicator is currently shown, exposed for testing.</summary>
    public bool IsOperationInProgress => OperationProgressPanel.IsVisible;

    /// <summary>Current operation-progress status text, exposed for testing.</summary>
    public string? OperationProgressMessage => OperationProgressText.Text;

    /// <summary>
    /// Disables the entire Edit tab strip, Undo/Redo, and Open/Export while an operation is
    /// running (backlog item 13: the UI must not allow a second operation to start, and must
    /// not let the mesh be swapped out or undone from under a running mutation), and shows or
    /// hides the busy indicator. Runs on every <see cref="MeshDocument.BusyChanged"/>.
    /// </summary>
    private void RefreshBusyState()
    {
        bool busy = _document.IsBusy;

        EditTabControl.IsEnabled = !busy;
        OpenMenuItem.IsEnabled = !busy;
        ExportMenuItem.IsEnabled = !busy;
        OpenFileButton.IsEnabled = !busy;
        ExportFileButton.IsEnabled = !busy;
        RefreshUndoRedoState();

        OperationProgressPanel.IsVisible = busy;
        CancelOperationButton.IsEnabled = busy && _document.CanCancelCurrentOperation;

        if (busy)
        {
            // Reset to an honest "no idea how far along this is" state every time a new
            // operation starts. It only becomes a determinate bar if Progress actually fires —
            // most operations never will, and showing a percentage that isn't tracking anything
            // would violate §4.
            OperationProgressBar.IsIndeterminate = true;
            OperationProgressText.Text = $"Working: {_document.CurrentOperationName ?? "operation"}...";
        }
        else
        {
            OperationProgressBar.IsIndeterminate = true;
            OperationProgressBar.Value = 0;
        }
    }

    /// <summary>Switches the busy indicator to a determinate bar the first time real progress
    /// arrives (see <see cref="MeshDocument.Progress"/>) and updates it after. Called on whatever
    /// thread raised the event — safe here because <see cref="MeshDocument.ApplyAsync"/> only
    /// ever raises it back on the caller's own context (the UI thread).</summary>
    private void ShowOperationProgress(OperationProgress progress)
    {
        OperationProgressText.Text = progress.Description;
        if (progress.FractionComplete is { } fraction)
        {
            OperationProgressBar.IsIndeterminate = false;
            OperationProgressBar.Value = fraction;
        }
    }

    private void OnCancelOperationClick(object? sender, RoutedEventArgs e) =>
        _document.CancelCurrentOperation();

    private void UpdateDiagnosticsPanel(MeshDiagnosticsReport report)
    {
        MeshStatistics stats = report.Statistics;
        StatisticsText.Text = string.Format(
            CultureInfo.InvariantCulture,
            "Triangles: {0}\nVertices: {1}\nShells: {2}\nVolume: {3:0.###}\nSurface area: {4:0.###}\nBounds: {5:0.##} x {6:0.##} x {7:0.##}",
            stats.TriangleCount,
            stats.VertexCount,
            stats.ShellCount,
            stats.Volume,
            stats.SurfaceArea,
            stats.BoundingBox.Width,
            stats.BoundingBox.Height,
            stats.BoundingBox.Depth);

        SummaryText.Text = report.Summary;

        IssuesList.ItemsSource = report.Issues
            .Select(issue => $"[{issue.Severity}] {issue.Category}: {issue.Message}")
            .ToList();
    }

    // ---------------------------------------------------------------------------------------
    // Import conveniences: remembered settings, recent files, drag-and-drop, and the unit offer.
    // ---------------------------------------------------------------------------------------

    /// <summary>The settings actually in effect, exposed for testing.</summary>
    public AppSettings SettingsForTesting => _settings;

    /// <summary>
    /// Writes the settings file and reports a failure on the status line. Called at each point a
    /// remembered value changes rather than only at shutdown: a crash or a kill -9 should not be
    /// able to lose the file you opened two minutes ago, and the file is a few hundred bytes.
    /// </summary>
    private void SaveSettings()
    {
        if (!_settingsStore.Save(_settings) && _settingsStore.SaveWarning is { } warning)
        {
            StatusText.Text = warning;
        }
    }

    /// <summary>
    /// Matches a remembered printer name back to a preset. An unknown name — a settings file from
    /// a later version, or a preset since renamed — falls back to the default bed rather than
    /// failing to start or inventing a bed of its own.
    /// </summary>
    private static BuildVolume ResolveBuildVolume(string? name) =>
        BuildVolume.Presets.FirstOrDefault(volume => volume.Name == name) ?? BuildVolume.Default;

    private void RestoreWindowPlacement()
    {
        // Recorded on every move and resize while the window is in its normal state, and written
        // to settings when it closes.
        PositionChanged += (_, _) => RecordNormalPlacement();

        if (_settings.Window is not { } placement || !placement.IsUsable)
        {
            return;
        }

        Width = placement.Width;
        Height = placement.Height;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint(placement.X, placement.Y);
        _normalPlacement = placement;

        if (placement.Maximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void RecordNormalPlacement()
    {
        if (WindowState != WindowState.Normal)
        {
            return;
        }

        var candidate = new WindowPlacement(
            Position.X,
            Position.Y,
            (int)Math.Round(Bounds.Width <= 0 ? Width : Bounds.Width),
            (int)Math.Round(Bounds.Height <= 0 ? Height : Bounds.Height),
            Maximized: false);

        // Nonsense geometry (a window mid-creation, or a headless backend that reports nothing)
        // must not overwrite a good record with one that will be ignored on the way back in.
        if (candidate.IsUsable)
        {
            _normalPlacement = candidate;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        RecordNormalPlacement();

        if (_normalPlacement is { } placement)
        {
            _settings.Window = placement with { Maximized = WindowState == WindowState.Maximized };
            SaveSettings();
        }

        base.OnClosing(e);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        RecordNormalPlacement();
    }

    private void RememberRecentFile(string path)
    {
        _settings.RememberRecentFile(path);
        SaveSettings();
        RefreshRecentFilesMenu();
    }

    private void ForgetRecentFile(string path)
    {
        _settings.ForgetRecentFile(path);
        SaveSettings();
        RefreshRecentFilesMenu();
    }

    /// <summary>
    /// Rebuilds File → Open Recent from <see cref="AppSettings.RecentFiles"/>.
    ///
    /// <para>
    /// Generated rather than bound so the disambiguation below can exist: two files called
    /// <c>model.stl</c> from different folders are one useless menu unless the entry says which
    /// folder, and appending the folder to every entry would make the common case unreadable. The
    /// full path is on the tooltip either way.
    /// </para>
    /// </summary>
    private void RefreshRecentFilesMenu()
    {
        RecentFilesMenuItem.Items.Clear();

        if (_settings.RecentFiles.Count == 0)
        {
            RecentFilesMenuItem.Items.Add(new MenuItem { Header = "No recent files", IsEnabled = false });
            return;
        }

        var duplicatedNames = _settings.RecentFiles
            .GroupBy(Path.GetFileName)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();

        foreach (string path in _settings.RecentFiles)
        {
            string name = Path.GetFileName(path);
            string header = duplicatedNames.Contains(name)
                ? $"{name}  —  {Path.GetDirectoryName(path)}"
                : name;

            // An underscore in a file name is an access-key marker to a MenuItem header, so
            // "my_model.stl" would show as "mymodel.stl" with a hidden shortcut on the m.
            var item = new MenuItem { Header = header.Replace("_", "__"), Tag = path };
            ToolTip.SetTip(item, path);
            item.Click += OnRecentFileClick;
            RecentFilesMenuItem.Items.Add(item);
        }

        RecentFilesMenuItem.Items.Add(new Separator());

        var clear = new MenuItem { Header = "_Clear Recent Files" };
        clear.Click += OnClearRecentFilesClick;
        RecentFilesMenuItem.Items.Add(clear);
    }

    private void OnRecentFileClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string path } || _document.IsBusy)
        {
            return;
        }

        // Checked here rather than when the menu was built: a list of ten paths is checked once,
        // at the moment it matters, instead of stat-ing every entry (possibly across a network
        // mount) each time the File menu is refreshed.
        if (!File.Exists(path))
        {
            StatusText.Text = $"{Path.GetFileName(path)} is no longer at {path} — removed from recent files.";
            ForgetRecentFile(path);
            return;
        }

        OpenFileFromPath(path);
    }

    private void OnClearRecentFilesClick(object? sender, RoutedEventArgs e)
    {
        _settings.RecentFiles.Clear();
        SaveSettings();
        RefreshRecentFilesMenu();
        StatusText.Text = "Recent files cleared";
    }

    /// <summary>The recent-files entries currently in the menu, most recent first. Exposed for
    /// testing: the assertion that matters is what the menu offers, not what the list holds.</summary>
    public IReadOnlyList<string> RecentFileMenuPaths => RecentFilesMenuItem.Items
        .OfType<MenuItem>()
        .Select(item => item.Tag as string)
        .Where(tag => tag is not null)
        .Select(tag => tag!)
        .ToList();

    /// <summary>Opens the nth recent-files entry the way clicking it does, for tests that cannot
    /// open a menu.</summary>
    public void ClickRecentFileForTesting(int index) =>
        OnRecentFileClick(RecentFilesMenuItem.Items.OfType<MenuItem>().ElementAt(index), new RoutedEventArgs());

    /// <summary>
    /// Makes the whole window a drop target.
    ///
    /// <para>
    /// On the <b>Window</b>, deliberately, and not on <c>MeshViewportControl</c>. On Linux the GL
    /// surface does not reliably take part in Avalonia's input routing — which is why
    /// <c>ViewportInputOverlay</c> exists to forward pointer events at all — so handlers attached
    /// to the viewport control would be attached to the one control that never sees the event.
    /// Dropping anywhere on the window is also what a user expects: the sidebars and the toolbar
    /// are part of the same window and there is nothing else a mesh file could mean.
    /// </para>
    /// </summary>
    private void InitializeDragAndDrop()
    {
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, OnDragOverWindow);
        AddHandler(DragDrop.DragOverEvent, OnDragOverWindow);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeaveWindow);
        AddHandler(DragDrop.DropEvent, OnDropOnWindow);
    }

    private void OnDragOverWindow(object? sender, DragEventArgs e)
    {
        DroppedFiles dropped = DroppedFiles.From(e.DataTransfer);

        // The refusal is shown, not just enacted. A window that simply declines a drop leaves the
        // user guessing whether the app is broken, the file is wrong, or the drag missed.
        bool accepted = dropped.Importable is not null && !_document.IsBusy;
        e.DragEffects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;

        ShowDropHint(dropped, accepted);
    }

    private void OnDragLeaveWindow(object? sender, RoutedEventArgs e) => HideDropHint();

    private void OnDropOnWindow(object? sender, DragEventArgs e)
    {
        HideDropHint();
        e.Handled = true;

        if (_document.IsBusy)
        {
            StatusText.Text = "Can't open a file while an operation is running.";
            return;
        }

        DroppedFiles dropped = DroppedFiles.From(e.DataTransfer);
        if (dropped.Importable is not { } path)
        {
            StatusText.Text = dropped.RefusalMessage;
            return;
        }

        OpenFileFromPath(path);

        // One document at a time is the v1.0 model (§5.1), so a multi-file drop opens the first
        // mesh and says so rather than silently discarding the rest.
        if (dropped.Paths.Count > 1)
        {
            StatusText.Text += $" — {dropped.Paths.Count - 1} other dropped file(s) ignored; Meshwright opens one mesh at a time.";
        }
    }

    private void ShowDropHint(DroppedFiles dropped, bool accepted)
    {
        DropHintText.Text = accepted
            ? $"Open {Path.GetFileName(dropped.Importable!)}"
            : dropped.RefusalMessage;
        DropHintOverlay.BorderBrush = accepted
            ? Avalonia.Media.Brushes.DeepSkyBlue
            : Avalonia.Media.Brushes.Orange;
        DropHintOverlay.IsVisible = true;
    }

    private void HideDropHint() => DropHintOverlay.IsVisible = false;

    /// <summary>The drop hint's text while it is on screen, or null. Exposed for testing.</summary>
    public string? DropHintMessage => DropHintOverlay.IsVisible ? DropHintText.Text : null;

    /// <summary>
    /// Shows or hides the mm/inch offer. <paramref name="suggestion"/> null hides the bar, which
    /// is what every ordinary import does.
    /// </summary>
    private void ShowUnitSuggestion(UnitScaleSuggestion? suggestion)
    {
        _pendingUnitSuggestion = _settings.OfferUnitScaling ? suggestion : null;

        if (_pendingUnitSuggestion is not { } offer)
        {
            UnitSuggestionBar.IsVisible = false;
            return;
        }

        UnitSuggestionText.Text = offer.Message;
        UnitSuggestionAcceptButton.Content = offer.AcceptLabel;
        UnitSuggestionBar.IsVisible = true;
    }

    /// <summary>The mm/inch offer's text while it is on screen, or null when nothing is being
    /// suggested. Exposed for testing: the assertion that matters is that an ordinary import
    /// says <em>nothing</em>, and that a suspicious one has not been scaled by the time this
    /// appears.</summary>
    public string? UnitSuggestionMessage => UnitSuggestionBar.IsVisible ? UnitSuggestionText.Text : null;

    private async void OnAcceptUnitSuggestionClick(object? sender, RoutedEventArgs e) =>
        await ApplyUnitSuggestionAsync();

    private async System.Threading.Tasks.Task ApplyUnitSuggestionAsync()
    {
        if (_pendingUnitSuggestion is null || _document.IsBusy || _document.Mesh is null)
        {
            return;
        }

        UnitSuggestionBar.IsVisible = false;
        _pendingUnitSuggestion = null;

        // Through the document, so it lands on the undo stack like any other change to the mesh
        // (§4: never silently destroy the model). Accepting a guess must be as reversible as
        // every other operation, because the guess can be wrong.
        OperationResult result = await _document.ApplyAsync(new InterpretAsInchesOperation());

        // The model is now 25.4x its previous size, so the camera framing it had is no longer
        // framing it. This is the one other place besides an open where refitting is right.
        Viewport.FrameMesh();
        StatusText.Text = result.Summary;
    }

    private void OnDismissUnitSuggestionClick(object? sender, RoutedEventArgs e)
    {
        UnitSuggestionBar.IsVisible = false;
        _pendingUnitSuggestion = null;
        StatusText.Text = "Kept the model at the size the file describes.";
    }

    /// <summary>Presses the offer's "Scale to mm" button, for tests that cannot click. Returns the
    /// task the click handler fires and forgets, so a test can await the rescale rather than
    /// asserting against a mesh that has not been touched yet.</summary>
    public System.Threading.Tasks.Task AcceptUnitSuggestionForTesting() => ApplyUnitSuggestionAsync();

    /// <summary>Presses the offer's "Keep as-is" button, for tests that cannot click.</summary>
    public void DismissUnitSuggestionForTesting() => OnDismissUnitSuggestionClick(this, new RoutedEventArgs());

    private void OnViewportPointerPressed(object? sender, PointerPressedEventArgs e) =>
        Viewport.HandleExternalPointerPressed(e);

    private void OnViewportPointerMoved(object? sender, PointerEventArgs e) =>
        Viewport.HandleExternalPointerMoved(e);

    private void OnViewportPointerReleased(object? sender, PointerReleasedEventArgs e) =>
        Viewport.HandleExternalPointerReleased(e);

    private void OnViewportPointerWheelChanged(object? sender, PointerWheelEventArgs e) =>
        Viewport.HandleExternalPointerWheelChanged(e);
}
