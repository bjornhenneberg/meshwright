using System.Numerics;
namespace Meshwright.Tests.Gpu;

/// <summary>Small, hand-built indexed mesh fixtures for GPU regression tests.</summary>
internal static class TriangleMeshFixtures
{
    internal readonly record struct SingleTriangle(g3.DMesh3 Mesh);

    internal readonly record struct Cube(g3.DMesh3 Mesh);

    /// <summary>A single flat triangle facing the default camera, centered near the origin.</summary>
    internal static SingleTriangle BuildSingleTriangle()
    {
        var mesh = new g3.DMesh3();
        int a = mesh.AppendVertex(new g3.Vector3d(-0.5, -0.5, 0));
        int b = mesh.AppendVertex(new g3.Vector3d(0.5, -0.5, 0));
        int c = mesh.AppendVertex(new g3.Vector3d(0, 0.5, 0));
        mesh.AppendTriangle(a, b, c);
        return new SingleTriangle(mesh);
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

        var mesh = new g3.DMesh3();
        var vertexIds = new int[corners.Length];
        for (int corner = 0; corner < corners.Length; corner++)
        {
            vertexIds[corner] = mesh.AppendVertex(new g3.Vector3d(corners[corner].X, corners[corner].Y, corners[corner].Z));
        }

        for (int i = 0; i < faces.Length; i++)
        {
            mesh.AppendTriangle(vertexIds[faces[i].A], vertexIds[faces[i].B], vertexIds[faces[i].C]);
        }

        return new Cube(mesh);
    }
}