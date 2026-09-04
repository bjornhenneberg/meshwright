using System.Text;
using g3;
using Meshwright.IO.Wavefront;
using Xunit;

namespace Meshwright.Tests.Wavefront;

/// <summary>
/// Tests for <see cref="ObjReader"/> — the OBJ half of §5.1's import scope, and the counterpart to
/// <see cref="ObjWriter"/>.
/// </summary>
public class ObjReaderTests
{
    private static DMesh3 Read(string obj) => ObjReader.Read(new StringReader(obj));

    [Fact]
    public void ReadsASingleTriangle()
    {
        DMesh3 mesh = Read("v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");

        Assert.Equal(3, mesh.VertexCount);
        Assert.Equal(1, mesh.TriangleCount);
        Assert.Equal(new Vector3d(1, 0, 0), mesh.GetVertex(1));
    }

    [Fact]
    public void FanTriangulatesPolygons()
    {
        // A quad becomes two triangles; an n-gon becomes n-2.
        DMesh3 quad = Read("v 0 0 0\nv 1 0 0\nv 1 1 0\nv 0 1 0\nf 1 2 3 4\n");
        Assert.Equal(2, quad.TriangleCount);

        DMesh3 pentagon = Read("v 0 0 0\nv 1 0 0\nv 2 1 0\nv 1 2 0\nv 0 2 0\nf 1 2 3 4 5\n");
        Assert.Equal(3, pentagon.TriangleCount);
    }

    [Fact]
    public void FanTriangulationPreservesWinding()
    {
        // Both triangles of the quad must wind the same way as the original polygon, or importing
        // a well-formed file would manufacture inverted-normal defects.
        DMesh3 mesh = Read("v 0 0 0\nv 1 0 0\nv 1 1 0\nv 0 1 0\nf 1 2 3 4\n");

        foreach (int tid in mesh.TriangleIndices())
        {
            Assert.True(mesh.GetTriNormal(tid).z > 0, "Counter-clockwise input should yield +Z normals.");
        }
    }

    [Theory]
    [InlineData("f 1 2 3")]
    [InlineData("f 1/1 2/2 3/3")]
    [InlineData("f 1//1 2//2 3//3")]
    [InlineData("f 1/1/1 2/2/2 3/3/3")]
    public void AcceptsEveryFaceCornerForm(string face)
    {
        DMesh3 mesh = Read($"v 0 0 0\nv 1 0 0\nv 0 1 0\nvt 0 0\nvt 1 0\nvt 0 1\nvn 0 0 1\n{face}\n");

        Assert.Equal(1, mesh.TriangleCount);
        Assert.Equal(new Index3i(0, 1, 2), mesh.GetTriangle(0));
    }

    [Fact]
    public void ResolvesNegativeIndicesRelativeToTheLastVertexRead()
    {
        // -1 is the most recently declared vertex, -3 the one three back.
        DMesh3 mesh = Read("v 0 0 0\nv 1 0 0\nv 0 1 0\nf -3 -2 -1\n");

        Assert.Equal(1, mesh.TriangleCount);
        Assert.Equal(new Index3i(0, 1, 2), mesh.GetTriangle(0));
    }

    [Fact]
    public void IgnoresCommentsBlankLinesAndUnknownDirectives()
    {
        DMesh3 mesh = Read(
            "# a comment\n\n" +
            "mtllib scene.mtl\no thing\ng group\ns 1\nusemtl red\n" +
            "v 0 0 0\nv 1 0 0\nv 0 1 0\n" +
            "vt 0 0\nvn 0 0 1\nvp 0.5\nl 1 2\n" +
            "f 1 2 3\n");

        Assert.Equal(1, mesh.TriangleCount);
    }

    [Fact]
    public void IgnoresTheOptionalFourthVertexComponent()
    {
        DMesh3 mesh = Read("v 0 0 0 1.0\nv 1 0 0 1.0\nv 0 1 0 1.0\nf 1 2 3\n");

        Assert.Equal(new Vector3d(1, 0, 0), mesh.GetVertex(1));
    }

