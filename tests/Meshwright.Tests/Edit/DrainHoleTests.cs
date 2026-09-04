using System;
using Xunit;
using g3;
using Meshwright.Core.Operations;
using Meshwright.Geometry.Edit;

namespace Meshwright.Tests.Edit;

/// <summary>
/// Tests for drain hole placement (§5.1 "Edit — Drain holes").
/// Verifies that holes are carved correctly, diameter is within tolerance, and edge cases are handled.
/// </summary>
public class DrainHoleTests
{
    /// <summary>
    /// Creates a simple cube mesh (1 unit on each side) for testing.
    /// Vertices at (0,0,0), (1,0,0), (1,1,0), (0,1,0), (0,0,1), (1,0,1), (1,1,1), (0,1,1).
    /// </summary>
    private static DMesh3 CreateSimpleCube()
    {
        var mesh = new DMesh3(MeshComponents.VertexNormals);

        // Add vertices
        int v0 = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int v1 = mesh.AppendVertex(new Vector3d(1, 0, 0));
        int v2 = mesh.AppendVertex(new Vector3d(1, 1, 0));
        int v3 = mesh.AppendVertex(new Vector3d(0, 1, 0));
        int v4 = mesh.AppendVertex(new Vector3d(0, 0, 1));
        int v5 = mesh.AppendVertex(new Vector3d(1, 0, 1));
        int v6 = mesh.AppendVertex(new Vector3d(1, 1, 1));
        int v7 = mesh.AppendVertex(new Vector3d(0, 1, 1));

        // Add triangles (2 per face)
        // Bottom (z=0)
        mesh.AppendTriangle(v0, v1, v2);
        mesh.AppendTriangle(v0, v2, v3);

        // Top (z=1)
        mesh.AppendTriangle(v4, v6, v5);
        mesh.AppendTriangle(v4, v7, v6);

        // Front (y=0)
        mesh.AppendTriangle(v0, v5, v1);
        mesh.AppendTriangle(v0, v4, v5);

        // Back (y=1)
        mesh.AppendTriangle(v2, v6, v7);
        mesh.AppendTriangle(v2, v7, v3);

        // Left (x=0)
        mesh.AppendTriangle(v0, v3, v7);
        mesh.AppendTriangle(v0, v7, v4);

        // Right (x=1)
        mesh.AppendTriangle(v1, v5, v6);
        mesh.AppendTriangle(v1, v6, v2);

        return mesh;
    }

    /// <summary>
    /// Creates a simple sphere mesh for testing (using marching cubes on an implicit sphere).
    /// </summary>
    private static DMesh3 CreateSimpleSphere(double radius = 1.0, int resolution = 16)
    {
        var mc = new MarchingCubes
        {
            Implicit = new ImplicitSphere { Radius = radius },
            CubeSize = 2.0 * radius / resolution,
            Bounds = new AxisAlignedBox3d(new Vector3d(-radius, -radius, -radius), new Vector3d(radius, radius, radius)),
            IsoValue = 0.0
        };

        mc.Generate();
        return mc.Mesh;
    }

    /// <summary>
    /// Test: placing a drain hole in a cube removes triangles.
    /// </summary>
    [Fact]
    public void PlaceDrainHole_InCube_RemovesTriangles()
    {
        var mesh = CreateSimpleCube();
        int trianglesBefore = mesh.TriangleCount;

        var surfacePoint = new Vector3d(0.5, 0.5, 0.0); // Center of bottom face
        var surfaceNormal = new Vector3d(0, 0, -1); // Pointing down
        double diameter = 0.3;

        var result = DrainHole.PlaceDrainHole(mesh, surfacePoint, surfaceNormal, diameter);

        Assert.True(result.HolePlaced, "Hole should be placed successfully.");
        Assert.True(mesh.TriangleCount < trianglesBefore, "Some triangles should be removed.");
        Assert.True(result.TrianglesRemoved > 0, "Result should report triangles removed.");
    }

    /// <summary>
    /// Test: diameter parameter is respected within reasonable tolerance.
    /// </summary>
    [Fact]
    public void PlaceDrainHole_DiameterMatchesRequest()
    {
        var mesh = CreateSimpleSphere(radius: 5.0);
        var surfacePoint = new Vector3d(5.0, 0, 0); // Point on surface
        var surfaceNormal = new Vector3d(1, 0, 0);
        double diameter = 2.0;

        var result = DrainHole.PlaceDrainHole(mesh, surfacePoint, surfaceNormal, diameter);

        Assert.True(result.HolePlaced);
        Assert.Equal(diameter, result.DiameterAchieved);
    }

