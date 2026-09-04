// ManifoldInteropTests.cs — End-to-end test of Manifold C API interop layer.
// Builds two overlapping cubes, performs a union boolean operation through P/Invoke,
// and verifies the result is a valid, non-empty mesh with plausible volume.

using g3;
using Meshwright.Geometry.Native;
using Xunit;

namespace Meshwright.Tests.Native;

public class ManifoldInteropTests
{
    /// Create a simple cube mesh as a g3.DMesh3.
    private static DMesh3 CreateCubeMesh(double size)
    {
        var mesh = new DMesh3(true);

        // Define a 1x1x1 cube (scale by size)
        var s = size / 2.0;

        // Vertices: 8 corners of a cube
        var v0 = mesh.AppendVertex(new Vector3d(-s, -s, -s));
        var v1 = mesh.AppendVertex(new Vector3d(s, -s, -s));
        var v2 = mesh.AppendVertex(new Vector3d(s, s, -s));
        var v3 = mesh.AppendVertex(new Vector3d(-s, s, -s));
        var v4 = mesh.AppendVertex(new Vector3d(-s, -s, s));
        var v5 = mesh.AppendVertex(new Vector3d(s, -s, s));
        var v6 = mesh.AppendVertex(new Vector3d(s, s, s));
        var v7 = mesh.AppendVertex(new Vector3d(-s, s, s));

        // Triangles: 12 triangles (2 per face)
        // Bottom face (z = -s)
        mesh.AppendTriangle(v0, v2, v1);
        mesh.AppendTriangle(v0, v3, v2);

        // Top face (z = s)
        mesh.AppendTriangle(v4, v5, v6);
        mesh.AppendTriangle(v4, v6, v7);

        // Front face (y = -s)
        mesh.AppendTriangle(v0, v1, v5);
        mesh.AppendTriangle(v0, v5, v4);

        // Back face (y = s)
        mesh.AppendTriangle(v2, v3, v7);
        mesh.AppendTriangle(v2, v7, v6);

        // Left face (x = -s)
        mesh.AppendTriangle(v0, v7, v3);
        mesh.AppendTriangle(v0, v4, v7);

        // Right face (x = s)
        mesh.AppendTriangle(v1, v6, v5);
        mesh.AppendTriangle(v1, v2, v6);

        return mesh;
    }

    /// Convert a DMesh3 to arrays suitable for Manifold's C API.
    private static void DMesh3ToArrays(
        DMesh3 mesh,
        out double[] vertices,
        out ulong[] triangles)
    {
        var verts = new double[mesh.VertexCount * 3];
        int vIdx = 0;
        foreach (int vid in mesh.VertexIndices())
        {
            var pos = mesh.GetVertex(vid);
            verts[vIdx++] = pos.x;
            verts[vIdx++] = pos.y;
            verts[vIdx++] = pos.z;
        }

        var tris = new ulong[mesh.TriangleCount * 3];
        int tIdx = 0;
        foreach (int tid in mesh.TriangleIndices())
        {
            var tri = mesh.GetTriangle(tid);
            tris[tIdx++] = (ulong)tri.a;
            tris[tIdx++] = (ulong)tri.b;
            tris[tIdx++] = (ulong)tri.c;
        }

        vertices = verts;
        triangles = tris;
    }

    /// Convert Manifold result back to DMesh3.
    private static DMesh3 ArraysToDMesh3(double[] vertexPositions, ulong[] triangleIndices)
    {
        var mesh = new DMesh3(true);

        // Assume 3 doubles per vertex
        var nVerts = vertexPositions.Length / 3;
        var vertMap = new int[nVerts];

        for (int i = 0; i < nVerts; i++)
        {
            var pos = new Vector3d(
                vertexPositions[i * 3],
                vertexPositions[i * 3 + 1],
                vertexPositions[i * 3 + 2]);
            vertMap[i] = mesh.AppendVertex(pos);
        }

        // Assume 3 ulong indices per triangle
        var nTris = triangleIndices.Length / 3;
        for (int i = 0; i < nTris; i++)
        {
            var v0 = (int)triangleIndices[i * 3];
            var v1 = (int)triangleIndices[i * 3 + 1];
            var v2 = (int)triangleIndices[i * 3 + 2];
            mesh.AppendTriangle(vertMap[v0], vertMap[v1], vertMap[v2]);
        }

        return mesh;
    }

