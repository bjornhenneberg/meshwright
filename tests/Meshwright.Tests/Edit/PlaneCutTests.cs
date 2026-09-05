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
    private static DMesh3 BuildCube(double size) => BuildBox(size, size, size);

    /// <summary>Helper to build a simple closed axis-aligned box for testing.</summary>
    private static DMesh3 BuildBox(double sizeX, double sizeY, double sizeZ)
    {
        var mesh = new DMesh3();
        int v000 = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int v100 = mesh.AppendVertex(new Vector3d(sizeX, 0, 0));
        int v110 = mesh.AppendVertex(new Vector3d(sizeX, sizeY, 0));
        int v010 = mesh.AppendVertex(new Vector3d(0, sizeY, 0));
        int v001 = mesh.AppendVertex(new Vector3d(0, 0, sizeZ));
        int v101 = mesh.AppendVertex(new Vector3d(sizeX, 0, sizeZ));
        int v111 = mesh.AppendVertex(new Vector3d(sizeX, sizeY, sizeZ));
        int v011 = mesh.AppendVertex(new Vector3d(0, sizeY, sizeZ));

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

    /// <summary>
    /// A cut has to remove the side it discarded. Asserting only that some triangles survive
    /// passes just as happily when the operation appends the cut result on top of the geometry it
    /// was supposed to replace, which leaves the discarded half sitting in the mesh.
    /// </summary>
    [Fact]
    public void PlaneCutKeepSideOperation_LeavesNothingOnTheDiscardedSide()
    {
        var document = new MeshDocument();
        document.Load(BuildCube(10.0));

        var planePoint = new Vector3d(5.0, 5.0, 5.0);
        Vector3d planeNormal = Vector3d.AxisZ.Normalized;
        document.Apply(new PlaneCutKeepSideOperation(planePoint, planeNormal));

        DMesh3 cut = document.Mesh!;
        const double tolerance = 1e-6;
        foreach (int tid in cut.TriangleIndices())
        {
            Vector3d centroid = cut.GetTriCentroid(tid);
            double signedDistance = (centroid - planePoint).Dot(planeNormal);
            Assert.True(
                signedDistance > -tolerance,
                $"Triangle {tid} sits {-signedDistance:0.###} below the cut plane, on the discarded side.");
        }
    }

    [Fact]
    public void PlaneCutDiscardSideOperation_LeavesNothingOnTheDiscardedSide()
    {
        var document = new MeshDocument();
        document.Load(BuildCube(10.0));

        var planePoint = new Vector3d(5.0, 5.0, 5.0);
        Vector3d planeNormal = Vector3d.AxisZ.Normalized;
        document.Apply(new PlaneCutDiscardSideOperation(planePoint, planeNormal));

        DMesh3 cut = document.Mesh!;
        const double tolerance = 1e-6;
        foreach (int tid in cut.TriangleIndices())
        {
            Vector3d centroid = cut.GetTriCentroid(tid);
            double signedDistance = (centroid - planePoint).Dot(planeNormal);
            Assert.True(
                signedDistance < tolerance,
                $"Triangle {tid} sits {signedDistance:0.###} above the cut plane, on the discarded side.");
        }
    }

    /// <summary>
    /// Split is the mode that keeps both halves. It used to be wired to the Keep operation
    /// behind a "for now" comment, so choosing it silently threw away the negative side.
    /// </summary>
    [Fact]
    public void PlaneCutSplitOperation_KeepsBothSidesAsSeparateShells()
    {
        var document = new MeshDocument();
        document.Load(BuildCube(10.0));

        var planePoint = new Vector3d(5.0, 5.0, 5.0);
        Vector3d planeNormal = Vector3d.AxisZ.Normalized;
        OperationResult result = document.Apply(new PlaneCutSplitOperation(planePoint, planeNormal));

        Assert.True(result.Changed);

        DMesh3 cut = document.Mesh!;
        int above = 0;
        int below = 0;
        foreach (int tid in cut.TriangleIndices())
        {
            double signedDistance = (cut.GetTriCentroid(tid) - planePoint).Dot(planeNormal);
            if (signedDistance > 1e-6)
            {
                above++;
            }
            else if (signedDistance < -1e-6)
            {
                below++;
            }
        }

        Assert.True(above > 0, "split should keep geometry on the positive side");
        Assert.True(below > 0, "split should keep geometry on the negative side");
    }

    /// <summary>
    /// Known defect, not yet fixed: a plane cut should leave closed shells — one for Keep/Discard,
    /// two for Split — but the cut result comes back as one disconnected shell per face.
    /// SplitCutTriangles welds correctly; the fault is upstream of the cap, in the cut-edge
    /// records feeding ExtractCapLoop, which are built with VertexAId and VertexBId set to the
    /// same vertex and so describe no connectivity for the loop to follow. Until that is fixed a
    /// cut result is not watertight and needs a repair pass before it will print.
    /// </summary>
    [Fact(Skip = "Known defect: cut-edge records are degenerate, so cap loops don't close the shell.")]
    public void PlaneCutKeepSideOperation_ProducesASingleClosedShell()
    {
        var document = new MeshDocument();
        document.Load(BuildCube(10.0));

        document.Apply(new PlaneCutKeepSideOperation(new Vector3d(5.0, 5.0, 5.0), Vector3d.AxisZ.Normalized));

        Assert.Equal(1, document.Report!.Statistics.ShellCount);
        Assert.Empty(document.Report.Issues.Where(issue => issue.Category == "BoundaryHole"));
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
        // Arrange - a non-cubic box cut off-center, so the two cuts aren't related by a
        // symmetry of the shape (an axis-aligned box sliced through its exact center is
        // topologically identical - same triangle count - no matter which face-normal you
        // cut along, so distinguishing cuts by triangle count alone doesn't work here;
        // bounding box shape is the meaningful, cut-plane-specific signal instead).
        var mesh1 = BuildBox(10.0, 6.0, 14.0);
        var mesh2 = BuildBox(10.0, 6.0, 14.0);
        var planeCut = new PlaneCut();

        Vector3d planePoint = new Vector3d(2.0, 3.0, 7.0);
        Vector3d normalZ = Vector3d.AxisZ.Normalized;
        Vector3d normalX = Vector3d.AxisX.Normalized;

        // Act
        PlaneCutResult resultZ = planeCut.Cut(mesh1, planePoint, normalZ, CutMode.Keep, HoleFillMode.Flat);
        PlaneCutResult resultX = planeCut.Cut(mesh2, planePoint, normalX, CutMode.Keep, HoleFillMode.Flat);

        // Assert - Different normals should keep geometrically different regions
        AxisAlignedBox3d boundsZ = resultZ.PositiveSideMesh.CachedBounds;
        AxisAlignedBox3d boundsX = resultX.PositiveSideMesh.CachedBounds;
        Assert.NotEqual(boundsZ.Extents, boundsX.Extents);
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
