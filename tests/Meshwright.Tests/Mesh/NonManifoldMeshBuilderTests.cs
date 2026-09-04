using g3;
using Meshwright.Geometry.Diagnostics;
using Meshwright.Geometry.Mesh;
using Xunit;

namespace Meshwright.Tests.Mesh;

/// <summary>
/// Tests for <see cref="NonManifoldMeshBuilder"/>, which keeps geometry <see cref="DMesh3"/> cannot
/// hold directly by splitting the mesh at the offending vertices rather than dropping the triangle.
/// </summary>
public class NonManifoldMeshBuilderTests
{
    private static readonly Vector3d A = new(0, 0, 0);
    private static readonly Vector3d B = new(1, 0, 0);

    /// <summary>N triangles all sharing the edge A-B: a non-manifold edge for N &gt; 2.</summary>
    private static NonManifoldMeshBuilder FanAroundSharedEdge(int fanCount)
    {
        var builder = new NonManifoldMeshBuilder();
        int a = builder.AddVertexWelded(A);
        int b = builder.AddVertexWelded(B);

        for (int i = 0; i < fanCount; i++)
        {
            double angle = Math.PI * i / fanCount;
            int tip = builder.AddVertexWelded(new Vector3d(0.5, Math.Cos(angle), Math.Sin(angle)));
            builder.AddTriangle(a, b, tip);
        }

        return builder;
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(40)]
    public void KeepsEveryTriangleOfANonManifoldFan(int fanCount)
    {
        NonManifoldMeshBuilder builder = FanAroundSharedEdge(fanCount);

        Assert.Equal(fanCount, builder.Mesh.TriangleCount);
        Assert.Equal(0, builder.TrianglesDropped);
        // Two triangles fit the shared edge; every one after that needs a split.
        Assert.Equal(fanCount - 2, builder.NonManifoldTrianglesSplit);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(8)]
    public void TrianglesLandAtTheirTruePositions(int fanCount)
    {
        // Splitting must move connectivity, never geometry.
        NonManifoldMeshBuilder builder = FanAroundSharedEdge(fanCount);

        foreach (int tid in builder.Mesh.TriangleIndices())
        {
            Index3i tri = builder.Mesh.GetTriangle(tid);
            Vector3d[] corners = { builder.Mesh.GetVertex(tri.a), builder.Mesh.GetVertex(tri.b), builder.Mesh.GetVertex(tri.c) };

            Assert.Contains(corners, v => v.Distance(A) < 1e-9);
            Assert.Contains(corners, v => v.Distance(B) < 1e-9);
            Assert.True(builder.Mesh.GetTriArea(tid) > 1e-9, "A split triangle must keep its area.");
        }
    }

    [Theory]
    [InlineData(3)]
    [InlineData(8)]
    public void TheNonManifoldEdgeIsStillReportedAsADefect(int fanCount)
    {
        // The point of splitting rather than dropping: the defect stays visible. NonManifoldDetector
        // groups edges by position precisely to find this shape.
        NonManifoldMeshBuilder builder = FanAroundSharedEdge(fanCount);

        var issues = new NonManifoldDetector().Detect(builder.Mesh);

        Assert.NotEmpty(issues);
    }

    [Fact]
    public void ManifoldGeometryIsNeverSplit()
    {
        // Two triangles sharing one edge are perfectly legal and must stay welded.
        var builder = new NonManifoldMeshBuilder();
        int a = builder.AddVertexWelded(A);
        int b = builder.AddVertexWelded(B);
        int c = builder.AddVertexWelded(new Vector3d(0, 1, 0));
        int d = builder.AddVertexWelded(new Vector3d(0, -1, 0));

        Assert.True(builder.AddTriangle(a, b, c));
        Assert.True(builder.AddTriangle(b, a, d));

        Assert.Equal(0, builder.NonManifoldTrianglesSplit);
        Assert.Equal(4, builder.Mesh.VertexCount);
        Assert.Empty(new NonManifoldDetector().Detect(builder.Mesh));
    }

    [Fact]
    public void DegenerateTrianglesAreKeptAndStillReported()
    {
        // Two corners at one position weld to a single id; the triangle is genuinely degenerate and
        // should be reported as such, not silently discarded.
        var builder = new NonManifoldMeshBuilder();
        int a = builder.AddVertexWelded(A);
        int b = builder.AddVertexWelded(B);

        Assert.True(builder.AddTriangle(a, a, b));

        Assert.Equal(1, builder.Mesh.TriangleCount);
        Assert.Equal(1, builder.DegenerateTrianglesSplit);
        Assert.Equal(0, builder.TrianglesDropped);
        Assert.Single(new DegenerateTriangleDetector().Detect(builder.Mesh));
    }

    [Fact]
    public void WeldingSharesVerticesForCoincidentPositions()
    {
        var builder = new NonManifoldMeshBuilder();

        Assert.Equal(builder.AddVertexWelded(A), builder.AddVertexWelded(A));
        Assert.NotEqual(builder.AddVertexUnwelded(A), builder.AddVertexUnwelded(A));
    }

    [Fact]
    public void EveryTriangleIsAccountedFor()
    {
        NonManifoldMeshBuilder builder = FanAroundSharedEdge(10);

        Assert.Equal(builder.TriangleCount, builder.Mesh.TriangleCount + builder.TrianglesDropped);
    }
}
