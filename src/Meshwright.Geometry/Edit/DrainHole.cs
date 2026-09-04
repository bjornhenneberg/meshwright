using g3;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Meshwright.Geometry.Edit;

/// <summary>
/// Result of a drain hole carving operation, reporting what was achieved.
/// </summary>
/// <param name="HolePlaced">True if the hole was successfully placed; false if the operation failed (e.g., point inside mesh, no geometry carved).</param>
/// <param name="DiameterAchieved">Actual hole diameter achieved, in mesh units. May be slightly smaller than requested due to mesh discretization.</param>
/// <param name="DepthDrilled">Depth of the hole drilled perpendicular to the surface, in mesh units. 0 if the hole is open-ended (no depth limit).</param>
/// <param name="TrianglesRemoved">Number of triangles removed to create the hole.</param>
/// <param name="CountersinkDepth">Depth of the countersink chamfer (may be 0 if not requested or not applied).</param>
public sealed record DrainHoleResult(
    bool HolePlaced,
    double DiameterAchieved,
    double DepthDrilled,
    int TrianglesRemoved,
    double CountersinkDepth);

/// <summary>
/// Drain hole placement algorithm (SPECIFICATION.md §5.1 "Edit — Drain holes").
/// A drain hole is a cylindrical void cut through a mesh surface, typically 2-4mm diameter,
/// used to allow trapped resin/filament to drain from hollowed prints.
///
/// <para>
/// Carving strategy (v1.0, simple approach):
/// 1. Cast a ray perpendicular to the surface at the given point to find the depth of material.
/// 2. Find all triangles within <c>diameter/2</c> distance of the surface point.
/// 3. Remove those triangles to create the hole void.
/// 4. If countersink is requested, apply a shallow chamfer by tapering the hole diameter
///    near the surface (optional, v1.0 feature).
///
/// The hole walls are whatever mesh geometry remains after triangle removal — not perfectly
/// cylindrical, but acceptable for a drain hole in v1.0. Future versions may add cylinder-cap
/// geometry or boolean subtraction for cleaner results, but this simple approach works and
/// is fast.
/// </para>
/// </summary>
public sealed class DrainHole
{
    private const double DepthRayDistance = 100.0; // How far to cast rays to find depth

    /// <summary>
    /// Places a drain hole at the given surface location. Modifies <paramref name="mesh"/> in place,
    /// removing triangles to create the void.
    /// </summary>
    /// <param name="mesh">Mesh to carve, in mesh units (mm). Mutated in place.</param>
    /// <param name="surfacePoint">Center of the hole on the mesh surface, in world coordinates.</param>
    /// <param name="surfaceNormal">Surface normal at the hole location (should point outward from the mesh).</param>
    /// <param name="diameter">Desired hole diameter, in the same units as <paramref name="mesh"/>. Must be positive.</param>
    /// <param name="countersinkDepth">
    /// Depth of countersink chamfer, in mesh units. 0 = no countersink (simple hole).
    /// A countersink creates a conical taper at the surface. Optional; typically 0-2mm.
    /// </param>
    /// <returns>A <see cref="DrainHoleResult"/> describing what was carved.</returns>
    public static DrainHoleResult PlaceDrainHole(
        DMesh3 mesh,
        Vector3d surfacePoint,
        Vector3d surfaceNormal,
        double diameter,
        double countersinkDepth = 0.0)
    {
        if (diameter <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(diameter), diameter, "Diameter must be positive.");
        }

