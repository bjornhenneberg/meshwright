using g3;
using Meshwright.Core.Operations;
using Meshwright.Geometry.Repair;
using Xunit;

namespace Meshwright.Tests.Repair;

public class VoxelRemeshRepairTests
{
    // Low resolution throughout: this suite is about pipeline correctness (does the SDF + marching
    // cubes round trip produce a watertight mesh?), not production-quality output, so keeping the
    // grid small keeps the tests fast.
    private const int LowResolution = 20;

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

    [Fact]
    public void Remesh_WatertightCube_ProducesWatertightNonemptyMesh()
    {
        DMesh3 mesh = BuildCube(Vector3d.Zero, 10.0);

        var repair = new VoxelRemeshRepair();
        VoxelRemeshResult result = repair.Remesh(mesh, LowResolution);

        Assert.True(result.TrianglesAfter > 0);
        Assert.Equal(mesh.TriangleCount, result.TrianglesAfter);

        var loops = new MeshBoundaryLoops(mesh);
        Assert.Empty(loops.Loops);
    }

    [Fact]
    public void Remesh_CubeWithHole_ProducesWatertightMesh()
    {
        // Remove one triangle from an otherwise-closed cube: the sledgehammer promise is that
        // voxel remesh always produces something watertight, even from broken input.
        DMesh3 mesh = BuildCube(Vector3d.Zero, 10.0);
        mesh.RemoveTriangle(0);

        var loopsBefore = new MeshBoundaryLoops(mesh);
        Assert.NotEmpty(loopsBefore.Loops); // sanity: the input really does have a hole

        var repair = new VoxelRemeshRepair();
        VoxelRemeshResult result = repair.Remesh(mesh, LowResolution);

        Assert.True(result.TrianglesAfter > 0);

        var loopsAfter = new MeshBoundaryLoops(mesh);
        Assert.Empty(loopsAfter.Loops);
    }

    [Fact]
    public void Operation_Apply_MutatesMesh_AndPreview_LeavesCallersMeshUntouched()
    {
        DMesh3 mesh = BuildCube(Vector3d.Zero, 10.0);
        int triangleCountBefore = mesh.TriangleCount;

        var operation = new VoxelRemeshOperation(LowResolution);

        OperationResult previewResult = operation.Preview(mesh);
        Assert.True(previewResult.Changed);
        Assert.Equal(triangleCountBefore, mesh.TriangleCount); // Preview must not mutate the caller's mesh.

        OperationResult applyResult = operation.Apply(mesh);
        Assert.True(applyResult.Changed);
        Assert.Contains($"{triangleCountBefore} ->", applyResult.Summary);
        Assert.Contains($"grid resolution {LowResolution}", applyResult.Summary);
        Assert.True(mesh.TriangleCount > 0);

        var loops = new MeshBoundaryLoops(mesh);
        Assert.Empty(loops.Loops);
    }
}
