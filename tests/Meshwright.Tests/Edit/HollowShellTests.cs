using g3;
using Meshwright.Core.Operations;
using Meshwright.Geometry.Edit;
using Xunit;

namespace Meshwright.Tests.Edit;

public class HollowShellTests
{
    // Low resolution throughout: this suite is about correctness of the offset/orientation/merge
    // logic (does hollowing produce two individually-closed, correctly-oriented shells with the
    // right cavity size?), not production-quality output, so keeping the grid small keeps the
    // tests fast — mirrors Repair/VoxelRemeshRepairTests.cs.
    private const int LowResolution = 24;

    private static DMesh3 BuildCube(Vector3d origin, double size)
    {
        var mesh = new DMesh3();
        int v000 = mesh.AppendVertex(origin + new Vector3d(0, 0, 0) * size);
        int v100 = mesh.AppendVertex(origin + new Vector3d(1, 0, 0) * size);
        int v110 = mesh.AppendVertex(origin + new Vector3d(1, 1, 0) * size);
        int v010 = mesh.AppendVertex(origin + new Vector3d(0, 1, 0) * size);
        int v001 = mesh.AppendVertex(origin + new Vector3d(0, 0, 1) * size);
        int v101 = mesh.AppendVertex(origin + new Vector3d(1, 0, 1) * size);
        int v111 = mesh.AppendVertex(origin + new Vector3d(1, 1, 1) * size);
        int v011 = mesh.AppendVertex(origin + new Vector3d(0, 1, 1) * size);

        void Quad(int p, int q, int r, int s)
        {
            mesh.AppendTriangle(p, q, r);
            mesh.AppendTriangle(p, r, s);
        }

        Quad(v000, v010, v110, v100); // bottom (z=0), outward normal -z
        Quad(v001, v101, v111, v011); // top (z=1), outward normal +z
        Quad(v000, v100, v101, v001); // front (y=0), outward normal -y
        Quad(v010, v011, v111, v110); // back (y=1), outward normal +y
        Quad(v000, v001, v011, v010); // left (x=0), outward normal -x
        Quad(v100, v110, v111, v101); // right (x=1), outward normal +x

        return mesh;
    }

    /// <summary>Every triangle has three distinct vertices and nonzero area.</summary>
    private static void AssertNoDegenerateTriangles(DMesh3 mesh)
    {
        foreach (int tid in mesh.TriangleIndices())
        {
            Index3i tri = mesh.GetTriangle(tid);
            Assert.True(tri.a != tri.b && tri.b != tri.c && tri.a != tri.c, $"Triangle {tid} repeats a vertex.");

            Vector3d v0 = mesh.GetVertex(tri.a);
            Vector3d v1 = mesh.GetVertex(tri.b);
            Vector3d v2 = mesh.GetVertex(tri.c);
            double area = 0.5 * (v1 - v0).Cross(v2 - v0).Length;
            Assert.True(area > 1e-12, $"Triangle {tid} has near-zero area ({area}).");
        }
    }

    /// <summary>
    /// True volume of a size-<paramref name="s"/> cube minus a size-<paramref name="inner"/> cube
    /// shell, i.e. the expected shell material volume once the cavity is subtracted.
    /// </summary>
    private static double ExpectedShellVolume(double outerSize, double innerSize) =>
        Math.Pow(outerSize, 3) - Math.Max(0, Math.Pow(innerSize, 3));

    [Fact]
    public void Hollow_Cube_AddsInnerShell_ReducesVolume_BothShellsClosedAndManifold()
    {
        const double outerSize = 20.0;
        const double wallThickness = 2.0;

        DMesh3 mesh = BuildCube(Vector3d.Zero, outerSize);
        int trianglesBefore = mesh.TriangleCount;

        var hollow = new HollowShell();
        HollowResult result = hollow.Hollow(mesh, wallThickness, LowResolution);

        Assert.True(result.CavityAdded);
        Assert.True(result.TrianglesAfter > trianglesBefore);
        Assert.Equal(mesh.TriangleCount, result.TrianglesAfter);

        // The combined mesh should have no open boundary — both shells are individually closed,
        // so their union has none either.
        var loops = new MeshBoundaryLoops(mesh);
        Assert.Empty(loops.Loops);

        // Two disjoint closed shells: outer (unchanged) + inner cavity wall.
        var components = new MeshConnectedComponents(mesh);
        components.FindConnectedT();
        Assert.Equal(2, components.Count);

        AssertNoDegenerateTriangles(mesh);

        // Volume should have dropped roughly to a shell of the requested thickness: expected
        // interior cube edge is outerSize - 2*wallThickness (offset inward on every face).
        double expectedInnerSize = outerSize - 2 * wallThickness;
        double expectedVolume = ExpectedShellVolume(outerSize, expectedInnerSize);
        double expectedOuterVolume = Math.Pow(outerSize, 3);

        Assert.Equal(expectedOuterVolume, result.VolumeBefore, 0.5);
        // Marching-cubes at this (deliberately coarse) test resolution won't hit the analytic
        // value exactly; assert it's in the right ballpark (well below the solid volume, roughly
        // near the analytic shell volume).
        Assert.True(result.VolumeAfter < result.VolumeBefore * 0.9, "Hollowing should meaningfully reduce enclosed volume.");
        Assert.InRange(result.VolumeAfter, expectedVolume * 0.5, expectedVolume * 1.5);
    }

