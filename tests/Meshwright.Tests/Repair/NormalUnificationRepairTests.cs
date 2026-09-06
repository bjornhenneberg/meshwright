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

    // A closed cube wound consistently *inward* (negative signed volume) except one face, which
    // is wound the other way and so disagrees with its neighbors. Reproduces backlog item 21's
    // exact defect: NormalUnificationRepair reported "flipped 13 triangles" for a 12-triangle
    // mesh, because a triangle corrected once by MakeWindingConsistent (to match its inward
    // neighbors) and then flipped again by the whole-shell outward-orientation pass was counted
    // twice, even though its final winding matches where it started.
    private static DMesh3 BuildCubeInwardWithOneOutwardFace(double size = 10.0)
    {
        var mesh = new DMesh3();
        int v000 = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int v100 = mesh.AppendVertex(new Vector3d(size, 0, 0));
        int v110 = mesh.AppendVertex(new Vector3d(size, size, 0));
        int v010 = mesh.AppendVertex(new Vector3d(0, size, 0));
        int v001 = mesh.AppendVertex(new Vector3d(0, 0, size));
        int v101 = mesh.AppendVertex(new Vector3d(size, 0, size));
        int v111 = mesh.AppendVertex(new Vector3d(size, size, size));
        int v011 = mesh.AppendVertex(new Vector3d(0, size, size));

        void Quad(int p, int q, int r, int s)
        {
            mesh.AppendTriangle(p, q, r);
            mesh.AppendTriangle(p, r, s);
        }

        Quad(v000, v100, v110, v010); // bottom (indices 0,1)
        Quad(v001, v011, v111, v101); // top    (indices 2,3)
        Quad(v000, v001, v101, v100); // front  (indices 4,5)
        Quad(v010, v110, v111, v011); // back   (indices 6,7)
        Quad(v000, v010, v011, v001); // left   (indices 8,9)
        Quad(v100, v101, v111, v110); // right  (indices 10,11)

        if (SignedVolume(mesh) > 0.0)
        {
            // Whichever way the winding above actually points, make the whole shell inward
            // (negative signed volume) before introducing the one-face anomaly.
            foreach (int tid in mesh.TriangleIndices())
            {
                mesh.ReverseTriOrientation(tid);
            }
        }

        // Flip one face back the other way so it disagrees with its now-inward neighbors. Not
        // triangle 0, so MakeWindingConsistent's BFS seeds from an inward triangle and corrects
        // this one to match, rather than propagating the anomaly's orientation to everything else.
        mesh.ReverseTriOrientation(5);

        return mesh;
    }

    [Fact]
    public void Apply_OneAnomalyOnInwardCube_DoesNotDoubleCountTheTwiceFlippedTriangle()
    {
        DMesh3 mesh = BuildCubeInwardWithOneOutwardFace();
        Assert.True(SignedVolume(mesh) < 0.0, "test fixture must start inward (negative volume)");
        int triangleCountBeforeRepair = mesh.TriangleCount;

        NormalUnificationRepair.Result result = NormalUnificationRepair.Apply(mesh);

        // The invariant that catches the double-count bug: the reported flip count can never
        // exceed the number of triangles in the mesh (the bug reported 13 on a 12-triangle mesh).
        Assert.True(result.FlippedTriangleCount <= triangleCountBeforeRepair);

        // 11 of the 12 triangles genuinely end up with different winding than they started with
        // (the ones that were consistently inward); the twelfth — corrected once, then flipped
        // back by the whole-shell pass — ends up exactly where it started and must not count.
        Assert.Equal(11, result.FlippedTriangleCount);
        Assert.Equal(1, result.ShellCount);

        Assert.True(SignedVolume(mesh) > 0.0);
        Assert.Empty(new InvertedNormalDetector().Detect(mesh));
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
