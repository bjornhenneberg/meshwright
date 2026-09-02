using System.Numerics;
using Meshwright.Rendering.GL;
using Xunit;

namespace Meshwright.Tests.GL;

public class VertexDataBuilderTests
{
    private static g3.DMesh3 TwoTriangleMesh()
    {
        var mesh = new g3.DMesh3();
        int a = mesh.AppendVertex(new g3.Vector3d(0, 0, 0));
        int b = mesh.AppendVertex(new g3.Vector3d(1, 0, 0));
        int c = mesh.AppendVertex(new g3.Vector3d(0, 1, 0));
        int d = mesh.AppendVertex(new g3.Vector3d(1, 1, 0));
        mesh.AppendTriangle(a, b, c);
        mesh.AppendTriangle(d, b, c);
        return mesh;
    }

    [Fact]
    public void BuildPositions_ReturnsAllVertexPositionsUnchanged()
    {
        g3.DMesh3 mesh = TwoTriangleMesh();

        Vector3[] result = VertexDataBuilder.BuildPositions(mesh);

        Assert.Equal(6, result.Length);
        Assert.Equal(new[]
        {
            new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0),
            new Vector3(1, 1, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0),
        }, result);
    }

    [Fact]
    public void BuildPerVertexNormals_DuplicatesEachTriangleNormalThreeTimesInOrder()
    {
        g3.DMesh3 mesh = TwoTriangleMesh();

        Vector3[] result = VertexDataBuilder.BuildPerVertexNormals(mesh);

        Assert.Equal(6, result.Length);
        Assert.Equal(new Vector3(0, 0, 1), result[0]);
        Assert.Equal(new Vector3(0, 0, 1), result[1]);
        Assert.Equal(new Vector3(0, 0, 1), result[2]);
        Assert.Equal(new Vector3(0, 0, -1), result[3]);
        Assert.Equal(new Vector3(0, 0, -1), result[4]);
        Assert.Equal(new Vector3(0, 0, -1), result[5]);
    }

    [Fact]
    public void Flatten_ProducesContiguousXyzFloats()
    {
        var vectors = new Vector3[] { new(1, 2, 3), new(4, 5, 6) };

        float[] result = VertexDataBuilder.Flatten(vectors);

        Assert.Equal(new float[] { 1, 2, 3, 4, 5, 6 }, result);
    }
}
