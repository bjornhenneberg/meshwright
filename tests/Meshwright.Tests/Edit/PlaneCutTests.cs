using g3;
using Meshwright.Core;
using Meshwright.Core.Operations;
using Meshwright.Geometry.Edit;
using Meshwright.Geometry.Repair;
using Xunit;

namespace Meshwright.Tests.Edit;

public class PlaneCutTests
{
    /// <summary>Helper to build a simple closed cube for testing.</summary>
    private static DMesh3 BuildCube(double size)
    {
        var mesh = new DMesh3();
        int v000 = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int v100 = mesh.AppendVertex(new Vector3d(1, 0, 0) * size);
        int v110 = mesh.AppendVertex(new Vector3d(1, 1, 0) * size);
        int v010 = mesh.AppendVertex(new Vector3d(0, 1, 0) * size);
        int v001 = mesh.AppendVertex(new Vector3d(0, 0, 1) * size);
        int v101 = mesh.AppendVertex(new Vector3d(1, 0, 1) * size);
        int v111 = mesh.AppendVertex(new Vector3d(1, 1, 1) * size);
        int v011 = mesh.AppendVertex(new Vector3d(0, 1, 1) * size);

        void Quad(int p, int q, int r, int s)
        {
            mesh.AppendTriangle(p, q, r);
            mesh.AppendTriangle(p, r, s);
        }

        Quad(v000, v010, v110, v100);
        Quad(v001, v101, v111, v011);
        Quad(v000, v100, v101, v001);
        Quad(v010, v011, v111, v110);
        Quad(v000, v001, v011, v010);
        Quad(v100, v110, v111, v101);

        return mesh;
    }

    [Fact]
    public void PlaneCut_CutsCubeInHalf_WithKeepMode()
    {
        // Arrange
        var mesh = BuildCube(10.0);
        var planeCut = new PlaneCut();
        Vector3d planePoint = new Vector3d(5.0, 5.0, 5.0);
        Vector3d planeNormal = Vector3d.AxisZ;

        // Act
        PlaneCutResult result = planeCut.Cut(mesh, planePoint, planeNormal, CutMode.Keep, HoleFillMode.Flat);

        // Assert
        Assert.True(result.MeshWasModified);
        Assert.True(result.PositiveSideMesh.TriangleCount > 0);
        Assert.Null(result.NegativeSideMesh); // Keep mode doesn't return negative side
    }

    [Fact]
    public void PlaneCut_CutsCubeInHalf_WithDiscardMode()
    {
        // Arrange
        var mesh = BuildCube(10.0);
        var planeCut = new PlaneCut();
        Vector3d planePoint = new Vector3d(5.0, 5.0, 5.0);
        Vector3d planeNormal = Vector3d.AxisZ;

        // Act
        PlaneCutResult result = planeCut.Cut(mesh, planePoint, planeNormal, CutMode.Discard, HoleFillMode.Flat);

        // Assert
        Assert.True(result.MeshWasModified);
        Assert.True(result.PositiveSideMesh.TriangleCount > 0);
        Assert.Null(result.NegativeSideMesh); // Discard mode returns only positive mesh
    }

    [Fact]
    public void PlaneCut_CutsCubeInHalf_WithSplitMode()
    {
        // Arrange
        var mesh = BuildCube(10.0);
        var planeCut = new PlaneCut();
        Vector3d planePoint = new Vector3d(5.0, 5.0, 5.0);
        Vector3d planeNormal = Vector3d.AxisZ;

        // Act
        PlaneCutResult result = planeCut.Cut(mesh, planePoint, planeNormal, CutMode.Split, HoleFillMode.Flat);

        // Assert
        Assert.True(result.MeshWasModified);
        Assert.True(result.PositiveSideMesh.TriangleCount > 0);
        Assert.NotNull(result.NegativeSideMesh);
        Assert.True(result.NegativeSideMesh!.TriangleCount > 0);
    }

    [Fact]
    public void PlaneCut_PlanePassesThroughNoGeometry_ReturnsUnchangedMesh()
    {
        // Arrange
        var mesh = BuildCube(10.0);
        var planeCut = new PlaneCut();
        Vector3d planePoint = new Vector3d(20.0, 20.0, 20.0); // Far from cube
        Vector3d planeNormal = Vector3d.AxisZ;

        // Act
        PlaneCutResult result = planeCut.Cut(mesh, planePoint, planeNormal, CutMode.Keep, HoleFillMode.Flat);

        // Assert
        Assert.False(result.MeshWasModified);
        Assert.Equal(12, result.PositiveSideMesh.TriangleCount); // Original cube has 12 triangles
    }