    [Fact]
    public void TestManifoldUnionBooleanInterop()
    {
        // Create two unit cubes
        var cube1Mesh = CreateCubeMesh(1.0);
        var cube2Mesh = CreateCubeMesh(1.0);

        // Translate one cube so they overlap
        var offset = new Vector3d(0.5, 0.5, 0.0);
        foreach (int vid in cube2Mesh.VertexIndices())
        {
            var pos = cube2Mesh.GetVertex(vid);
            cube2Mesh.SetVertex(vid, pos + offset);
        }

        // Convert to arrays
        DMesh3ToArrays(cube1Mesh, out var verts1, out var tris1);
        DMesh3ToArrays(cube2Mesh, out var verts2, out var tris2);

        // Create Manifold meshes
        var mesh1 = ManifoldInterop.CreateMeshGL64(verts1, tris1);
        var mesh2 = ManifoldInterop.CreateMeshGL64(verts2, tris2);

        try
        {
            // Convert to Manifold objects
            var manifold1 = ManifoldInterop.MeshToManifold(mesh1);
            var manifold2 = ManifoldInterop.MeshToManifold(mesh2);

            try
            {
                // Perform union
                var result = ManifoldInterop.Union(manifold1, manifold2);

                try
                {
                    // Verify the result is valid
                    var status = ManifoldInterop.GetStatus(result);
                    Assert.Equal(ManifoldError.NoError, status);

                    // Result should not be empty
                    Assert.False(ManifoldInterop.IsEmpty(result));

                    // Get triangle/vertex counts
                    var resultVertCount = ManifoldInterop.GetVertexCount(result);
                    var resultTriCount = ManifoldInterop.GetTriangleCount(result);

                    Assert.True(resultVertCount > 0, "Result should have vertices");
                    Assert.True(resultTriCount > 0, "Result should have triangles");

                    // Extract mesh data
                    var resultMesh = ManifoldInterop.ManifoldToMesh(result);
                    try
                    {
                        ManifoldInterop.ExtractMeshGL64(resultMesh, out var resultVerts, out var resultTris);

                        // Verify geometry: union of two unit cubes should have volume roughly 1.875
                        // (two 1x1x1 cubes minus their 0.5x0.5x0.5 overlap)
                        var volume = ManifoldInterop.GetVolume(result);
                        Assert.True(volume > 1.5 && volume < 2.0,
                            $"Expected union volume ~1.875, got {volume}");

                        // Convert back to DMesh3 and verify it's valid
                        var finalMesh = ArraysToDMesh3(resultVerts, resultTris);
                        Assert.True(finalMesh.TriangleCount > 0);
                    }
                    finally
                    {
                        ManifoldInterop.DeleteMeshGL64(resultMesh);
                    }
                }
                finally
                {
                    ManifoldInterop.DeleteManifold(result);
                }
            }
            finally
            {
                ManifoldInterop.DeleteManifold(manifold1);
                ManifoldInterop.DeleteManifold(manifold2);
            }
        }
        finally
        {
            ManifoldInterop.DeleteMeshGL64(mesh1);
            ManifoldInterop.DeleteMeshGL64(mesh2);
        }
    }

