using System.Numerics;
using Meshwright.Geometry;

namespace Meshwright.Tests.Gpu;

/// <summary>Small, hand-built <see cref="TriangleMesh"/> fixtures for GPU regression tests.</summary>
internal static class TriangleMeshFixtures
{
    internal readonly record struct SingleTriangle(TriangleMesh Mesh);

    internal readonly record struct Cube(TriangleMesh Mesh);

    /// <summary>A single flat triangle facing the default camera, centered near the origin.</summary>
    internal static SingleTriangle BuildSingleTriangle()
    {
        var positions = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3(0.5f, -0.5f, 0f),
            new Vector3(0f, 0.5f, 0f),
        };
        var normals = new[] { Vector3.UnitZ };

        return new SingleTriangle(new TriangleMesh(positions, normals));
    }

    /// <summary>A larger, multi-triangle unit cube (12 triangles, one flat normal each), centered at the origin.</summary>
    internal static Cube BuildCube()
    {
        Vector3[] corners =
        {
            new(-1f, -1f, -1f), new(1f, -1f, -1f), new(1f, 1f, -1f), new(-1f, 1f, -1f),
            new(-1f, -1f, 1f), new(1f, -1f, 1f), new(1f, 1f, 1f), new(-1f, 1f, 1f),
        };

        (int A, int B, int C)[] faces =
        {
            (0, 1, 2), (0, 2, 3), // back
            (5, 4, 7), (5, 7, 6), // front
            (4, 0, 3), (4, 3, 7), // left
            (1, 5, 6), (1, 6, 2), // right
            (3, 2, 6), (3, 6, 7), // top
            (4, 5, 1), (4, 1, 0), // bottom
        };

        var positions = new Vector3[faces.Length * 3];
        var normals = new Vector3[faces.Length];

        for (int i = 0; i < faces.Length; i++)
        {
            Vector3 a = corners[faces[i].A];
            Vector3 b = corners[faces[i].B];
            Vector3 c = corners[faces[i].C];

            positions[i * 3] = a;
            positions[i * 3 + 1] = b;
            positions[i * 3 + 2] = c;
            normals[i] = Vector3.Normalize(Vector3.Cross(b - a, c - a));
        }

        return new Cube(new TriangleMesh(positions, normals));
    }
}
