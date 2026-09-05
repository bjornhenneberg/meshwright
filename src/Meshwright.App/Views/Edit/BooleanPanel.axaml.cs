using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using g3;
using Meshwright.Core;
using Meshwright.Core.Operations;
using Meshwright.Geometry.Diagnostics;
using Meshwright.IO;

namespace Meshwright.App.Views.Edit;

public partial class BooleanPanel : UserControl
{
    private MeshDocument? _document;
    private DMesh3? _secondaryMesh;
    private string? _secondaryMeshName;

    public BooleanPanel()
    {
        InitializeComponent();
        OperationSelector.SelectedIndex = 0; // Default to Union
        UpdateSecondaryDisplay();
    }

    /// <summary>
    /// Sets the mesh document this panel operates on, exposed for testing. Called by the
    /// integrating view (or test harness) to bind this panel to a live document.
    /// </summary>
    public void SetDocument(MeshDocument document)
    {
        _document = document;
        UpdateStatsDisplay();
    }

    /// <summary>Diagnostics report for the currently loaded mesh, exposed for testing.</summary>
    public MeshDiagnosticsReport? CurrentReport => _document?.Report;

    /// <summary>Current operation result message text, exposed for testing.</summary>
    public string? OperationResultMessage => ResultMessageText?.Text;

    /// <summary>Current selected operation index (0=Union, 1=Difference, 2=Intersection), exposed for testing.</summary>
    public int SelectedOperationIndex => OperationSelector?.SelectedIndex ?? 0;

    /// <summary>Whether a secondary mesh is currently loaded, exposed for testing.</summary>
    public bool HasSecondaryMesh => _secondaryMesh is not null;

    /// <summary>File name of the loaded secondary mesh, or null, exposed for testing.</summary>
    public string? SecondaryMeshName => _secondaryMeshName;

    /// <summary>Current text of the secondary-mesh status line, exposed for testing.</summary>
    public string? SecondaryMeshStatus => SecondaryMeshStatusText?.Text;

    /// <summary>Whether the Apply button is currently enabled, exposed for testing.</summary>
    public bool IsApplyEnabled => ApplyButton?.IsEnabled ?? false;

    /// <summary>Selects the operation (0=Union, 1=Difference, 2=Intersection) without driving the
    /// combo box's UI, for use by tests that can't click through headlessly.</summary>
    public void SelectOperationForTesting(int index)
    {
        if (OperationSelector is not null)
        {
            OperationSelector.SelectedIndex = index;
        }
    }

    /// <summary>Invokes the Apply button's click handler directly, for use by tests that can't
    /// drive UI input headlessly.</summary>
    public void InvokeApplyForTesting() => OnApplyClick(this, new RoutedEventArgs());

    /// <summary>
    /// Loads a mesh file by path as the secondary operand, bypassing the file picker dialog.
    /// Used by tests that can't drive an OS file picker headlessly, mirroring
    /// <see cref="MainWindow.OpenFileFromPath"/>.
    /// </summary>
    public void LoadSecondaryMeshFromPath(string path)
    {
        try
        {
            MeshImportResult import = MeshImporter.ImportFileWithDiagnostics(path);
            ApplySecondaryMesh(import.Mesh, Path.GetFileName(path), import.Warning);
        }
        catch (Exception ex)
        {
            if (ResultMessageText is not null)
            {
                ResultMessageText.Text = $"Failed to load secondary mesh: {ex.Message}";
            }
        }
    }

    private void ApplySecondaryMesh(DMesh3 mesh, string fileName, string? warning)
    {
        _secondaryMesh = mesh;
        _secondaryMeshName = fileName;
        UpdateSecondaryDisplay();

        if (ResultMessageText is not null)
        {
            var stats = MeshStatistics.Compute(mesh);
            string message = string.Format(
                CultureInfo.InvariantCulture,
                "Loaded secondary mesh {0} ({1} triangles).",
                fileName,
                stats.TriangleCount);
            ResultMessageText.Text = warning is null ? message : $"{message} Warning: {warning}";
        }
    }

    private void UpdateSecondaryDisplay()
    {
        if (SecondaryMeshStatusText is not null)
        {
            SecondaryMeshStatusText.Text = _secondaryMesh is null
                ? "No secondary mesh loaded. Load one to enable Union, Difference and Intersection."
                : $"{_secondaryMeshName} ({_secondaryMesh.TriangleCount} triangles)";
        }

        if (ApplyButton is not null)
        {
            ApplyButton.IsEnabled = _secondaryMesh is not null;
        }
    }

    private void UpdateStatsDisplay()
    {
        if (_document?.Mesh is null)
        {
            BeforeStats.Text = "(No mesh loaded)";
            AfterStats.Text = "(No mesh loaded)";
            return;
        }

        var stats = MeshStatistics.Compute(_document.Mesh);
        BeforeStats.Text = string.Format(
            CultureInfo.InvariantCulture,
            "Triangles: {0}\nVolume: {1:0.###}",
            stats.TriangleCount,
            stats.Volume);
        AfterStats.Text = "(Not applied yet)";
    }

    private async void OnLoadSecondaryClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is not { } storageProvider)
        {
            return;
        }

        IReadOnlyList<IStorageFile> files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open secondary mesh file",
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
            ApplySecondaryMesh(import.Mesh, files[0].Name, import.Warning);
        }
        catch (Exception ex)
        {
            if (ResultMessageText is not null)
            {
                ResultMessageText.Text = $"Failed to load secondary mesh {files[0].Name}: {ex.Message}";
            }
        }
    }

    private void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        if (_document is null || _document.Mesh is null)
        {
            if (ResultMessageText is not null)
            {
                ResultMessageText.Text = "No mesh loaded.";
            }
            return;
        }

        if (_secondaryMesh is null)
        {
            if (ResultMessageText is not null)
            {
                ResultMessageText.Text = "Load a secondary mesh before applying a boolean operation.";
            }
            return;
        }

        try
        {
            var secondaryMesh = _secondaryMesh;

            // Get the selected operation
            int selectedIndex = OperationSelector?.SelectedIndex ?? 0;
            IMeshOperation operation = selectedIndex switch
            {
                0 => new BooleanUnionOperation(secondaryMesh),
                1 => new BooleanDifferenceOperation(secondaryMesh),
                2 => new BooleanIntersectionOperation(secondaryMesh),
                _ => throw new InvalidOperationException("Unknown operation")
            };

            OperationResult result = _document.Apply(operation);

            var statsAfter = MeshStatistics.Compute(_document.Mesh);
            AfterStats.Text = string.Format(
                CultureInfo.InvariantCulture,
                "Triangles: {0}\nVolume: {1:0.###}",
                statsAfter.TriangleCount,
                statsAfter.Volume);

            if (ResultMessageText is not null)
            {
                ResultMessageText.Text = result.Summary;
            }
        }
        catch (Exception ex)
        {
            if (ResultMessageText is not null)
            {
                ResultMessageText.Text = $"Error: {ex.Message}";
            }
        }
    }
}
