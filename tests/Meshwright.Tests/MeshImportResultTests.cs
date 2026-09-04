using System.Text;
using Meshwright.IO;
using Meshwright.IO.Stl;
using Meshwright.IO.Wavefront;
using Xunit;

namespace Meshwright.Tests;

/// <summary>
/// Tests that import reports the geometry <c>DMesh3</c> cannot represent instead of dropping it
/// silently. Found by checking the M4-1 corpus against Thingi10K's ground truth: 14 of 24 real
/// print files were losing triangles on import with no indication, two of them about 73% of the
/// mesh, after which every detector was describing a different mesh from the one the user opened.
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
    public void Stl_ReportsTrianglesRefusedForNonManifoldEdges()
    {
        MeshImportResult result = StlReader.ReadWithDiagnostics(StreamOf(ThreeFacetsOnOneEdge));

        Assert.Equal(3, result.TrianglesInFile);
        Assert.Equal(2, result.Mesh.TriangleCount);
        Assert.Equal(1, result.NonManifoldTrianglesDropped);
        Assert.False(result.IsLossless);
    }

    [Fact]
    public void Obj_ReportsTrianglesRefusedForNonManifoldEdges()
    {
        MeshImportResult result = ObjReader.ReadWithDiagnostics(new StringReader(
            "v 0 0 0\nv 1 0 0\nv 0 1 0\nv 0 -1 0\nv 0 0 1\n" +
            "f 1 2 3\nf 1 2 4\nf 1 2 5\n"));

        Assert.Equal(3, result.TrianglesInFile);
        Assert.Equal(1, result.NonManifoldTrianglesDropped);
    }

    [Fact]
    public void ReportsTrianglesRefusedForRepeatedCorners()
    {
        // Two corners at the same position weld to one vertex, leaving a triangle with a repeated
        // corner that DMesh3 also refuses — counted separately from the non-manifold case.
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
        Assert.Equal(0, result.Mesh.TriangleCount);
        Assert.Equal(1, result.DegenerateTrianglesDropped);
        Assert.Equal(0, result.NonManifoldTrianglesDropped);
    }

    [Fact]
    public void EveryTriangleIsAccountedFor()
    {
        MeshImportResult result = StlReader.ReadWithDiagnostics(StreamOf(ThreeFacetsOnOneEdge));

        Assert.Equal(result.TrianglesInFile, result.Mesh.TriangleCount + result.TrianglesDropped);
    }

    [Fact]
    public void LosslessImportWarnsAboutNothing()
    {
        MeshImportResult result = ObjReader.ReadWithDiagnostics(new StringReader("v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n"));

        Assert.True(result.IsLossless);
        Assert.Null(result.Warning);
    }

    [Fact]
    public void WarningNamesTheScaleAndTheCauseInPlainLanguage()
    {
        string? warning = StlReader.ReadWithDiagnostics(StreamOf(ThreeFacetsOnOneEdge)).Warning;

        Assert.NotNull(warning);
        Assert.Contains("1 of 3 triangles", warning);
        Assert.Contains("more than two faces met along one edge", warning);
        // §5.1 asks for plain language, not mesh-topology jargon, in user-facing text.
        Assert.DoesNotContain("manifold", warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ImporterSurfacesTheSameCountsThroughTheDispatcher()
    {
        MeshImportResult result = MeshImporter.ImportWithDiagnostics(StreamOf(ThreeFacetsOnOneEdge), "part.stl");

        Assert.Equal(1, result.NonManifoldTrianglesDropped);
    }
}
