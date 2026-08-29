using System.Numerics;

namespace Meshwright.Geometry;

/// <summary>
/// Minimal M0 stopgap mesh representation: a flat, non-indexed triangle soup with
/// one normal per triangle. Superseded by a DMesh3-based structure in a later milestone.
/// </summary>
public sealed class TriangleMesh
{
    public TriangleMesh(Vector3[] positions, Vector3[] normals)
    {
        if (positions.Length % 3 != 0)
        {
            throw new ArgumentException("Positions length must be a multiple of 3.", nameof(positions));
        }

        if (normals.Length != positions.Length / 3)
        {
            throw new ArgumentException("Normals length must equal the triangle count.", nameof(normals));
        }

        Positions = positions;
        Normals = normals;
    }

    /// <summary>Flat vertex positions, length 3 * TriangleCount, not deduplicated.</summary>
    public Vector3[] Positions { get; }

    /// <summary>One normal per triangle.</summary>
    public Vector3[] Normals { get; }

    public int TriangleCount => Normals.Length;
}
