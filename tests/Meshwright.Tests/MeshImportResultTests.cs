using System.Text;
using g3;
using Meshwright.Geometry.Diagnostics;
using Meshwright.IO;
using Meshwright.IO.Stl;
using Meshwright.IO.Wavefront;
using Xunit;

namespace Meshwright.Tests;

/// <summary>
/// Tests that import keeps geometry <c>DMesh3</c> cannot hold directly, by splitting the mesh at
/// the offending vertices. Found by checking the M4-1 corpus against Thingi10K's ground truth: 14
/// of 24 real print files were losing triangles on import, two of them about 73% of the mesh, after
/// which every detector was describing something the user had not opened.
/// </summary>
public class MeshImportResultTests
{
    /// <summary>Three triangles sharing one edge — a non-manifold edge, which DMesh3 cannot hold.</summary>
    private const string ThreeFacetsOnOneEdge = """
        solid nm
        facet normal 0 0 1
        outer loop
        vertex 0 0 0
        vertex 1 0 0
        vertex 0 1 0
        endloop
        endfacet
        facet normal 0 0 1
        outer loop
        vertex 0 0 0
        vertex 1 0 0
        vertex 0 -1 0
        endloop
        endfacet
        facet normal 0 1 0
        outer loop
        vertex 0 0 0
        vertex 1 0 0
        vertex 0 0 1
        endloop
        endfacet
        endsolid nm
        """;

    private static Stream StreamOf(string text) => new MemoryStream(Encoding.ASCII.GetBytes(text));

    [Fact]
    public void Stl_KeepsEveryTriangleOfANonManifoldEdge()
    {
        MeshImportResult result = StlReader.ReadWithDiagnostics(StreamOf(ThreeFacetsOnOneEdge));

        Assert.Equal(3, result.TrianglesInFile);
        Assert.Equal(3, result.Mesh.TriangleCount);
        Assert.Equal(1, result.NonManifoldTrianglesSplit);
        Assert.Equal(0, result.TrianglesDropped);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void Obj_KeepsEveryTriangleOfANonManifoldEdge()
    {
        MeshImportResult result = ObjReader.ReadWithDiagnostics(new StringReader(
            "v 0 0 0\nv 1 0 0\nv 0 1 0\nv 0 -1 0\nv 0 0 1\n" +
            "f 1 2 3\nf 1 2 4\nf 1 2 5\n"));

        Assert.Equal(3, result.TrianglesInFile);
        Assert.Equal(3, result.Mesh.TriangleCount);
        Assert.Equal(1, result.NonManifoldTrianglesSplit);
        Assert.Equal(0, result.TrianglesDropped);
    }

    [Fact]
    public void TheNonManifoldEdgeSurvivesImportAsADetectableDefect()
    {
        // Keeping the triangle is only worth anything if the defect is still reportable afterwards.
        DMesh3 mesh = StlReader.ReadWithDiagnostics(StreamOf(ThreeFacetsOnOneEdge)).Mesh;

        Assert.NotEmpty(new NonManifoldDetector().Detect(mesh));
    }

    [Fact]
    public void KeepsTrianglesWhoseCornersWeldedTogether()
    {
        // Two corners at the same position weld to one vertex; the triangle is genuinely degenerate
        // and is kept as a zero-area triangle so the degenerate detector can report it.
        MeshImportResult result = StlReader.ReadWithDiagnostics(StreamOf("""
            solid d
            facet normal 0 0 1
            outer loop
            vertex 0 0 0
            vertex 0 0 0
            vertex 1 0 0
            endloop
            endfacet
            endsolid d
            """));

        Assert.Equal(1, result.TrianglesInFile);
        Assert.Equal(1, result.Mesh.TriangleCount);
        Assert.Equal(1, result.DegenerateTrianglesSplit);
        Assert.Equal(0, result.NonManifoldTrianglesSplit);
        Assert.Equal(0, result.TrianglesDropped);
        Assert.Single(new DegenerateTriangleDetector().Detect(result.Mesh));
    }

    [Fact]
    public void EveryTriangleIsAccountedFor()
    {
        MeshImportResult result = StlReader.ReadWithDiagnostics(StreamOf(ThreeFacetsOnOneEdge));

        Assert.Equal(result.TrianglesInFile, result.Mesh.TriangleCount + result.TrianglesDropped);
        Assert.Equal(0, result.TrianglesDropped);
    }

    [Fact]
    public void CleanImportWarnsAboutNothing()
    {
        MeshImportResult result = ObjReader.ReadWithDiagnostics(new StringReader("v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n"));

        Assert.True(result.IsComplete);
        Assert.Null(result.Warning);
        Assert.Equal(0, result.TrianglesSplit);
    }

    [Fact]
    public void SplittingIsNotAWarning()
    {
        // Splitting loses nothing, and Inspect reports the underlying non-manifold defect itself,
        // so the status line must not double-report it as an import problem.
        MeshImportResult result = StlReader.ReadWithDiagnostics(StreamOf(ThreeFacetsOnOneEdge));

        Assert.True(result.IsComplete);
        Assert.Null(result.Warning);
        Assert.Equal(1, result.TrianglesSplit);
    }

    [Fact]
    public void ImporterSurfacesTheSameCountsThroughTheDispatcher()
    {
        MeshImportResult result = MeshImporter.ImportWithDiagnostics(StreamOf(ThreeFacetsOnOneEdge), "part.stl");

        Assert.Equal(1, result.NonManifoldTrianglesSplit);
        Assert.Equal(3, result.Mesh.TriangleCount);
    }
}
