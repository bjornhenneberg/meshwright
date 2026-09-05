using System;
using System.IO;
using Avalonia.Headless.XUnit;
using g3;
using Meshwright.App;
using Meshwright.App.Views.Edit;
using Meshwright.Core;
using Meshwright.Geometry.Diagnostics;
using Meshwright.IO;
using Xunit;

namespace Meshwright.Tests;

/// <summary>
/// Boolean union/difference/intersection between the loaded mesh and a second mesh loaded from
/// disk (SPECIFICATION.md §5.1, backlog item 14). <see cref="BooleanPanel"/> used to run every
/// operation against a hardcoded fixture cube; these tests exercise the real load-a-second-file
/// path and assert the geometric invariants the operations must hold, not merely that they ran
/// (see §11, 2026-09-05).
/// </summary>
public class BooleanPanelTests
{
    /// <summary>Unit cube centered at the origin (±0.5 in each dimension).</summary>
    private static DMesh3 CreateUnitCube()
    {
        var mesh = new DMesh3(true);
        double s = 0.5;

        var v0 = mesh.AppendVertex(new Vector3d(-s, -s, -s));
        var v1 = mesh.AppendVertex(new Vector3d(s, -s, -s));
        var v2 = mesh.AppendVertex(new Vector3d(s, s, -s));
        var v3 = mesh.AppendVertex(new Vector3d(-s, s, -s));
        var v4 = mesh.AppendVertex(new Vector3d(-s, -s, s));
        var v5 = mesh.AppendVertex(new Vector3d(s, -s, s));
        var v6 = mesh.AppendVertex(new Vector3d(s, s, s));
        var v7 = mesh.AppendVertex(new Vector3d(-s, s, s));

        mesh.AppendTriangle(v0, v2, v1);
        mesh.AppendTriangle(v0, v3, v2);
        mesh.AppendTriangle(v4, v5, v6);
        mesh.AppendTriangle(v4, v6, v7);
        mesh.AppendTriangle(v0, v1, v5);
        mesh.AppendTriangle(v0, v5, v4);
        mesh.AppendTriangle(v2, v3, v7);
        mesh.AppendTriangle(v2, v7, v6);
        mesh.AppendTriangle(v0, v7, v3);
        mesh.AppendTriangle(v0, v4, v7);
        mesh.AppendTriangle(v1, v6, v5);
        mesh.AppendTriangle(v1, v2, v6);

        return mesh;
    }

    private static DMesh3 CreateOffsetCube(double dx, double dy, double dz)
    {
        var mesh = CreateUnitCube();
        foreach (int vid in mesh.VertexIndices())
        {
            mesh.SetVertex(vid, mesh.GetVertex(vid) + new Vector3d(dx, dy, dz));
        }
        return mesh;
    }

    /// <summary>Writes <paramref name="mesh"/> to a temp .stl file, standing in for a file a user
    /// would pick with the file dialog, and returns its path.</summary>
    private static string WriteTempStl(DMesh3 mesh)
    {
        string path = Path.Combine(Path.GetTempPath(), $"meshwright-boolean-test-{Guid.NewGuid():N}.stl");
        MeshExporter.ExportFile(path, mesh);
        return path;
    }

    private static void AssertClosedShell(DMesh3 mesh)
    {
        Assert.True(mesh.IsClosed());
        Assert.Empty(new BoundaryHoleDetector().Detect(mesh));
    }

