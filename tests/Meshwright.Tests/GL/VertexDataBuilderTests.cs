using System.Numerics;
using Meshwright.Geometry;
using Meshwright.Rendering.GL;
using Xunit;

namespace Meshwright.Tests.GL;

public class VertexDataBuilderTests
{
    private static TriangleMesh TwoTriangleMesh()
    {
        var positions = new Vector3[]
        {
            new(0, 0, 0), new(1, 0, 0), new(0, 1, 0),
            new(1, 1, 0), new(1, 0, 0), new(0, 1, 0),
        };
        var normals = new Vector3[]
        {
            new(0, 0, 1),
            new(0, 0, -1),
        };
        return new TriangleMesh(positions, normals);
    }

    [Fact]
    public void BuildPositions_ReturnsAllVertexPositionsUnchanged()
    {
        TriangleMesh mesh = TwoTriangleMesh();

        Vector3[] result = VertexDataBuilder.BuildPositions(mesh);

        Assert.Equal(6, result.Length);
        Assert.Equal(mesh.Positions, result);
    }

    [Fact]
    public void BuildPerVertexNormals_DuplicatesEachTriangleNormalThreeTimesInOrder()
    {
        TriangleMesh mesh = TwoTriangleMesh();

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
