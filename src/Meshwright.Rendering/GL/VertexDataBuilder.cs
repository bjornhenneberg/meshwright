using System.Numerics;
using Meshwright.Geometry;

namespace Meshwright.Rendering.GL;

/// <summary>
/// Pure buffer-building logic for uploading a <see cref="TriangleMesh"/> to the GPU.
/// Decoupled from any GL calls so it can be unit tested without a GL context.
/// </summary>
public static class VertexDataBuilder
{
    /// <summary>Flat vertex positions, unchanged from <see cref="TriangleMesh.Positions"/> (already 3 per triangle).</summary>
    public static Vector3[] BuildPositions(TriangleMesh mesh)
    {
        return (Vector3[])mesh.Positions.Clone();
    }

    /// <summary>Expands per-triangle normals to one entry per vertex (flat shading via duplication).</summary>
    public static Vector3[] BuildPerVertexNormals(TriangleMesh mesh)
    {
        var normals = new Vector3[mesh.Positions.Length];
        for (int triangle = 0; triangle < mesh.TriangleCount; triangle++)
        {
            Vector3 normal = mesh.Normals[triangle];
            int baseIndex = triangle * 3;
            normals[baseIndex] = normal;
            normals[baseIndex + 1] = normal;
            normals[baseIndex + 2] = normal;
        }

        return normals;
    }

    /// <summary>Flattens a <see cref="Vector3"/> array into an interleaved-free contiguous float array (x0,y0,z0,x1,y1,z1,...).</summary>
    public static float[] Flatten(Vector3[] vectors)
    {
        var result = new float[vectors.Length * 3];
        for (int i = 0; i < vectors.Length; i++)
        {
            result[i * 3] = vectors[i].X;
            result[i * 3 + 1] = vectors[i].Y;
            result[i * 3 + 2] = vectors[i].Z;
        }

        return result;
    }
}
