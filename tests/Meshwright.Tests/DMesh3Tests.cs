using g3;
using Xunit;

namespace Meshwright.Tests;

public class DMesh3Tests
{
    [Fact]
    public void IndexedVerticesWithEqualCoordinatesRetainDistinctIds()
    {
        var mesh = new DMesh3();
        int first = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int duplicate = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int other = mesh.AppendVertex(new Vector3d(1, 0, 0));

        mesh.AppendTriangle(first, duplicate, other);

        Assert.Equal(3, mesh.VertexCount);
        Assert.Equal(1, mesh.TriangleCount);
        Assert.Equal(first, mesh.GetTriangle(0).a);
        Assert.Equal(duplicate, mesh.GetTriangle(0).b);
    }

    [Fact]
    public void GetBounds_UnitCubeAtOffset_ReturnsCorrectCenterAndRadius()
    {
        var mesh = new DMesh3();
        foreach (var corner in new[]
        {
            new Vector3d(9, 19, 29), new Vector3d(11, 19, 29), new Vector3d(11, 21, 29),
            new Vector3d(9, 21, 31), new Vector3d(11, 21, 31), new Vector3d(9, 21, 31),
        }) mesh.AppendVertex(corner);

        AxisAlignedBox3d bounds = mesh.GetBounds();
        Vector3d center = bounds.Center;
        double radius = bounds.DiagonalLength / 2.0;

        Assert.Equal(10, center.x, 4);
        Assert.Equal(20, center.y, 4);
        Assert.Equal(30, center.z, 4);
        Assert.Equal(Math.Sqrt(3), radius, 4);
    }

    [Fact]
    public void TopologyHelpers_FindEdgeConnectedComponentAndBoundaryLoop()
    {
        var mesh = new DMesh3();
        int a = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int b = mesh.AppendVertex(new Vector3d(1, 0, 0));
        int c = mesh.AppendVertex(new Vector3d(1, 1, 0));
        int d = mesh.AppendVertex(new Vector3d(0, 1, 0));
        int touchingOnly = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int e = mesh.AppendVertex(new Vector3d(-1, 0, 0));
        int f = mesh.AppendVertex(new Vector3d(0, -1, 0));
        mesh.AppendTriangle(a, b, c);
        mesh.AppendTriangle(a, c, d);
        mesh.AppendTriangle(touchingOnly, e, f);

        var components = new MeshConnectedComponents(mesh);
        components.FindConnectedT();
        var loops = new MeshBoundaryLoops(mesh);

        Assert.Equal(2, components.Components.Count);
        Assert.Contains(components.Components, component => component.Indices.Length == 2);
        Assert.Equal(2, loops.Loops.Count);
        Assert.Contains(loops.Loops, loop => loop.Vertices.Length == 4);
    }
}