    [Fact]
    public void HandlesCarriageReturnsAndExtraWhitespace()
    {
        DMesh3 mesh = Read("v 0 0 0\r\n   v   1   0   0  \r\nv 0 1 0\r\n\tf  1  2  3 \r\n");

        Assert.Equal(3, mesh.VertexCount);
        Assert.Equal(1, mesh.TriangleCount);
    }

    [Fact]
    public void DoesNotWeldCoincidentVertices()
    {
        // Two triangles sharing an edge geometrically, but not by index. Welding them here would
        // silently repair the file during import and hide the defect from Inspect.
        DMesh3 mesh = Read(
            "v 0 0 0\nv 1 0 0\nv 0 1 0\n" +
            "v 0 0 0\nv 1 0 0\nv 1 1 0\n" +
            "f 1 2 3\nf 4 5 6\n");

        Assert.Equal(6, mesh.VertexCount);
        Assert.Equal(2, mesh.TriangleCount);
    }

    [Fact]
    public void KeepsVerticesThatNoFaceReferences()
    {
        // A stray unreferenced vertex is a real-world condition, and one Inspect should be able to
        // report rather than one the reader should quietly drop.
        DMesh3 mesh = Read("v 0 0 0\nv 1 0 0\nv 0 1 0\nv 9 9 9\nf 1 2 3\n");

        Assert.Equal(4, mesh.VertexCount);
        Assert.Equal(1, mesh.TriangleCount);
    }

    [Fact]
    public void RoundTripsThroughObjWriter()
    {
        DMesh3 original = Read("v 0 0 0\nv 1 0 0\nv 0 1 0\nv 1 1 1\nf 1 2 3\nf 2 4 3\n");

        var buffer = new StringWriter();
        ObjWriter.Write(buffer, original);
        DMesh3 reloaded = Read(buffer.ToString());

        Assert.Equal(original.VertexCount, reloaded.VertexCount);
        Assert.Equal(original.TriangleCount, reloaded.TriangleCount);
        for (int i = 0; i < original.VertexCount; i++)
        {
            Assert.Equal(original.GetVertex(i).x, reloaded.GetVertex(i).x, 6);
            Assert.Equal(original.GetVertex(i).y, reloaded.GetVertex(i).y, 6);
            Assert.Equal(original.GetVertex(i).z, reloaded.GetVertex(i).z, 6);
        }
    }

    [Fact]
    public void ReadsFromAStream()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n"));

        Assert.Equal(1, ObjReader.Read(stream).TriangleCount);
    }

    [Fact]
    public void RejectsAFileWithNoVertices()
    {
        var ex = Assert.Throws<InvalidDataException>(() => Read("# nothing here\ng empty\n"));
        Assert.Contains("no vertices", ex.Message);
    }

    [Fact]
    public void RejectsAPointCloudWithNoFaces()
    {
        var ex = Assert.Throws<InvalidDataException>(() => Read("v 0 0 0\nv 1 0 0\nv 0 1 0\n"));
        Assert.Contains("no faces", ex.Message);
    }

    [Theory]
    [InlineData("v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 9\n", "out of range")]
    [InlineData("v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 0\n", "out of range")]
    [InlineData("v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 -9\n", "out of range")]
    [InlineData("v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2\n", "corners")]
    [InlineData("v 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n", "coordinates")]
    [InlineData("v 0 0 zero\nv 1 0 0\nv 0 1 0\nf 1 2 3\n", "coordinate")]
    [InlineData("v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 three\n", "vertex index")]
    public void RejectsMalformedInputWithAClearMessage(string obj, string expectedFragment)
    {
        var ex = Assert.Throws<InvalidDataException>(() => Read(obj));
        Assert.Contains(expectedFragment, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ErrorMessagesNameTheOffendingLine()
    {
        var ex = Assert.Throws<InvalidDataException>(() => Read("v 0 0 0\nv 1 0 0\nv 0 1 0\n\n\nf 1 2 99\n"));
        Assert.Contains("line 6", ex.Message);
    }
}
