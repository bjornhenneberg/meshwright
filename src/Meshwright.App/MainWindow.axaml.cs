using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using g3;
using Meshwright.App.Gizmos;
using Meshwright.App.Views.Edit;
using Meshwright.Core;
using Meshwright.Geometry.Diagnostics;
using Meshwright.IO;
using Meshwright.IO.Stl;
using Meshwright.Rendering.Gizmos;

namespace Meshwright.App;

public partial class MainWindow : Window
{
    private const string SampleMeshResourceName = "Meshwright.App.Assets.SampleMesh.stl";

    private readonly MeshDocument _document = new();

    // Gizmos for interactive operations
    private DrainHoleGizmo? _drainHoleGizmo;
    private PlaneCutGizmo? _planeCutGizmo;
    private TransformGizmo? _transformGizmo;

    /// <summary>The currently active panel's "reset your own gizmo-active UI state" method, so
    /// it can be told to stand down when a different panel takes the single viewport gizmo
    /// slot (see <see cref="ActivateGizmoOwner"/>).</summary>
    private Action? _deactivateCurrentGizmoOwner;

    public MainWindow()
    {
        InitializeComponent();
        InitializeEditPanels();
        LoadSampleMesh();
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

    /// <summary>Loads a mesh file by path through the real load pipeline, bypassing the file
    /// picker dialog; used by integration tests that can't drive an OS file picker headlessly.</summary>
    public void LoadFileForTesting(string path)
    {
        MeshImportResult import = MeshImporter.ImportFileWithDiagnostics(path);
        ApplyLoadedMesh(import.Mesh, StatusFor($"Loaded {Path.GetFileName(path)}", import));
    }

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
        ApplyLoadedMesh(import.Mesh, StatusFor("Loaded sample tetrahedron", import));
    }

    private async void OnOpenFileClick(object? sender, RoutedEventArgs e)
    {
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

    private void ApplyLoadedMesh(DMesh3 mesh, string statusPrefix)
    {
        _document.Load(mesh);
        RefreshFromDocument(statusPrefix);
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
        if (_document.Undo())
        {
            RefreshFromDocument("Undo");
        }
        else
        {
            StatusText.Text = "Nothing to undo";
            RefreshUndoRedoState();
        }
    }

    private void PerformRedo()
    {
        if (_document.Redo())
        {
            RefreshFromDocument("Redo");
        }
        else
        {
            StatusText.Text = "Nothing to redo";
            RefreshUndoRedoState();
        }
    }

    /// <summary>Refreshes the viewport, gizmos, Edit panels, status bar, and diagnostics panel
    /// from the document's current mesh/report. Shared by the initial load path and by
    /// undo/redo, both of which change <see cref="_document"/>'s mesh out from under the UI.</summary>
    private void RefreshFromDocument(string statusPrefix)
    {
        DMesh3 mesh = _document.Mesh!;
        MeshDiagnosticsReport report = _document.Report!;

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

        // Clear any active gizmo from the viewport
        Viewport.Gizmo = null;

        // Update all panels with statistics from the new mesh
        PlaneCutPanel.SetDocument(_document);
        TransformPanel.SetDocument(_document);
        HollowPanel.SetDocument(_document);
        DrainHolePanel.SetDocument(_document);
        DecimatePanel.SetDocument(_document);
        BooleanPanel.SetDocument(_document);

        StatusText.Text = $"{statusPrefix} ({mesh.TriangleCount} triangles) — {report.Issues.Count} issues found";
        UpdateDiagnosticsPanel(report);
        RefreshUndoRedoState();
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

    private void OnViewportPointerPressed(object? sender, PointerPressedEventArgs e) =>
        Viewport.HandleExternalPointerPressed(e);

    private void OnViewportPointerMoved(object? sender, PointerEventArgs e) =>
        Viewport.HandleExternalPointerMoved(e);

    private void OnViewportPointerReleased(object? sender, PointerReleasedEventArgs e) =>
        Viewport.HandleExternalPointerReleased(e);

    private void OnViewportPointerWheelChanged(object? sender, PointerWheelEventArgs e) =>
        Viewport.HandleExternalPointerWheelChanged(e);
}
