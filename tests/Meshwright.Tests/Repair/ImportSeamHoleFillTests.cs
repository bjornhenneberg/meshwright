using System.Linq;
using g3;
using Meshwright.Geometry.Diagnostics;
using Meshwright.Geometry.Mesh;
using Meshwright.Geometry.Repair;
using Xunit;
using Xunit.Abstractions;

namespace Meshwright.Tests.Repair;

public class ImportSeamHoleFillTests
{
    private readonly ITestOutputHelper _output;

    public ImportSeamHoleFillTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A closed cube whose top face has been detached by duplicating its four corner vertices —
    /// the shape <see cref="NonManifoldMeshBuilder"/> leaves behind when it cuts connectivity to
    /// represent geometry <see cref="DMesh3"/> cannot hold. The surface is geometrically complete:
    /// every boundary edge has another edge at the same two positions.
    /// </summary>
    internal static DMesh3 CubeWithADetachedTopFace()
    {
        var mesh = new DMesh3();
        Vector3d[] corners =
        {
            new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0),
            new(0, 0, 1), new(1, 0, 1), new(1, 1, 1), new(0, 1, 1),
        };

        int[] v = corners.Select(mesh.AppendVertex).ToArray();

        void Quad(int a, int b, int c, int d)
        {
            mesh.AppendTriangle(a, b, c);
            mesh.AppendTriangle(a, c, d);
        }

        Quad(v[0], v[3], v[2], v[1]);   // bottom
        Quad(v[0], v[1], v[5], v[4]);   // front
        Quad(v[3], v[7], v[6], v[2]);   // back
        Quad(v[0], v[4], v[7], v[3]);   // left
        Quad(v[1], v[2], v[6], v[5]);   // right

        // The top face, on its own copies of the same four positions.
        int[] top = new[] { 4, 5, 6, 7 }.Select(i => mesh.AppendVertex(corners[i])).ToArray();
        Quad(top[0], top[1], top[2], top[3]);

        return mesh;
    }

    [Fact]
    public void TheFixtureIsGeometricallyClosedButIndexOpen()
    {
        DMesh3 mesh = CubeWithADetachedTopFace();

        var loops = new MeshBoundaryLoops(mesh);
        Assert.Equal(2, loops.Loops.Count);

        HashSet<int> seams = PositionTopology.SeamEdges(mesh);
        foreach (EdgeLoop loop in loops.Loops)
        {
            Assert.All(loop.Edges, edge => Assert.Contains(edge, seams));
        }
    }

    [Fact]
    public void Diagnostics_ReportNoHole()
    {
        DMesh3 mesh = CubeWithADetachedTopFace();

        Assert.Empty(new BoundaryHoleDetector().Detect(mesh));
    }

    /// <summary>
    /// Backlog item 24: hole filling used to find its loops with <see cref="MeshBoundaryLoops"/>,
    /// which is vertex-index based and has no seam exclusion, so it added geometry across a seam
    /// Inspect had correctly reported as no hole at all — on this fixture, a second lid draped over
    /// the lid the model already had.
    /// </summary>
    [Theory]
    [InlineData(HoleFillMode.Flat)]
    [InlineData(HoleFillMode.Planar)]
    [InlineData(HoleFillMode.Smooth)]
    public void FillingAddsNothingWhereDetectionFoundNoHole(HoleFillMode mode)
    {
        DMesh3 mesh = CubeWithADetachedTopFace();
        int trianglesBefore = mesh.TriangleCount;
        int verticesBefore = mesh.VertexCount;

        HoleFillResult result = HoleFillRepair.Fill(mesh, mode);
        _output.WriteLine($"{mode}: filled {result.HolesFilled} holes, {result.TrianglesAdded} triangles added");

        Assert.Equal(0, result.HolesFilled);
        Assert.Equal(0, result.TrianglesAdded);
        Assert.Equal(trianglesBefore, mesh.TriangleCount);
        Assert.Equal(verticesBefore, mesh.VertexCount);
    }
}
