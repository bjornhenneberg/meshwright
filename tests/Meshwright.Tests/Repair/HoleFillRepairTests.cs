using g3;
using Meshwright.Core.Operations;
using Meshwright.Geometry.Repair;
using Xunit;

namespace Meshwright.Tests.Repair;

public class HoleFillRepairTests
{
    private static DMesh3 BuildTetrahedron()
    {
        var mesh = new DMesh3();
        int v0 = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int v1 = mesh.AppendVertex(new Vector3d(1, 0, 0));
        int v2 = mesh.AppendVertex(new Vector3d(0, 1, 0));
        int v3 = mesh.AppendVertex(new Vector3d(0, 0, 1));

        mesh.AppendTriangle(v0, v2, v1); // bottom
        mesh.AppendTriangle(v0, v1, v3);
        mesh.AppendTriangle(v1, v2, v3);
        mesh.AppendTriangle(v2, v0, v3);

        return mesh;
    }

    private static DMesh3 BuildCubeMissingOneFace()
    {
        // Unit cube [0,1]^3, triangulated with outward-facing winding, top face omitted ->
        // one convex, 4-edge boundary loop.
        var mesh = new DMesh3();
        int v000 = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int v100 = mesh.AppendVertex(new Vector3d(1, 0, 0));
        int v110 = mesh.AppendVertex(new Vector3d(1, 1, 0));
        int v010 = mesh.AppendVertex(new Vector3d(0, 1, 0));
        int v001 = mesh.AppendVertex(new Vector3d(0, 0, 1));
        int v101 = mesh.AppendVertex(new Vector3d(1, 0, 1));
        int v111 = mesh.AppendVertex(new Vector3d(1, 1, 1));
        int v011 = mesh.AppendVertex(new Vector3d(0, 1, 1));

        void Quad(int a, int b, int c, int d)
        {
            mesh.AppendTriangle(a, b, c);
            mesh.AppendTriangle(a, c, d);
        }

        Quad(v000, v010, v110, v100); // bottom (z=0)
        // top (z=1) omitted -> one boundary loop of 4 edges
        Quad(v000, v100, v101, v001); // front (y=0)
        Quad(v010, v011, v111, v110); // back (y=1)
        Quad(v000, v001, v011, v010); // left (x=0)
        Quad(v100, v110, v111, v101); // right (x=1)

        return mesh;
    }

    /// <summary>
    /// A 4x4 grid of unit squares with its i and j indices wrapped (a flat-embedded torus, purely
    /// for connectivity -- the wrap-seam triangles are long slivers and not geometrically
    /// meaningful) so the mesh has no outer boundary at all, plus an interior L-shaped notch
    /// (squares (1,1), (2,1), (1,2), by lower-left grid index) left unfilled. That gives exactly
    /// one boundary loop: the notch's own 8-edge, reflex-at-(2,2) perimeter. Wrapping instead of
    /// leaving a natural outer edge sidesteps a real failure mode -- ear-clipping a boundary that
    /// sits directly on top of an already-triangulated flat region can legitimately pick a
    /// diagonal that collides with an existing internal mesh edge (AppendTriangle then rejects
    /// it), which happened here when this was a plain (non-wrapped) grid with a large flat outer
    /// boundary loop for the fill to close.
    /// </summary>
    private static DMesh3 BuildGridWithNonConvexInteriorHole()
    {
        const int n = 4;
        var mesh = new DMesh3();
        var verts = new int[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                verts[i, j] = mesh.AppendVertex(new Vector3d(i, j, 0));
            }
        }