    [Fact]
    public void Hollow_InnerShellNormals_FaceIntoCavity()
    {
        const double outerSize = 20.0;
        const double wallThickness = 2.0;

        DMesh3 mesh = BuildCube(Vector3d.Zero, outerSize);

        var hollow = new HollowShell();
        hollow.Hollow(mesh, wallThickness, LowResolution);

        var components = new MeshConnectedComponents(mesh);
        components.FindConnectedT();
        Assert.Equal(2, components.Count);

        Vector3d center = new Vector3d(outerSize / 2, outerSize / 2, outerSize / 2);

        // Identify the inner shell as whichever component has the smaller enclosed volume, then
        // check that its face normals point back toward the cavity center (inward) rather than
        // away from it — i.e. each triangle's outward-facing side (per its own winding) faces the
        // center, the opposite of a normally-oriented outward shell.
        double VolumeOf(MeshConnectedComponents.Component c)
        {
            double v = 0.0;
            foreach (int tid in c.Indices)
            {
                Index3i tri = mesh.GetTriangle(tid);
                Vector3d v0 = mesh.GetVertex(tri.a);
                Vector3d v1 = mesh.GetVertex(tri.b);
                Vector3d v2 = mesh.GetVertex(tri.c);
                v += v0.Dot(v1.Cross(v2)) / 6.0;
            }
            return Math.Abs(v);
        }

        MeshConnectedComponents.Component inner = components.Components[0];
        MeshConnectedComponents.Component outer = components.Components[1];
        if (VolumeOf(inner) > VolumeOf(outer))
        {
            (inner, outer) = (outer, inner);
        }

        int facingCenter = 0;
        int facingAway = 0;
        foreach (int tid in inner.Indices)
        {
            Index3i tri = mesh.GetTriangle(tid);
            Vector3d v0 = mesh.GetVertex(tri.a);
            Vector3d v1 = mesh.GetVertex(tri.b);
            Vector3d v2 = mesh.GetVertex(tri.c);
            Vector3d faceNormal = (v1 - v0).Cross(v2 - v0);
            Vector3d faceCentroid = (v0 + v1 + v2) / 3.0;
            Vector3d towardCenter = center - faceCentroid;

            if (faceNormal.Dot(towardCenter) > 0)
            {
                facingCenter++;
            }
            else
            {
                facingAway++;
            }
        }

        Assert.True(facingCenter > facingAway, $"Expected most inner-shell normals to face the cavity center; facingCenter={facingCenter}, facingAway={facingAway}.");
    }

    [Fact]
    public void Hollow_WallThicknessTooLargeForMesh_LeavesMeshUnchanged()
    {
        const double outerSize = 10.0;

        DMesh3 mesh = BuildCube(Vector3d.Zero, outerSize);
        int trianglesBefore = mesh.TriangleCount;

        var hollow = new HollowShell();
        // A wall thickness comparable to the whole cube leaves no interior at all.
        HollowResult result = hollow.Hollow(mesh, outerSize, LowResolution);

        Assert.False(result.CavityAdded);
        Assert.Equal(trianglesBefore, mesh.TriangleCount);
        Assert.Equal(trianglesBefore, result.TrianglesAfter);
        Assert.Equal(result.VolumeBefore, result.VolumeAfter);
    }

    [Fact]
    public void Hollow_NonPositiveWallThickness_Throws()
    {
        DMesh3 mesh = BuildCube(Vector3d.Zero, 10.0);
        var hollow = new HollowShell();

        Assert.Throws<ArgumentOutOfRangeException>(() => hollow.Hollow(mesh, 0.0, LowResolution));
        Assert.Throws<ArgumentOutOfRangeException>(() => hollow.Hollow(mesh, -1.0, LowResolution));
    }

    [Fact]
    public void Operation_Apply_MutatesMesh_AndPreview_LeavesCallersMeshUntouched()
    {
        DMesh3 mesh = BuildCube(Vector3d.Zero, 20.0);
        int trianglesBefore = mesh.TriangleCount;

        var operation = new HollowOperation(2.0, LowResolution);

        OperationResult previewResult = operation.Preview(mesh);
        Assert.True(previewResult.Changed);
        Assert.Equal(trianglesBefore, mesh.TriangleCount); // Preview must not mutate the caller's mesh.

        OperationResult applyResult = operation.Apply(mesh);
        Assert.True(applyResult.Changed);
        Assert.Contains("Hollowed to 2mm wall thickness", applyResult.Summary);
        Assert.Contains("% of volume", applyResult.Summary);
        Assert.True(mesh.TriangleCount > trianglesBefore);

        var loops = new MeshBoundaryLoops(mesh);
        Assert.Empty(loops.Loops);
    }

    [Fact]
    public void Operation_WallThicknessTooLarge_ReportsUnchanged()
    {
        DMesh3 mesh = BuildCube(Vector3d.Zero, 10.0);
        int trianglesBefore = mesh.TriangleCount;

        var operation = new HollowOperation(10.0, LowResolution);
        OperationResult result = operation.Apply(mesh);

        Assert.False(result.Changed);
        Assert.Equal(trianglesBefore, mesh.TriangleCount);
        Assert.Contains("Mesh left unchanged", result.Summary);
    }
}
