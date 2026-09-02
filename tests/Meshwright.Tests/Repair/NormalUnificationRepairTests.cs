using g3;
using Meshwright.Core.Operations;
using Meshwright.Geometry.Diagnostics;
using Meshwright.Geometry.Repair;
using Xunit;

namespace Meshwright.Tests.Repair;

public class NormalUnificationRepairTests
{
    // Tetrahedron with vertices at the origin and the three unit axes, wound so every
    // triangle's normal points away from the opposite vertex (consistently outward).
    // Matches the fixture in InvertedNormalDetectorTests.
    private static DMesh3 BuildConsistentOutwardTetrahedron()
    {
        var mesh = new DMesh3();
        int v0 = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int v1 = mesh.AppendVertex(new Vector3d(1, 0, 0));
        int v2 = mesh.AppendVertex(new Vector3d(0, 1, 0));
        int v3 = mesh.AppendVertex(new Vector3d(0, 0, 1));

        mesh.AppendTriangle(v0, v2, v1); // opposite v3
        mesh.AppendTriangle(v0, v1, v3); // opposite v2
        mesh.AppendTriangle(v0, v3, v2); // opposite v1
        mesh.AppendTriangle(v1, v2, v3); // opposite v0

        return mesh;
    }

    // Same tetrahedron, but the face opposite v0 has its vertex order reversed, so it
    // disagrees with all three of its neighbors across their shared edges.
    private static DMesh3 BuildTetrahedronWithOneFlippedFace()
    {
        var mesh = new DMesh3();
        int v0 = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int v1 = mesh.AppendVertex(new Vector3d(1, 0, 0));
        int v2 = mesh.AppendVertex(new Vector3d(0, 1, 0));
        int v3 = mesh.AppendVertex(new Vector3d(0, 0, 1));

        mesh.AppendTriangle(v0, v2, v1);
        mesh.AppendTriangle(v0, v1, v3);
        mesh.AppendTriangle(v0, v3, v2);
        mesh.AppendTriangle(v1, v3, v2); // flipped: was (v1, v2, v3)

        return mesh;
    }

    // Every triangle of the outward tetrahedron above, wound the other way round. Each
    // triangle still agrees with its neighbors (winding is internally consistent), but the
    // whole shell now faces inward, i.e. negative signed volume.
    private static DMesh3 BuildInsideOutTetrahedron()
    {
        var mesh = new DMesh3();
        int v0 = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int v1 = mesh.AppendVertex(new Vector3d(1, 0, 0));
        int v2 = mesh.AppendVertex(new Vector3d(0, 1, 0));
        int v3 = mesh.AppendVertex(new Vector3d(0, 0, 1));

        mesh.AppendTriangle(v0, v1, v2);
        mesh.AppendTriangle(v0, v3, v1);
        mesh.AppendTriangle(v0, v2, v3);
        mesh.AppendTriangle(v1, v3, v2);

        return mesh;
    }

    private static double SignedVolume(DMesh3 mesh)
    {
        double volume = 0.0;
        foreach (int tid in mesh.TriangleIndices())
        {
            Index3i tri = mesh.GetTriangle(tid);
            Vector3d v0 = mesh.GetVertex(tri.a);
            Vector3d v1 = mesh.GetVertex(tri.b);
            Vector3d v2 = mesh.GetVertex(tri.c);
            volume += v0.Dot(v1.Cross(v2)) / 6.0;
        }

        return volume;
    }

    [Fact]
    public void Apply_OneFlippedFace_MakesWindingConsistentAndReportsChanged()
    {
        DMesh3 mesh = BuildTetrahedronWithOneFlippedFace();

        NormalUnificationRepair.Result result = NormalUnificationRepair.Apply(mesh);

        Assert.True(result.FlippedTriangleCount > 0);
        Assert.Equal(1, result.ShellCount);

        var detector = new InvertedNormalDetector();
        Assert.Empty(detector.Detect(mesh));
    }

    [Fact]
    public void Apply_InsideOutShell_FlipsWholeShellToPositiveVolume()
    {
        DMesh3 mesh = BuildInsideOutTetrahedron();
        Assert.True(SignedVolume(mesh) < 0.0);

        NormalUnificationRepair.Result result = NormalUnificationRepair.Apply(mesh);

        Assert.Equal(4, result.FlippedTriangleCount); // whole shell (4 triangles) corrected
        Assert.True(SignedVolume(mesh) > 0.0);

        var detector = new InvertedNormalDetector();
        Assert.Empty(detector.Detect(mesh));
    }

    [Fact]
    public void Apply_AlreadyConsistentAndOutward_ReportsNoFlips()
    {
        DMesh3 mesh = BuildConsistentOutwardTetrahedron();

        NormalUnificationRepair.Result result = NormalUnificationRepair.Apply(mesh);

        Assert.Equal(0, result.FlippedTriangleCount);
    }

    [Fact]
    public void UnifyNormalsOperation_ApplyMutatesPreviewDoesNot()
    {
        DMesh3 mesh = BuildTetrahedronWithOneFlippedFace();
        var operation = new UnifyNormalsOperation();

        OperationResult previewResult = operation.Preview(mesh);
        Assert.True(previewResult.Changed);
        // Preview must not have mutated the caller's mesh: the flipped face is still there.
        Assert.NotEmpty(new InvertedNormalDetector().Detect(mesh));

        OperationResult applyResult = operation.Apply(mesh);
        Assert.True(applyResult.Changed);
        Assert.Empty(new InvertedNormalDetector().Detect(mesh));
    }
}