    [AvaloniaFact]
    public void NewPanel_HasNoSecondaryMesh_ApplyDisabledAndStatusExplainsWhy()
    {
        var panel = new BooleanPanel();

        Assert.False(panel.HasSecondaryMesh);
        Assert.False(panel.IsApplyEnabled);
        Assert.Contains("no secondary mesh", panel.SecondaryMeshStatus, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void LoadSecondaryMeshFromPath_SetsNameStatsAndEnablesApply()
    {
        var document = new MeshDocument();
        document.Load(CreateUnitCube());

        var panel = new BooleanPanel();
        panel.SetDocument(document);

        string path = WriteTempStl(CreateOffsetCube(0.5, 0.5, 0.0));
        try
        {
            panel.LoadSecondaryMeshFromPath(path);

            Assert.True(panel.HasSecondaryMesh);
            Assert.Equal(Path.GetFileName(path), panel.SecondaryMeshName);
            Assert.Contains(Path.GetFileName(path), panel.SecondaryMeshStatus);
            Assert.True(panel.IsApplyEnabled);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [AvaloniaFact]
    public void ApplyClick_WithoutSecondaryMesh_ExplainsWhyRatherThanFallingBackToAFixture()
    {
        var document = new MeshDocument();
        document.Load(CreateUnitCube());

        var panel = new BooleanPanel();
        panel.SetDocument(document);

        panel.InvokeApplyForTesting();

        Assert.Contains("secondary mesh", panel.OperationResultMessage, StringComparison.OrdinalIgnoreCase);
        // The document must be untouched: no silent fixture-cube fallback.
        Assert.Equal(1, MeshStatistics.Compute(document.Mesh!).ShellCount);
        Assert.Equal(12, document.Mesh!.TriangleCount);
    }

    [AvaloniaFact]
    public void Union_OfLoadedMeshAndSecondFile_HasVolumeBetweenEitherAloneAndTheirSumAndUnionBoundingBox()
    {
        var document = new MeshDocument();
        var cubeA = CreateUnitCube();
        document.Load(cubeA);
        var statsBefore = MeshStatistics.Compute(cubeA);

        var cubeB = CreateOffsetCube(0.5, 0.5, 0.0);
        var statsB = MeshStatistics.Compute(cubeB);

        var panel = new BooleanPanel();
        panel.SetDocument(document);
        string path = WriteTempStl(cubeB);
        try
        {
            panel.LoadSecondaryMeshFromPath(path);
            panel.SelectOperationForTesting(0); // Union
            panel.InvokeApplyForTesting();
        }
        finally
        {
            File.Delete(path);
        }

        var statsAfter = MeshStatistics.Compute(document.Mesh!);

        Assert.True(statsAfter.Volume > statsBefore.Volume, "Union volume should exceed the primary alone.");
        Assert.True(statsAfter.Volume > statsB.Volume, "Union volume should exceed the secondary alone.");
        Assert.True(
            statsAfter.Volume < statsBefore.Volume + statsB.Volume,
            "Overlapping cubes must union to less than the sum of their volumes.");

        var expectedBounds = new AxisAlignedBox3d(statsBefore.BoundingBox.Min, statsBefore.BoundingBox.Max);
        expectedBounds.Contain(statsB.BoundingBox);
        Assert.Equal(expectedBounds.Min.x, statsAfter.BoundingBox.Min.x, 3);
        Assert.Equal(expectedBounds.Min.y, statsAfter.BoundingBox.Min.y, 3);
        Assert.Equal(expectedBounds.Min.z, statsAfter.BoundingBox.Min.z, 3);
        Assert.Equal(expectedBounds.Max.x, statsAfter.BoundingBox.Max.x, 3);
        Assert.Equal(expectedBounds.Max.y, statsAfter.BoundingBox.Max.y, 3);
        Assert.Equal(expectedBounds.Max.z, statsAfter.BoundingBox.Max.z, 3);

        AssertClosedShell(document.Mesh!);
        Assert.Equal(1, statsAfter.ShellCount);
    }

    [AvaloniaFact]
    public void Difference_OfLoadedMeshAndSecondFile_HasVolumeLessThanPrimaryAndBoundsWithinPrimary()
    {
        var document = new MeshDocument();
        var largeCube = CreateUnitCube();
        document.Load(largeCube);
        var statsBefore = MeshStatistics.Compute(largeCube);

        var smallCube = CreateOffsetCube(0.1, 0.1, 0.0);

        var panel = new BooleanPanel();
        panel.SetDocument(document);
        string path = WriteTempStl(smallCube);
        try
        {
            panel.LoadSecondaryMeshFromPath(path);
            panel.SelectOperationForTesting(1); // Difference
            panel.InvokeApplyForTesting();
        }
        finally
        {
            File.Delete(path);
        }

        var statsAfter = MeshStatistics.Compute(document.Mesh!);

        Assert.True(statsAfter.Volume < statsBefore.Volume, "Difference volume should be less than the primary's.");
        Assert.True(statsAfter.Volume > 0, "Difference should not be empty.");

        // Bounding box must be the primary's, or a subset of it.
        Assert.True(statsAfter.BoundingBox.Min.x >= statsBefore.BoundingBox.Min.x - 1e-6);
        Assert.True(statsAfter.BoundingBox.Min.y >= statsBefore.BoundingBox.Min.y - 1e-6);
        Assert.True(statsAfter.BoundingBox.Min.z >= statsBefore.BoundingBox.Min.z - 1e-6);
        Assert.True(statsAfter.BoundingBox.Max.x <= statsBefore.BoundingBox.Max.x + 1e-6);
        Assert.True(statsAfter.BoundingBox.Max.y <= statsBefore.BoundingBox.Max.y + 1e-6);
        Assert.True(statsAfter.BoundingBox.Max.z <= statsBefore.BoundingBox.Max.z + 1e-6);

        AssertClosedShell(document.Mesh!);
        Assert.Equal(1, statsAfter.ShellCount);
    }

    [AvaloniaFact]
    public void Intersection_OfLoadedMeshAndSecondFile_HasVolumeLessThanEitherAndBoundsInsideBoth()
    {
        var document = new MeshDocument();
        var cubeA = CreateUnitCube();
        document.Load(cubeA);
        var statsA = MeshStatistics.Compute(cubeA);

        var cubeB = CreateOffsetCube(0.5, 0.0, 0.0);
        var statsB = MeshStatistics.Compute(cubeB);

        var panel = new BooleanPanel();
        panel.SetDocument(document);
        string path = WriteTempStl(cubeB);
        try
        {
            panel.LoadSecondaryMeshFromPath(path);
            panel.SelectOperationForTesting(2); // Intersection
            panel.InvokeApplyForTesting();
        }
        finally
        {
            File.Delete(path);
        }

        var statsAfter = MeshStatistics.Compute(document.Mesh!);

        Assert.True(statsAfter.Volume < statsA.Volume, "Intersection volume should be less than the primary's.");
        Assert.True(statsAfter.Volume < statsB.Volume, "Intersection volume should be less than the secondary's.");
        Assert.True(statsAfter.Volume > 0, "Overlapping cubes must intersect to a non-empty volume.");

        // Bounding box must lie inside both inputs' bounding boxes.
        foreach (var bounds in new[] { statsA.BoundingBox, statsB.BoundingBox })
        {
            Assert.True(statsAfter.BoundingBox.Min.x >= bounds.Min.x - 1e-6);
            Assert.True(statsAfter.BoundingBox.Min.y >= bounds.Min.y - 1e-6);
            Assert.True(statsAfter.BoundingBox.Min.z >= bounds.Min.z - 1e-6);
            Assert.True(statsAfter.BoundingBox.Max.x <= bounds.Max.x + 1e-6);
            Assert.True(statsAfter.BoundingBox.Max.y <= bounds.Max.y + 1e-6);
            Assert.True(statsAfter.BoundingBox.Max.z <= bounds.Max.z + 1e-6);
        }

        AssertClosedShell(document.Mesh!);
        Assert.Equal(1, statsAfter.ShellCount);
    }

    [AvaloniaFact]
    public void MainWindow_BooleanPanel_LoadingSecondaryMeshAndApplying_UpdatesViewportThroughDocumentChanged()
    {
        // §11 (2026-09-05): every mesh change must announce itself through MeshDocument.Changed
        // so the viewport/diagnostics refresh; this proves the boolean panel's real (non-fixture)
        // path still goes through that event rather than mutating the mesh on the side.
        var window = new MainWindow();
        string primaryPath = WriteTempStl(CreateUnitCube());
        string secondaryPath = WriteTempStl(CreateOffsetCube(0.5, 0.5, 0.0));
        try
        {
            window.LoadFileForTesting(primaryPath);
            int trianglesBefore = window.CurrentReport!.Statistics.TriangleCount;

            var panel = window.BooleanPanelForTesting;
            Assert.NotNull(panel);
            panel.LoadSecondaryMeshFromPath(secondaryPath);
            Assert.True(panel.HasSecondaryMesh);

            panel.SelectOperationForTesting(0); // Union
            panel.InvokeApplyForTesting();

            Assert.NotEqual(trianglesBefore, window.CurrentReport!.Statistics.TriangleCount);
            Assert.StartsWith("Boolean Union", window.StatusMessage);
        }
        finally
        {
            File.Delete(primaryPath);
            File.Delete(secondaryPath);
        }
    }
}
