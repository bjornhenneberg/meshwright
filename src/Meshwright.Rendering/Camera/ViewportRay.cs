using System.Numerics;

namespace Meshwright.Rendering.Camera;

/// <summary>
/// A world-space ray produced by unprojecting a viewport pixel through a camera's view/projection
/// matrices (see <see cref="ViewportRaycaster.Unproject"/>). Used both for mesh picking
/// (<c>Meshwright.Geometry.Spatial.MeshRaycaster</c>) and for gizmo drag math
/// (<c>Meshwright.Rendering.Gizmos</c>).
/// </summary>
/// <param name="Origin">World-space ray origin (on the camera's near plane).</param>
/// <param name="Direction">Normalized world-space ray direction.</param>
public readonly record struct ViewportRay(Vector3 Origin, Vector3 Direction)
{
    /// <summary>World-space point at distance <paramref name="distance"/> along the ray.</summary>
    public Vector3 PointAt(float distance) => Origin + distance * Direction;

    /// <summary>
    /// Converts to a double-precision g3 ray for consumption by
    /// <c>Meshwright.Geometry.Spatial.MeshRaycaster</c>, which operates on <see cref="g3.DMesh3"/>
    /// in g3Sharp's double-precision math types. Rendering is allowed to depend on Geometry types
    /// (see AGENTS.md §6.3 architecture) so this conversion lives on the Rendering-side type.
    /// </summary>
    /// <remarks>
    /// <see cref="Direction"/> is normalized only to float precision, which leaves |d|² off by up
    /// to ~1e-7 — far outside the 1e-8 <c>MathUtil.ZeroTolerance</c> that g3's
    /// <c>Vector3d.IsNormalized</c> (and hence <c>DMeshAABBTree3.FindNearestHitTriangle</c>) demands.
    /// So we deliberately let the <see cref="g3.Ray3d"/> constructor re-normalize in double rather
    /// than asserting <c>bIsNormalized: true</c>; g3's own float ray constructor does the same
    /// ("float cast may not be normalized in double, is trouble in algorithms!", Ray3.cs:51).
    /// </remarks>
    public g3.Ray3d ToRay3d() => new(
        new g3.Vector3d(Origin.X, Origin.Y, Origin.Z),
        new g3.Vector3d(Direction.X, Direction.Y, Direction.Z));
}