    [Fact]
    public void TestManifoldDifferenceBoolean()
    {
        // Create two unit cubes
        var cube1Mesh = CreateCubeMesh(1.0);
        var cube2Mesh = CreateCubeMesh(0.5);

        // Translate the smaller cube to overlap with the larger one
        var offset = new Vector3d(0.25, 0.25, 0.0);
        foreach (int vid in cube2Mesh.VertexIndices())
        {
            var pos = cube2Mesh.GetVertex(vid);
            cube2Mesh.SetVertex(vid, pos + offset);
        }

        DMesh3ToArrays(cube1Mesh, out var verts1, out var tris1);
        DMesh3ToArrays(cube2Mesh, out var verts2, out var tris2);

        var mesh1 = ManifoldInterop.CreateMeshGL64(verts1, tris1);
        var mesh2 = ManifoldInterop.CreateMeshGL64(verts2, tris2);

        try
        {
            var manifold1 = ManifoldInterop.MeshToManifold(mesh1);
            var manifold2 = ManifoldInterop.MeshToManifold(mesh2);

            try
            {
                // Perform difference
                var result = ManifoldInterop.Difference(manifold1, manifold2);

                try
                {
                    var status = ManifoldInterop.GetStatus(result);
                    Assert.Equal(ManifoldError.NoError, status);

                    Assert.False(ManifoldInterop.IsEmpty(result));

                    var resultTriCount = ManifoldInterop.GetTriangleCount(result);
                    Assert.True(resultTriCount > 0);

                    // Volume should be less than the first cube's volume (1.0)
                    var volume = ManifoldInterop.GetVolume(result);
                    Assert.True(volume < 1.0 && volume > 0.0,
                        $"Expected difference volume < 1.0, got {volume}");
                }
                finally
                {
                    ManifoldInterop.DeleteManifold(result);
                }
            }
            finally
            {
                ManifoldInterop.DeleteManifold(manifold1);
                ManifoldInterop.DeleteManifold(manifold2);
            }
        }
        finally
        {
            ManifoldInterop.DeleteMeshGL64(mesh1);
            ManifoldInterop.DeleteMeshGL64(mesh2);
        }
    }

    [Fact]
    public void TestManifoldIntersectionBoolean()
    {
        // Create two unit cubes
        var cube1Mesh = CreateCubeMesh(1.0);
        var cube2Mesh = CreateCubeMesh(1.0);

        // Translate one cube so they partially overlap
        var offset = new Vector3d(0.5, 0.0, 0.0);
        foreach (int vid in cube2Mesh.VertexIndices())
        {
            var pos = cube2Mesh.GetVertex(vid);
            cube2Mesh.SetVertex(vid, pos + offset);
        }

        DMesh3ToArrays(cube1Mesh, out var verts1, out var tris1);
        DMesh3ToArrays(cube2Mesh, out var verts2, out var tris2);

        var mesh1 = ManifoldInterop.CreateMeshGL64(verts1, tris1);
        var mesh2 = ManifoldInterop.CreateMeshGL64(verts2, tris2);

        try
        {
            var manifold1 = ManifoldInterop.MeshToManifold(mesh1);
            var manifold2 = ManifoldInterop.MeshToManifold(mesh2);

            try
            {
                // Perform intersection
                var result = ManifoldInterop.Intersection(manifold1, manifold2);

                try
                {
                    var status = ManifoldInterop.GetStatus(result);
                    Assert.Equal(ManifoldError.NoError, status);

                    Assert.False(ManifoldInterop.IsEmpty(result));

                    var resultTriCount = ManifoldInterop.GetTriangleCount(result);
                    Assert.True(resultTriCount > 0);

                    // Intersection volume should be less than either cube's volume
                    var volume = ManifoldInterop.GetVolume(result);
                    Assert.True(volume > 0.0 && volume < 1.0,
                        $"Expected intersection volume between 0 and 1, got {volume}");
                }
                finally
                {
                    ManifoldInterop.DeleteManifold(result);
                }
            }
            finally
            {
                ManifoldInterop.DeleteManifold(manifold1);
                ManifoldInterop.DeleteManifold(manifold2);
            }
        }
        finally
        {
            ManifoldInterop.DeleteMeshGL64(mesh1);
            ManifoldInterop.DeleteMeshGL64(mesh2);
        }
    }
}
