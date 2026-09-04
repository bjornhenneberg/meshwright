using g3;

namespace Meshwright.Geometry.Spatial;

/// <summary>
/// Ray-mesh picking on top of the vendored <see cref="DMeshAABBTree3"/> (see
/// <c>Vendor/g3/spatial/DMeshAABBTree.cs</c>). Pure geometry/math, no UI or rendering
/// dependencies, per AGENTS.md's Geometry/Rendering boundary — <c>Meshwright.Rendering</c> and
/// <c>Meshwright.App</c> call in here rather than reimplementing picking themselves.
/// </summary>
public static class MeshRaycaster
{
    /// <summary>
    /// Finds the nearest ray-mesh intersection using a caller-supplied, already-built
    /// <see cref="DMeshAABBTree3"/>. Prefer this overload when picking repeatedly against the same
    /// mesh (e.g. every pointer-move during a drag) so the tree is built once and reused, rather
    /// than the convenience <see cref="Raycast(DMesh3, Ray3d, double)"/> overload.
    /// </summary>
    /// <param name="tree">A tree built (or auto-built) over the mesh to query.</param>
    /// <param name="ray">World-space ray; <see cref="Ray3d.Direction"/> must be normalized (g3's own requirement).</param>
    /// <param name="maxDistance">Optional cutoff distance along the ray.</param>
    /// <returns>The nearest hit, or null if the ray misses the mesh (within <paramref name="maxDistance"/>).</returns>
    public static MeshRayHit? Raycast(DMeshAABBTree3 tree, Ray3d ray, double maxDistance = double.MaxValue)
    {
        int triangleId = tree.FindNearestHitTriangle(ray, maxDistance);
        if (triangleId == DMesh3.InvalidID)
        {
            return null;
        }

        // FindNearestHitTriangle already confirms triangleId is hit by the ray; re-deriving the
        // exact parameter/point here (rather than threading it out of the tree traversal) matches
        // the vendored API's own documented usage ("Use MeshQueries.TriangleIntersection() to get
        // more information", DMeshAABBTree.cs:295).
        IntrRay3Triangle3? intersection = MeshQueries.TriangleIntersection(tree.Mesh, triangleId, ray);
        if (intersection is null || !intersection.IsSimpleIntersection)
        {
            return null;
        }

        Vector3d point = ray.PointAt(intersection.RayParameter);
        Vector3d normal = tree.Mesh.GetTriNormal(triangleId);
        return new MeshRayHit(triangleId, point, intersection.RayParameter, normal);
    }

    /// <summary>
    /// Convenience overload that builds a throwaway <see cref="DMeshAABBTree3"/> for a single
    /// query. Fine for one-off picks (e.g. a single click); for repeated picking against the same
    /// mesh, build a tree once and use <see cref="Raycast(DMeshAABBTree3, Ray3d, double)"/> instead.
    /// </summary>
    public static MeshRayHit? Raycast(DMesh3 mesh, Ray3d ray, double maxDistance = double.MaxValue)
    {
        var tree = new DMeshAABBTree3(mesh, autoBuild: true);
        return Raycast(tree, ray, maxDistance);
    }
}