    /// <summary>
    /// Test: multiple holes can be placed sequentially on the same mesh.
    /// </summary>
    [Fact]
    public void PlaceDrainHole_MultipleHoles_AllPlaced()
    {
        var mesh = CreateSimpleSphere(radius: 5.0);
        double diameter = 1.5;

        // Place three holes at different locations
        var locations = new[]
        {
            new { Point = new Vector3d(5, 0, 0), Normal = new Vector3d(1, 0, 0) },
            new { Point = new Vector3d(-5, 0, 0), Normal = new Vector3d(-1, 0, 0) },
            new { Point = new Vector3d(0, 5, 0), Normal = new Vector3d(0, 1, 0) }
        };

        int totalTrianglesRemoved = 0;
        foreach (var location in locations)
        {
            var result = DrainHole.PlaceDrainHole(mesh, location.Point, location.Normal, diameter);
            Assert.True(result.HolePlaced);
            totalTrianglesRemoved += result.TrianglesRemoved;
        }

        Assert.True(totalTrianglesRemoved > 0, "Multiple holes should remove triangles cumulatively.");
    }

    /// <summary>
    /// Test: invalid diameter (<=0) throws an exception.
    /// </summary>
    [Fact]
    public void PlaceDrainHole_InvalidDiameter_Throws()
    {
        var mesh = CreateSimpleCube();
        var surfacePoint = new Vector3d(0.5, 0.5, 0.0);
        var surfaceNormal = new Vector3d(0, 0, -1);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DrainHole.PlaceDrainHole(mesh, surfacePoint, surfaceNormal, diameter: 0.0));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DrainHole.PlaceDrainHole(mesh, surfacePoint, surfaceNormal, diameter: -1.0));
    }

    /// <summary>
    /// Test: negative countersink depth throws an exception.
    /// </summary>
    [Fact]
    public void PlaceDrainHole_NegativeCountersink_Throws()
    {
        var mesh = CreateSimpleCube();
        var surfacePoint = new Vector3d(0.5, 0.5, 0.0);
        var surfaceNormal = new Vector3d(0, 0, -1);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DrainHole.PlaceDrainHole(mesh, surfacePoint, surfaceNormal, 1.0, countersinkDepth: -0.5));
    }

    /// <summary>
    /// Test: hole placement on empty mesh returns no hole placed.
    /// </summary>
    [Fact]
    public void PlaceDrainHole_EmptyMesh_ReturnsFalse()
    {
        var mesh = new DMesh3();
        var surfacePoint = new Vector3d(0, 0, 0);
        var surfaceNormal = new Vector3d(0, 0, 1);

        var result = DrainHole.PlaceDrainHole(mesh, surfacePoint, surfaceNormal, 1.0);

        Assert.False(result.HolePlaced);
        Assert.Equal(0, result.TrianglesRemoved);
    }

    /// <summary>
    /// Test: the operation contract (IMeshOperation) works correctly.
    /// </summary>
    [Fact]
    public void PlaceDrainHoleOperation_Contract_Works()
    {
        var mesh = CreateSimpleSphere();
        int trianglesBefore = mesh.TriangleCount;

        var operation = new PlaceDrainHoleOperation(
            surfacePoint: new Vector3d(1, 0, 0),
            surfaceNormal: new Vector3d(1, 0, 0),
            diameter: 0.8,
            countersinkDepth: 0.2);

        // Preview should not modify the mesh
        var previewResult = operation.Preview(mesh);
        Assert.Equal(trianglesBefore, mesh.TriangleCount);

        // Apply should modify the mesh
        var applyResult = operation.Apply(mesh);
        if (applyResult.Changed)
        {
            Assert.True(mesh.TriangleCount <= trianglesBefore);
        }

        Assert.NotNull(operation.Name);
        Assert.Contains("Drain", operation.Name);
    }

    /// <summary>
    /// Test: countersink depth parameter is accepted.
    /// </summary>
    [Fact]
    public void PlaceDrainHole_WithCountersink_Accepted()
    {
        var mesh = CreateSimpleSphere();
        var surfacePoint = new Vector3d(1, 0, 0);
        var surfaceNormal = new Vector3d(1, 0, 0);
        double countersinkDepth = 0.5;

        var result = DrainHole.PlaceDrainHole(mesh, surfacePoint, surfaceNormal, 1.0, countersinkDepth);

        Assert.True(result.HolePlaced);
        Assert.Equal(countersinkDepth, result.CountersinkDepth);
    }
}

/// <summary>
/// Implicit sphere function for marching cubes test fixture.
/// </summary>
public sealed class ImplicitSphere : ImplicitFunction3d
{
    public double Radius = 1.0;

    public double Value(ref Vector3d pt) => pt.Length - Radius;
}