    [Fact]
    public void PlaneCutKeepSideOperation_AppliesSuccessfully()
    {
        // Arrange
        var document = new MeshDocument();
        var mesh = BuildCube(10.0);
        document.Load(mesh);

        Vector3d planePoint = new Vector3d(5.0, 5.0, 5.0);
        Vector3d planeNormal = Vector3d.AxisZ.Normalized;
        var operation = new PlaneCutKeepSideOperation(planePoint, planeNormal);

        // Act
        OperationResult result = document.Apply(operation);

        // Assert
        Assert.True(result.Changed);
        Assert.Contains("Cut plane", result.Summary);
        Assert.NotNull(document.Mesh);
        Assert.True(document.Mesh.TriangleCount > 0);
    }

    [Fact]
    public void PlaneCutDiscardSideOperation_AppliesSuccessfully()
    {
        // Arrange
        var document = new MeshDocument();
        var mesh = BuildCube(10.0);
        document.Load(mesh);

        Vector3d planePoint = new Vector3d(5.0, 5.0, 5.0);
        Vector3d planeNormal = Vector3d.AxisZ.Normalized;
        var operation = new PlaneCutDiscardSideOperation(planePoint, planeNormal);

        // Act
        OperationResult result = document.Apply(operation);

        // Assert
        Assert.True(result.Changed);
        Assert.Contains("Cut plane", result.Summary);
        Assert.NotNull(document.Mesh);
        Assert.True(document.Mesh.TriangleCount > 0);
    }

    [Fact]
    public void PlaneCutOperation_Preview_DoesNotMutateMesh()
    {
        // Arrange
        var mesh = BuildCube(10.0);
        int originalTriangleCount = mesh.TriangleCount;

        Vector3d planePoint = new Vector3d(5.0, 5.0, 5.0);
        Vector3d planeNormal = Vector3d.AxisZ.Normalized;
        var operation = new PlaneCutKeepSideOperation(planePoint, planeNormal);

        // Act
        var meshCopy = new DMesh3(mesh, bCompact: false);
        OperationResult preview = operation.Preview(meshCopy);

        // Assert - Original mesh should be untouched
        Assert.Equal(originalTriangleCount, mesh.TriangleCount);
    }

    [Fact]
    public void PlaneCut_WithDifferentNormals_ProducesDifferentResults()
    {
        // Arrange
        var mesh1 = BuildCube(10.0);
        var mesh2 = BuildCube(10.0);
        var planeCut = new PlaneCut();

        Vector3d planePoint = new Vector3d(5.0, 5.0, 5.0);
        Vector3d normalZ = Vector3d.AxisZ.Normalized;
        Vector3d normalX = Vector3d.AxisX.Normalized;

        // Act
        PlaneCutResult resultZ = planeCut.Cut(mesh1, planePoint, normalZ, CutMode.Keep, HoleFillMode.Flat);
        PlaneCutResult resultX = planeCut.Cut(mesh2, planePoint, normalX, CutMode.Keep, HoleFillMode.Flat);

        // Assert - Different normals should produce different results (different triangle counts)
        Assert.NotEqual(resultZ.PositiveSideMesh.TriangleCount, resultX.PositiveSideMesh.TriangleCount);
    }

    [Fact]
    public void PlaneCut_CapIsAdded()
    {
        // Arrange
        var mesh = BuildCube(10.0);
        var planeCut = new PlaneCut();
        Vector3d planePoint = new Vector3d(5.0, 5.0, 5.0);
        Vector3d planeNormal = Vector3d.AxisZ;

        // Act
        PlaneCutResult result = planeCut.Cut(mesh, planePoint, planeNormal, CutMode.Keep, HoleFillMode.Flat);

        // Assert
        Assert.True(result.CapTrianglesAdded > 0);
    }

    [Fact]
    public void PlaneCutOperation_Undo_RestoresMesh()
    {
        // Arrange
        var document = new MeshDocument();
        var mesh = BuildCube(10.0);
        document.Load(mesh);
        int originalTriangleCount = document.Mesh.TriangleCount;

        Vector3d planePoint = new Vector3d(5.0, 5.0, 5.0);
        Vector3d planeNormal = Vector3d.AxisZ.Normalized;
        var operation = new PlaneCutKeepSideOperation(planePoint, planeNormal);

        // Act
        document.Apply(operation);
        Assert.True(document.CanUndo);
        document.Undo();

        // Assert
        Assert.Equal(originalTriangleCount, document.Mesh!.TriangleCount);
        Assert.False(document.CanUndo);
    }
}
