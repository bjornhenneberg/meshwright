using System.Numerics;
using Meshwright.Geometry;
using Xunit;

namespace Meshwright.Tests;

public class TriangleMeshTests
{
    [Fact]
    public void GetBounds_UnitCubeAtOffset_ReturnsCorrectCenterAndRadius()
    {
        var offset = new Vector3(10f, 20f, 30f);
        Vector3[] corners =
        {
            offset + new Vector3(-1f, -1f, -1f),
            offset + new Vector3(1f, -1f, -1f),
            offset + new Vector3(1f, 1f, -1f),
            offset + new Vector3(-1f, -1f, 1f),
            offset + new Vector3(1f, 1f, 1f),
            offset + new Vector3(-1f, 1f, 1f),
        };
        var positions = new Vector3[corners.Length * 3];
        for (int i = 0; i < corners.Length; i++)
        {
            positions[i * 3] = corners[i];
            positions[i * 3 + 1] = corners[i];
            positions[i * 3 + 2] = corners[i];
        }
        var normals = new Vector3[corners.Length];
        var mesh = new TriangleMesh(positions, normals);

        (Vector3 center, float radius) = mesh.GetBounds();

        Assert.Equal(offset.X, center.X, 4);
        Assert.Equal(offset.Y, center.Y, 4);
        Assert.Equal(offset.Z, center.Z, 4);
        Assert.Equal(MathF.Sqrt(3f), radius, 4);
    }

    [Fact]
    public void GetBounds_DegenerateSinglePointMesh_HasNonZeroRadiusFloor()
    {
        var point = new Vector3(5f, 5f, 5f);
        var positions = new[] { point, point, point };
        var normals = new[] { Vector3.UnitY };
        var mesh = new TriangleMesh(positions, normals);

        (Vector3 center, float radius) = mesh.GetBounds();

        Assert.Equal(point, center);
        Assert.True(radius > 0f);
    }
}