        var excludedSquares = new HashSet<(int I, int J)> { (1, 1), (2, 1), (1, 2) };
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                if (excludedSquares.Contains((i, j)))
                {
                    continue;
                }
                int a = verts[i, j];
                int b = verts[(i + 1) % n, j];
                int c = verts[(i + 1) % n, (j + 1) % n];
                int d = verts[i, (j + 1) % n];
                mesh.AppendTriangle(a, b, c);
                mesh.AppendTriangle(a, c, d);
            }
        }

        return mesh;
    }

    private static int BoundaryLoopCount(DMesh3 mesh) => new MeshBoundaryLoops(mesh).Count;

    [Theory]
    [InlineData(HoleFillMode.Flat, 4)]
    [InlineData(HoleFillMode.Planar, 2)]
    // A cube face's boundary loop is a single quad: its diagonal (~1.41x the edge length) falls
    // under Smooth's refinement threshold (1.5x), so no interior vertex is added, and the sharp
    // 90-degree fold at a cube corner isn't the kind of continuous curvature Smooth models -- it
    // correctly matches Planar's ear-clip fill here rather than inventing a false bulge.
    [InlineData(HoleFillMode.Smooth, 2)]
    public void Fill_CubeMissingOneFace_ClosesTheHole(HoleFillMode mode, int expectedTrianglesAdded)
    {
        DMesh3 mesh = BuildCubeMissingOneFace();
        Assert.Equal(1, BoundaryLoopCount(mesh));
        int trianglesBefore = mesh.TriangleCount;

        HoleFillResult result = HoleFillRepair.Fill(mesh, mode);

        Assert.Equal(1, result.HolesFilled);
        Assert.Equal(expectedTrianglesAdded, result.TrianglesAdded);
        Assert.Equal(trianglesBefore + expectedTrianglesAdded, mesh.TriangleCount);
        Assert.Equal(0, BoundaryLoopCount(mesh));
    }

    [Theory]
    [InlineData(HoleFillMode.Flat)]
    [InlineData(HoleFillMode.Planar)]
    [InlineData(HoleFillMode.Smooth)]
    public void Fill_ClosedTetrahedron_IsNoOp(HoleFillMode mode)
    {
        DMesh3 mesh = BuildTetrahedron();
        int vertsBefore = mesh.VertexCount;
        int trisBefore = mesh.TriangleCount;
        Assert.Equal(0, BoundaryLoopCount(mesh));

        HoleFillResult result = HoleFillRepair.Fill(mesh, mode);

        Assert.Equal(0, result.HolesFilled);
        Assert.Equal(0, result.TrianglesAdded);
        Assert.Equal(vertsBefore, mesh.VertexCount);
        Assert.Equal(trisBefore, mesh.TriangleCount);
    }

    [Fact]
    public void Fill_NonConvexInteriorHole_Planar_EarClipsCorrectly()
    {
        DMesh3 mesh = BuildGridWithNonConvexInteriorHole();
        List<EdgeLoop> loopsBefore = new MeshBoundaryLoops(mesh).Loops;
        EdgeLoop loop = Assert.Single(loopsBefore);
        Assert.Equal(8, loop.Edges.Length); // the L-notch perimeter (two of its six polygon edges cross an extra collinear grid vertex)
        // Triangulating any simple polygon takes exactly (vertex count - 2) triangles, regardless
        // of convexity -- true here whether ear-clipping finds ears in "textbook" order or falls
        // back to fanning the remainder, so this is safe to assert without depending on exactly
        // which ears get picked first.
        int expectedTriangles = loop.Edges.Length - 2;
        int trisBefore = mesh.TriangleCount;

        HoleFillResult result = HoleFillRepair.Fill(mesh, HoleFillMode.Planar);

        Assert.Equal(1, result.HolesFilled);
        Assert.Equal(expectedTriangles, result.TrianglesAdded);
        Assert.Equal(trisBefore + expectedTriangles, mesh.TriangleCount);
        Assert.Equal(0, BoundaryLoopCount(mesh));
    }

    /// <summary>
    /// This hole sits in a flat face, so Smooth's curvature bulge is a no-op here (see
    /// <see cref="HoleFillSmoothTests.Smooth_FlatPlateHole_InteriorVerticesStayInThePlane"/> for
    /// that invariant on a cleaner example) -- what's worth pinning down on this particular
    /// non-convex loop is that its longest ear-clip diagonal (~2.24, crossing the L-shape) exceeds
    /// the refinement threshold (1.5x the ~1-unit boundary edge length) exactly once, adding
    /// exactly one interior vertex via a single edge split.
    /// </summary>
    [Fact]
    public void Fill_NonConvexInteriorHole_Smooth_AddsOneInteriorVertex()
    {
        DMesh3 mesh = BuildGridWithNonConvexInteriorHole();
        List<EdgeLoop> loopsBefore = new MeshBoundaryLoops(mesh).Loops;
        EdgeLoop loop = Assert.Single(loopsBefore);
        int expectedTriangles = loop.Edges.Length; // one ear split into 3: (n-2-1)+3 = n
        int vertsBefore = mesh.VertexCount;
        int trisBefore = mesh.TriangleCount;

        HoleFillResult result = HoleFillRepair.Fill(mesh, HoleFillMode.Smooth);

        Assert.Equal(1, result.HolesFilled);
        Assert.Equal(expectedTriangles, result.TrianglesAdded);
        Assert.Equal(vertsBefore + 1, mesh.VertexCount); // exactly one new interior (refinement) vertex
        Assert.Equal(trisBefore + expectedTriangles, mesh.TriangleCount);
        Assert.Equal(0, BoundaryLoopCount(mesh));
    }

    [Fact]
    public void FillHolesOperation_Apply_ClosesHoleAndReportsSummary()
    {
        DMesh3 mesh = BuildCubeMissingOneFace();
        var operation = new FillHolesOperation(HoleFillMode.Planar);

        OperationResult result = operation.Apply(mesh);

        Assert.True(result.Changed);
        Assert.Equal("Filled 1 hole (planar), adding 2 triangles.", result.Summary);
        Assert.Equal(0, BoundaryLoopCount(mesh));
    }

    [Fact]
    public void FillHolesOperation_Preview_DoesNotMutateCallerMesh()
    {
        DMesh3 mesh = BuildCubeMissingOneFace();
        int vertsBefore = mesh.VertexCount;
        int trisBefore = mesh.TriangleCount;
        var operation = new FillHolesOperation(HoleFillMode.Planar);

        OperationResult result = operation.Preview(mesh);

        Assert.True(result.Changed);
        Assert.Equal("Filled 1 hole (planar), adding 2 triangles.", result.Summary);
        Assert.Equal(vertsBefore, mesh.VertexCount);
        Assert.Equal(trisBefore, mesh.TriangleCount);
        Assert.Equal(1, BoundaryLoopCount(mesh)); // caller's mesh is untouched, hole still open
    }

    [Fact]
    public void FillHolesOperation_ClosedMesh_IsNoOp()
    {
        DMesh3 mesh = BuildTetrahedron();
        var operation = new FillHolesOperation();

        OperationResult result = operation.Apply(mesh);

        Assert.False(result.Changed);
        Assert.Equal("No holes found.", result.Summary);
    }
}
