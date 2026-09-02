using System.Numerics;
namespace Meshwright.Rendering.GL;

/// <summary>
/// Pure buffer-building logic for expanding indexed <see cref="g3.DMesh3"/> meshes for the GPU.
/// Decoupled from any GL calls so it can be unit tested without a GL context.
/// </summary>
public static class VertexDataBuilder
{
    /// <summary>Expands indexed triangle corners into a flat GPU position stream.</summary>
    public static Vector3[] BuildPositions(g3.DMesh3 mesh)
    {
        var positions = new Vector3[mesh.TriangleCount * 3];
        int outputIndex = 0;
        foreach (int triangleId in mesh.TriangleIndices())
        {
            g3.Index3i triangle = mesh.GetTriangle(triangleId);
            foreach (int vertexId in new[] { triangle.a, triangle.b, triangle.c })
            {
                g3.Vector3d vertex = mesh.GetVertex(vertexId);
                positions[outputIndex++] = new Vector3((float)vertex.x, (float)vertex.y, (float)vertex.z);
            }
        }

        return positions;
    }

    /// <summary>Expands per-triangle normals to one entry per vertex (flat shading via duplication).</summary>
    public static Vector3[] BuildPerVertexNormals(g3.DMesh3 mesh)
    {
        var normals = new Vector3[mesh.TriangleCount * 3];
        int outputIndex = 0;
        foreach (int triangleId in mesh.TriangleIndices())
        {
            g3.Vector3d normal = mesh.GetTriNormal(triangleId);
            Vector3 gpuNormal = new((float)normal.x, (float)normal.y, (float)normal.z);
            normals[outputIndex++] = gpuNormal;
            normals[outputIndex++] = gpuNormal;
            normals[outputIndex++] = gpuNormal;
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

    /// <summary>
    /// Builds a per-vertex-corner highlight flag (1.0 flagged / 0.0 not), one entry per triangle
    /// corner in the same order as <see cref="BuildPositions"/>, so a triangle in
    /// <paramref name="flaggedTriangleIds"/> highlights across all three of its corners.
    /// </summary>
    public static float[] BuildTriangleHighlightFlags(g3.DMesh3 mesh, IReadOnlyCollection<int> flaggedTriangleIds)
    {
        var flags = new float[mesh.TriangleCount * 3];
        int outputIndex = 0;
        foreach (int triangleId in mesh.TriangleIndices())
        {
            float flag = flaggedTriangleIds.Contains(triangleId) ? 1f : 0f;
            flags[outputIndex++] = flag;
            flags[outputIndex++] = flag;
            flags[outputIndex++] = flag;
        }

        return flags;
    }

    /// <summary>Expands flagged vertex-pair edges into a flat GPU line-list position stream (two positions per edge).</summary>
    public static Vector3[] BuildEdgeLinePositions(g3.DMesh3 mesh, IReadOnlyList<g3.Index2i> flaggedEdges)
    {
        var positions = new Vector3[flaggedEdges.Count * 2];
        int outputIndex = 0;
        foreach (g3.Index2i edge in flaggedEdges)
        {
            g3.Vector3d a = mesh.GetVertex(edge.a);
            g3.Vector3d b = mesh.GetVertex(edge.b);
            positions[outputIndex++] = new Vector3((float)a.x, (float)a.y, (float)a.z);
            positions[outputIndex++] = new Vector3((float)b.x, (float)b.y, (float)b.z);
        }

        return positions;
    }
}
