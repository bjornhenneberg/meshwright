using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using g3;
using Meshwright.Core;
using Meshwright.Core.Operations;
using Meshwright.Geometry.Diagnostics;

namespace Meshwright.App.Views.Edit;

public partial class BooleanPanel : UserControl
{
    private MeshDocument? _document;

    public BooleanPanel()
    {
        InitializeComponent();
        OperationSelector.SelectedIndex = 0; // Default to Union
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

    /// <summary>
    /// Create a simple test fixture cube mesh positioned at an offset to overlap with the primary mesh.
    /// This is used for MVP demonstration; real multi-mesh loading is deferred to v1.x.
    /// </summary>
    private static DMesh3 CreateTestFixtureCube()
    {
        var mesh = new DMesh3(true);

        // Create a 1x1x1 cube positioned at (0.5, 0.5, 0) so it overlaps with a unit cube at origin
        double s = 0.5; // half-size
        double x = 0.5; // offset
        double y = 0.5; // offset
        double z = 0.0; // no z offset

        var v0 = mesh.AppendVertex(new Vector3d(x - s, y - s, z - s));
        var v1 = mesh.AppendVertex(new Vector3d(x + s, y - s, z - s));
        var v2 = mesh.AppendVertex(new Vector3d(x + s, y + s, z - s));
        var v3 = mesh.AppendVertex(new Vector3d(x - s, y + s, z - s));
        var v4 = mesh.AppendVertex(new Vector3d(x - s, y - s, z + s));
        var v5 = mesh.AppendVertex(new Vector3d(x + s, y - s, z + s));
        var v6 = mesh.AppendVertex(new Vector3d(x + s, y + s, z + s));
        var v7 = mesh.AppendVertex(new Vector3d(x - s, y + s, z + s));

        // Bottom face (z = z - s)
        mesh.AppendTriangle(v0, v1, v2);
        mesh.AppendTriangle(v0, v2, v3);

        // Top face (z = z + s)
        mesh.AppendTriangle(v4, v6, v5);
        mesh.AppendTriangle(v4, v7, v6);

        // Front face (y = y - s)
        mesh.AppendTriangle(v0, v5, v1);
        mesh.AppendTriangle(v0, v4, v5);

        // Back face (y = y + s)
        mesh.AppendTriangle(v2, v7, v3);
        mesh.AppendTriangle(v2, v6, v7);

        // Left face (x = x - s)
        mesh.AppendTriangle(v0, v3, v7);
        mesh.AppendTriangle(v0, v7, v4);

        // Right face (x = x + s)
        mesh.AppendTriangle(v1, v5, v6);
        mesh.AppendTriangle(v1, v6, v2);

        return mesh;
    }

    private void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        if (_document is null)
        {
            if (ResultMessageText is not null)
            {
                ResultMessageText.Text = "No mesh loaded.";
            }
            return;
        }

        if (_document.Mesh is null)
        {
            if (ResultMessageText is not null)
            {
                ResultMessageText.Text = "No mesh loaded.";
            }
            return;
        }

        try
        {
            // Create a test fixture secondary mesh for MVP
            var secondaryMesh = CreateTestFixtureCube();

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
