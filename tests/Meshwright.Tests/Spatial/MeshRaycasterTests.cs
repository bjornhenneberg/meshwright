using g3;
using Meshwright.Geometry.Spatial;
using Xunit;

namespace Meshwright.Tests.Spatial;

/// <summary>
/// Unit tests for ray-mesh picking via <see cref="MeshRaycaster"/>. Uses deterministic hand-built
/// meshes (single triangle, cube) and rays with known geometric properties to verify hit detection,
/// distance calculation, and normal computation.
/// </summary>
public class MeshRaycasterTests
{
    [Fact]
    public void Raycast_HitsTriangleCenteredAtOrigin()
    {
        // Single flat triangle in the xy-plane, centered at origin, facing +z.
        var mesh = new DMesh3();
        int a = mesh.AppendVertex(new Vector3d(-1, -1, 0));
        int b = mesh.AppendVertex(new Vector3d(1, -1, 0));
        int c = mesh.AppendVertex(new Vector3d(0, 1, 0));
        int tid = mesh.AppendTriangle(a, b, c);

        // Ray straight down the z-axis toward the triangle.
        var ray = new Ray3d(new Vector3d(0, 0, 10), new Vector3d(0, 0, -1), bIsNormalized: true);

        MeshRayHit? hit = MeshRaycaster.Raycast(mesh, ray);

        Assert.NotNull(hit);
        Assert.Equal(tid, hit.Value.TriangleId);
        Assert.True(hit.Value.Distance > 0, "Hit distance should be positive");
        Assert.True(hit.Value.Point.z > -1 && hit.Value.Point.z < 1, "Hit point should be at/near the triangle plane");
    }

    [Fact]
    public void Raycast_MissesTriangleBehindRayOrigin()
    {
        var mesh = new DMesh3();
        int a = mesh.AppendVertex(new Vector3d(-1, -1, 0));
        int b = mesh.AppendVertex(new Vector3d(1, -1, 0));
        int c = mesh.AppendVertex(new Vector3d(0, 1, 0));
        mesh.AppendTriangle(a, b, c);

        // Ray pointing away from the triangle.
        var ray = new Ray3d(new Vector3d(0, 0, 10), new Vector3d(0, 0, 1), bIsNormalized: true);

        MeshRayHit? hit = MeshRaycaster.Raycast(mesh, ray);

        Assert.Null(hit);
    }

    [Fact]
    public void Raycast_MissesTriangleOutsideBounds()
    {
        var mesh = new DMesh3();
        int a = mesh.AppendVertex(new Vector3d(-1, -1, 0));
        int b = mesh.AppendVertex(new Vector3d(1, -1, 0));
        int c = mesh.AppendVertex(new Vector3d(0, 1, 0));
        mesh.AppendTriangle(a, b, c);

        // Ray far to the side, missing the triangle.
        var ray = new Ray3d(new Vector3d(100, 100, 10), new Vector3d(0, 0, -1), bIsNormalized: true);

        MeshRayHit? hit = MeshRaycaster.Raycast(mesh, ray);

        Assert.Null(hit);
    }

    [Fact]
    public void Raycast_WithMaxDistance_RespectsCutoff()
    {
        var mesh = new DMesh3();
        int a = mesh.AppendVertex(new Vector3d(-1, -1, 0));
        int b = mesh.AppendVertex(new Vector3d(1, -1, 0));
        int c = mesh.AppendVertex(new Vector3d(0, 1, 0));
        mesh.AppendTriangle(a, b, c);

        var ray = new Ray3d(new Vector3d(0, 0, 10), new Vector3d(0, 0, -1), bIsNormalized: true);

        // Triangle is at z=0, ray starts at z=10, so distance is ~10. Set max to 5 (should miss).
        MeshRayHit? hit = MeshRaycaster.Raycast(mesh, ray, maxDistance: 5);

        Assert.Null(hit);
    }

    [Fact]
    public void Raycast_ReturnsTriangleNormal()
    {
        // Triangle in xy-plane, should have normal pointing in +z or -z direction.
        var mesh = new DMesh3();
        int a = mesh.AppendVertex(new Vector3d(-1, -1, 0));
        int b = mesh.AppendVertex(new Vector3d(1, -1, 0));
        int c = mesh.AppendVertex(new Vector3d(0, 1, 0));
        mesh.AppendTriangle(a, b, c);

        var ray = new Ray3d(new Vector3d(0, 0, 10), new Vector3d(0, 0, -1), bIsNormalized: true);

        MeshRayHit? hit = MeshRaycaster.Raycast(mesh, ray);

        Assert.NotNull(hit);
        // Normal should be roughly perpendicular to the xy-plane.
        Assert.True(Math.Abs(hit.Value.Normal.z) > 0.9, "Triangle normal z-component should be close to ±1");
    }

    [Fact]
    public void Raycast_PicksNearestTriangle_IgnoresFarther()
    {
        // Two triangles, one in front of the other along a ray.
        var mesh = new DMesh3();

        // Triangle at z=0 (nearer).
        int a1 = mesh.AppendVertex(new Vector3d(-1, -1, 0));
        int b1 = mesh.AppendVertex(new Vector3d(1, -1, 0));
        int c1 = mesh.AppendVertex(new Vector3d(0, 1, 0));
        int tid1 = mesh.AppendTriangle(a1, b1, c1);

        // Triangle at z=-10 (farther).
        int a2 = mesh.AppendVertex(new Vector3d(-1, -1, -10));
        int b2 = mesh.AppendVertex(new Vector3d(1, -1, -10));
        int c2 = mesh.AppendVertex(new Vector3d(0, 1, -10));
        int tid2 = mesh.AppendTriangle(a2, b2, c2);

        var ray = new Ray3d(new Vector3d(0, 0, 10), new Vector3d(0, 0, -1), bIsNormalized: true);

        MeshRayHit? hit = MeshRaycaster.Raycast(mesh, ray);

        Assert.NotNull(hit);
        Assert.Equal(tid1, hit.Value.TriangleId);
    }

    [Fact]
    public void Raycast_WithTree_MatchesConvenienceOverload()
    {
        var mesh = new DMesh3();
        int a = mesh.AppendVertex(new Vector3d(-1, -1, 0));
        int b = mesh.AppendVertex(new Vector3d(1, -1, 0));
        int c = mesh.AppendVertex(new Vector3d(0, 1, 0));
        mesh.AppendTriangle(a, b, c);

        var ray = new Ray3d(new Vector3d(0, 0, 10), new Vector3d(0, 0, -1), bIsNormalized: true);

        // Convenience overload (builds tree internally).
        MeshRayHit? convenienceHit = MeshRaycaster.Raycast(mesh, ray);

        // Manual tree building.
        var tree = new DMeshAABBTree3(mesh, autoBuild: true);
        MeshRayHit? treeHit = MeshRaycaster.Raycast(tree, ray);

        Assert.NotNull(convenienceHit);
        Assert.NotNull(treeHit);
        Assert.Equal(convenienceHit.Value.TriangleId, treeHit.Value.TriangleId);
        Assert.Equal(convenienceHit.Value.Distance, treeHit.Value.Distance, precision: 5);
    }
}