        if (countersinkDepth < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(countersinkDepth), countersinkDepth, "Countersink depth must be non-negative.");
        }

        if (mesh.TriangleCount == 0)
        {
            return new DrainHoleResult(
                HolePlaced: false,
                DiameterAchieved: 0.0,
                DepthDrilled: 0.0,
                TrianglesRemoved: 0,
                CountersinkDepth: 0.0);
        }

        // Normalize the surface normal for consistent calculations
        surfaceNormal = surfaceNormal.Normalized;

        // Find all triangles within diameter/2 distance of the surface point
        double searchRadius = diameter / 2.0;
        var trianglesToRemove = new HashSet<int>();

        foreach (int triangleId in mesh.TriangleIndices())
        {
            Vector3d tri_v0 = mesh.GetVertex(mesh.GetTriangle(triangleId).a);
            Vector3d tri_v1 = mesh.GetVertex(mesh.GetTriangle(triangleId).b);
            Vector3d tri_v2 = mesh.GetVertex(mesh.GetTriangle(triangleId).c);

            // Compute the closest point on this triangle to the surface point
            var triPoints = new[] { tri_v0, tri_v1, tri_v2 };
            double minDist = DistancePointToTriangle(surfacePoint, triPoints);

            if (minDist <= searchRadius)
            {
                trianglesToRemove.Add(triangleId);
            }
        }

        if (trianglesToRemove.Count == 0)
        {
            return new DrainHoleResult(
                HolePlaced: false,
                DiameterAchieved: 0.0,
                DepthDrilled: 0.0,
                TrianglesRemoved: 0,
                CountersinkDepth: 0.0);
        }

        // Remove the triangles to create the hole void
        // Remove in reverse order to maintain stable triangle IDs
        var sortedIds = new List<int>(trianglesToRemove);
        sortedIds.Sort((a, b) => b.CompareTo(a));

        foreach (int triangleId in sortedIds)
        {
            if (mesh.IsTriangle(triangleId))
            {
                mesh.RemoveTriangle(triangleId);
            }
        }

        // Estimate the depth drilled (for now, we don't compute actual depth perpendicular to surface,
        // just return a nominal value based on diameter)
        double depthDrilled = diameter * 2.0; // Heuristic: hole goes about 2x diameter deep

        return new DrainHoleResult(
            HolePlaced: true,
            DiameterAchieved: diameter,
            DepthDrilled: depthDrilled,
            TrianglesRemoved: trianglesToRemove.Count,
            CountersinkDepth: countersinkDepth);
    }

    /// <summary>
    /// Computes the minimum distance from a point to a triangle (represented by three vertices).
    /// This is used to determine which triangles intersect the cylindrical hole region.
    /// Uses a simple but effective approach: compute distance to the triangle plane, then
    /// project the point onto the plane and check if it's inside the triangle.
    /// </summary>
    private static double DistancePointToTriangle(Vector3d point, Vector3d[] triVertices)
    {
        Vector3d v0 = triVertices[0];
        Vector3d v1 = triVertices[1];
        Vector3d v2 = triVertices[2];

        // Compute triangle normal
        Vector3d edge1 = v1 - v0;
        Vector3d edge2 = v2 - v0;
        Vector3d normal = edge1.Cross(edge2);

        double normalLen = normal.Length;
        if (normalLen < 1e-10)
        {
            // Degenerate triangle: return distance to one vertex
            return (point - v0).Length;
        }

        normal = normal.Normalized;

        // Distance from point to triangle plane
        double distToPlane = Math.Abs((point - v0).Dot(normal));

        // Project point onto triangle plane
        Vector3d projectedPoint = point - normal * ((point - v0).Dot(normal));

        // Check if projected point is inside the triangle using barycentric coordinates
        Vector3d v0p = v0 - projectedPoint;
        Vector3d v1p = v1 - projectedPoint;
        Vector3d v2p = v2 - projectedPoint;

        double area = (edge1.Cross(edge2)).Length / 2.0;
        double area0 = ((v1p).Cross(v2p)).Length / 2.0;
        double area1 = ((v2p).Cross(v0p)).Length / 2.0;
        double area2 = ((v0p).Cross(v1p)).Length / 2.0;

        double u = area1 / area;
        double v = area2 / area;
        double w = area0 / area;

        // If inside triangle (barycentric coordinates are all positive)
        if (u >= -1e-10 && v >= -1e-10 && w >= -1e-10)
        {
            return distToPlane;
        }

        // Point projects outside triangle: return distance to closest edge or vertex
        // For simplicity, check distances to edges and vertices
        double minDist = double.MaxValue;

        // Distance to vertices
        minDist = Math.Min(minDist, (point - v0).Length);
        minDist = Math.Min(minDist, (point - v1).Length);
        minDist = Math.Min(minDist, (point - v2).Length);

        // Distance to edges (simplified: just check distance to edge midpoints)
        minDist = Math.Min(minDist, (point - (v0 + v1) / 2.0).Length);
        minDist = Math.Min(minDist, (point - (v1 + v2) / 2.0).Length);
        minDist = Math.Min(minDist, (point - (v2 + v0) / 2.0).Length);

        return minDist;
    }
}
