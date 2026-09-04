using System.Numerics;
using g3;

namespace Meshwright.Geometry.Edit;

/// <summary>
/// Mesh transformation algorithms: translation, rotation, scaling, mirroring, and alignment.
/// All operations work on DMesh3 and return a transformed copy (for Preview) or mutate in place (for Apply).
/// </summary>
public static class Transform
{
    /// <summary>
    /// Apply a 4×4 affine transformation matrix to all vertices in the mesh.
    /// Throws if the matrix is singular (determinant ≈ 0).
    /// </summary>
    public static DMesh3 TransformMesh(DMesh3 mesh, Matrix4x4 xform)
    {
        if (Math.Abs(xform.GetDeterminant()) < 1e-10)
        {
            throw new ArgumentException("Transformation matrix is singular (determinant ≈ 0)");
        }

        var result = new DMesh3(mesh, bCompact: false);
        foreach (int vid in result.VertexIndices())
        {
            var v = result.GetVertex(vid);
            var v4 = new Vector4((float)v.x, (float)v.y, (float)v.z, 1f);
            var transformed = Vector4.Transform(v4, xform);

            if (Math.Abs(transformed.W) > 1e-10)
            {
                result.SetVertex(vid, new Vector3d(
                    transformed.X / transformed.W,
                    transformed.Y / transformed.W,
                    transformed.Z / transformed.W));
            }
        }

        // Update normals to match the transformation
        // Recompute them from face geometry
        var norms = new MeshNormals(result);
        norms.Compute();
        norms.CopyTo(result);

        return result;
    }

    /// <summary>
    /// Translate the mesh by an offset vector, mutating it in place.
    /// </summary>
    public static void TranslateMesh(DMesh3 mesh, Vector3d offset)
    {
        foreach (int vid in mesh.VertexIndices())
        {
            var v = mesh.GetVertex(vid);
            mesh.SetVertex(vid, v + offset);
        }
    }

    /// <summary>
    /// Rotate the mesh around an axis through a center point by a given angle (in degrees), mutating it in place.
    /// </summary>
    public static void RotateMesh(DMesh3 mesh, double angleDegrees, Vector3d axis, Vector3d center)
    {
        axis.Normalize();
        double angleRad = angleDegrees * Math.PI / 180.0;

        foreach (int vid in mesh.VertexIndices())
        {
            var v = mesh.GetVertex(vid);
            var rel = v - center;

            // Use Rodrigues' rotation formula
            double cos = Math.Cos(angleRad);
            double sin = Math.Sin(angleRad);
            double oneMinusCos = 1.0 - cos;

            // k · (k · rel) * (1 - cos)
            double dotProduct = axis.Dot(rel);
            var parallel = axis * (dotProduct * oneMinusCos);

            // cos * rel
            var cosComponent = rel * cos;

            // sin * (k × rel)
            var crossProduct = axis.Cross(rel);
            var sinComponent = crossProduct * sin;

            var rotated = parallel + cosComponent + sinComponent;
            mesh.SetVertex(vid, center + rotated);
        }

        // Update normals
        var norms = new MeshNormals(mesh);
        norms.Compute();
        norms.CopyTo(mesh);
    }

    /// <summary>
    /// Scale the mesh isotropically around a center point, mutating it in place.
    /// </summary>
    public static void ScaleMesh(DMesh3 mesh, double scale, Vector3d center)
    {
        if (Math.Abs(scale) < 1e-10)
        {
            throw new ArgumentException("Scale factor must be non-zero");
        }

        foreach (int vid in mesh.VertexIndices())
        {
            var v = mesh.GetVertex(vid);
            var rel = v - center;
            var scaled = rel * scale;
            mesh.SetVertex(vid, center + scaled);
        }

        // Normals don't change direction with isotropic scaling, but do need to be recomputed
        // if scale is negative (flips normals)
        var norms = new MeshNormals(mesh);
        norms.Compute();
        norms.CopyTo(mesh);
    }

    /// <summary>
    /// Mirror (reflect) the mesh across a plane defined by a point and normal, mutating it in place.
    /// </summary>
    public static void MirrorMesh(DMesh3 mesh, Vector3d planePoint, Vector3d planeNormal)
    {
        planeNormal.Normalize();

        foreach (int vid in mesh.VertexIndices())
        {
            var v = mesh.GetVertex(vid);
            var toPoint = v - planePoint;

            // Project onto plane normal and flip
            double dist = toPoint.Dot(planeNormal);
            var reflected = v - 2.0 * dist * planeNormal;

            mesh.SetVertex(vid, reflected);
        }

        // Mirror reverses the winding order, so we need to flip normals
        foreach (int tid in mesh.TriangleIndices())
        {
            var tri = mesh.GetTriangle(tid);
            // Reverse the winding: swap v1 and v2
            mesh.SetTriangle(tid, new Index3i(tri.a, tri.c, tri.b));
        }

        // Recompute normals
        var norms = new MeshNormals(mesh);
        norms.Compute();
        norms.CopyTo(mesh);
    }

    /// <summary>
    /// Align the mesh to the build bed by setting its lowest Z-coordinate to Z=0, mutating it in place.
    /// </summary>
    public static void AlignToBed(DMesh3 mesh)
    {
        var bounds = mesh.CachedBounds;
        double minZ = bounds.Min.z;

        if (Math.Abs(minZ) < 1e-10)
        {
            // Already at Z=0
            return;
        }

        double offset = -minZ;
        foreach (int vid in mesh.VertexIndices())
        {
            var v = mesh.GetVertex(vid);
            mesh.SetVertex(vid, new Vector3d(v.x, v.y, v.z + offset));
        }
    }

    /// <summary>
    /// Alias for AlignToBed; drops the mesh so its lowest point touches Z=0.
    /// </summary>
    public static void DropToZ0(DMesh3 mesh)
    {
        AlignToBed(mesh);
    }
}
