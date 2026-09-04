using g3;

namespace Meshwright.Geometry.Spatial;

/// <summary>Result of a successful <see cref="MeshRaycaster"/> ray-mesh intersection.</summary>
/// <param name="TriangleId">Id of the hit triangle (into the source <see cref="DMesh3"/>).</param>
/// <param name="Point">World-space hit point.</param>
/// <param name="Distance">Distance from the ray origin to <see cref="Point"/>, along the ray direction.</param>
/// <param name="Normal">Face normal of the hit triangle at <see cref="Point"/>.</param>
public readonly record struct MeshRayHit(int TriangleId, Vector3d Point, double Distance, Vector3d Normal);
