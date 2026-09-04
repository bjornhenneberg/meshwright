using System;
using System.Globalization;
using System.IO;
using System.Linq;
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
using Meshwright.IO.Stl;

namespace Meshwright.App;

public partial class MainWindow : Window
{
    private const string SampleMeshResourceName = "Meshwright.App.Assets.SampleMesh.stl";

    private readonly MeshDocument _document = new();

    // Gizmos for interactive operations
    private DrainHoleGizmo? _drainHoleGizmo;

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
        // update the viewport accordingly
        DrainHolePanel.SetGizmoActivationCallback(
            onActivate: () => Viewport.Gizmo = _drainHoleGizmo,
            onDeactivate: () => Viewport.Gizmo = null);
    }

    /// <summary>Diagnostics report for the currently loaded mesh, exposed for testing.</summary>
    public MeshDiagnosticsReport? CurrentReport => _document.Report;

    /// <summary>Current status bar text, exposed for testing.</summary>
    public string? StatusMessage => StatusText.Text;

    /// <summary>Current diagnostics summary text, exposed for testing.</summary>
    public string? SummaryMessage => SummaryText.Text;

    /// <summary>Loads an STL file by path through the real load pipeline, bypassing the file
    /// picker dialog; used by integration tests that can't drive an OS file picker headlessly.</summary>
    public void LoadFileForTesting(string path)
    {
        using Stream stream = File.OpenRead(path);
        var mesh = StlReader.Read(stream);
        ApplyLoadedMesh(mesh, $"Loaded {Path.GetFileName(path)}");
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

        var mesh = StlReader.Read(stream);
        ApplyLoadedMesh(mesh, "Loaded sample tetrahedron");
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
            Title = "Open STL file",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("STL files") { Patterns = new[] { "*.stl" } },
            },
        });

        if (files.Count == 0)
        {
            return;
        }

        try
        {
            await using Stream stream = await files[0].OpenReadAsync();
            var mesh = StlReader.Read(stream);
            ApplyLoadedMesh(mesh, $"Loaded {files[0].Name}");
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
