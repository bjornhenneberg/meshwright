using System;
using g3;
using Meshwright.Geometry.Native;

namespace Meshwright.Geometry.Edit;

/// <summary>
/// Boolean operations (union, difference, intersection) on meshes via Manifold C API.
/// Handles conversion between g3.DMesh3 and Manifold's opaque mesh format, wrapping
/// the low-level P/Invoke interop in a type-safe, higher-level API.
/// </summary>
public sealed class BooleanOperations
{
    /// <summary>
    /// Convert a DMesh3 to Manifold API arrays (flattened vertex and triangle data).
    /// </summary>
    /// <param name="mesh">The mesh to convert.</param>
    /// <param name="vertices">Output: flattened array [x0, y0, z0, x1, y1, z1, ...]</param>
    /// <param name="triangles">Output: flattened array [v0, v1, v2, v0, v1, v2, ...]</param>
    private static void DMesh3ToArrays(DMesh3 mesh, out double[] vertices, out ulong[] triangles)
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

    /// <summary>
    /// Convert Manifold result arrays back to a DMesh3.
    /// </summary>
    /// <param name="vertexPositions">Flattened vertex array [x0, y0, z0, ...]</param>
    /// <param name="triangleIndices">Flattened triangle array [v0, v1, v2, ...]</param>
    /// <returns>A new DMesh3 containing the converted mesh data.</returns>
    private static DMesh3 ArraysToDMesh3(double[] vertexPositions, ulong[] triangleIndices)
    {
        var mesh = new DMesh3(true);

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

    /// <summary>
    /// Perform a boolean union of two meshes.
    /// </summary>
    /// <param name="meshA">The primary mesh (typically the loaded mesh in the document).</param>
    /// <param name="meshB">The secondary mesh to union with the primary.</param>
    /// <returns>The resulting union mesh, or null if the operation failed.</returns>
    /// <exception cref="InvalidOperationException">Thrown if Manifold reports an error.</exception>
    public static DMesh3 Union(DMesh3 meshA, DMesh3 meshB)
    {
        return PerformBooleanOperation(meshA, meshB, BooleanOp.Union);
    }

    /// <summary>
    /// Perform a boolean difference of two meshes (A - B).
    /// </summary>
    /// <param name="meshA">The primary mesh (what to keep).</param>
    /// <param name="meshB">The secondary mesh to subtract from the primary.</param>
    /// <returns>The resulting difference mesh, or null if the operation failed.</returns>
    /// <exception cref="InvalidOperationException">Thrown if Manifold reports an error.</exception>
    public static DMesh3 Difference(DMesh3 meshA, DMesh3 meshB)
    {
        return PerformBooleanOperation(meshA, meshB, BooleanOp.Difference);
    }

    /// <summary>
    /// Perform a boolean intersection of two meshes.
    /// </summary>
    /// <param name="meshA">The first mesh.</param>
    /// <param name="meshB">The second mesh.</param>
    /// <returns>The resulting intersection mesh, or null if the operation failed.</returns>
    /// <exception cref="InvalidOperationException">Thrown if Manifold reports an error.</exception>
    public static DMesh3 Intersection(DMesh3 meshA, DMesh3 meshB)
    {
        return PerformBooleanOperation(meshA, meshB, BooleanOp.Intersection);
    }

    private enum BooleanOp
    {
        Union,
        Difference,
        Intersection
    }

    /// <summary>
    /// Helper to perform any of the three boolean operations, delegating to the appropriate
    /// ManifoldInterop method.
    /// </summary>
    private static DMesh3 PerformBooleanOperation(DMesh3 meshA, DMesh3 meshB, BooleanOp op)
    {
        // Convert input meshes to Manifold arrays
        DMesh3ToArrays(meshA, out var vertsA, out var trisA);
        DMesh3ToArrays(meshB, out var vertsB, out var trisB);

        // Create Manifold mesh objects
        var manifoldMeshA = ManifoldInterop.CreateMeshGL64(vertsA, trisA);
        var manifoldMeshB = ManifoldInterop.CreateMeshGL64(vertsB, trisB);

        try
        {
            // Convert to Manifold objects
            var manifoldA = ManifoldInterop.MeshToManifold(manifoldMeshA);
            var manifoldB = ManifoldInterop.MeshToManifold(manifoldMeshB);

            try
            {
                // Perform the boolean operation
                ManifoldManifold result = op switch
                {
                    BooleanOp.Union => ManifoldInterop.Union(manifoldA, manifoldB),
                    BooleanOp.Difference => ManifoldInterop.Difference(manifoldA, manifoldB),
                    BooleanOp.Intersection => ManifoldInterop.Intersection(manifoldA, manifoldB),
                    _ => throw new InvalidOperationException("Unknown boolean operation")
                };

                try
                {
                    // Check for errors
                    var status = ManifoldInterop.GetStatus(result);
                    if (status != ManifoldError.NoError)
                    {
                        throw new InvalidOperationException(
                            $"Manifold boolean operation failed with error: {status}");
                    }

                    // Check if result is empty
                    if (ManifoldInterop.IsEmpty(result))
                    {
                        throw new InvalidOperationException(
                            "Boolean operation resulted in an empty mesh (no overlap or invalid geometry).");
                    }

                    // Extract result mesh
                    var resultMesh = ManifoldInterop.ManifoldToMesh(result);
                    try
                    {
                        ManifoldInterop.ExtractMeshGL64(resultMesh, out var resultVerts, out var resultTris);
                        return ArraysToDMesh3(resultVerts, resultTris);
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
                ManifoldInterop.DeleteManifold(manifoldA);
                ManifoldInterop.DeleteManifold(manifoldB);
            }
        }
        finally
        {
            ManifoldInterop.DeleteMeshGL64(manifoldMeshA);
            ManifoldInterop.DeleteMeshGL64(manifoldMeshB);
        }
    }
}
