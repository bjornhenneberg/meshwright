using g3;
using Meshwright.IO.Stl;
using Xunit;

namespace Meshwright.Tests.Stl;

public class StlWriterTests
{
    [Fact]
    public void RoundTripsThroughStlReader()
    {
        DMesh3 mesh = BuildTetrahedron();

        using var stream = new MemoryStream();
        StlWriter.Write(stream, mesh);
        stream.Position = 0;

        DMesh3 roundTripped = StlReader.Read(stream);

        Assert.Equal(mesh.TriangleCount, roundTripped.TriangleCount);
        AssertSameVertexPositions(mesh, roundTripped);
    }

    [Fact]
    public void WritesExpectedBinaryLayout()
    {
        DMesh3 mesh = BuildTetrahedron();

        using var stream = new MemoryStream();
        StlWriter.Write(stream, mesh);
        byte[] bytes = stream.ToArray();

        const int headerSize = 80;
        const int triangleCountSize = sizeof(uint);
        const int triangleRecordSize = 12 * sizeof(float) + 2;

        uint triangleCount = BitConverter.ToUInt32(bytes, headerSize);
        Assert.Equal((uint)mesh.TriangleCount, triangleCount);

        long expectedLength = headerSize + triangleCountSize + (long)triangleCount * triangleRecordSize;
        Assert.Equal(expectedLength, bytes.Length);
    }

    [Fact]
    public void WritesEmptyMesh()
    {
        var mesh = new DMesh3();

        using var stream = new MemoryStream();
        StlWriter.Write(stream, mesh);
        stream.Position = 0;

        DMesh3 roundTripped = StlReader.Read(stream);

        Assert.Equal(0, roundTripped.TriangleCount);
    }

    [Fact]
    public void WriteFileRoundTrips()
    {
        DMesh3 mesh = BuildTetrahedron();
        string path = Path.Combine(Path.GetTempPath(), $"meshwright-stlwriter-{Guid.NewGuid():N}.stl");

        try
        {
            StlWriter.WriteFile(path, mesh);
            DMesh3 roundTripped = StlReader.ReadFile(path);

            Assert.Equal(mesh.TriangleCount, roundTripped.TriangleCount);
            AssertSameVertexPositions(mesh, roundTripped);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static DMesh3 BuildTetrahedron()
    {
        var mesh = new DMesh3();
        int a = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int b = mesh.AppendVertex(new Vector3d(1, 0, 0));
        int c = mesh.AppendVertex(new Vector3d(0, 1, 0));
        int d = mesh.AppendVertex(new Vector3d(0, 0, 1));
        mesh.AppendTriangle(a, c, b);
        mesh.AppendTriangle(a, b, d);
        mesh.AppendTriangle(b, c, d);
        mesh.AppendTriangle(c, a, d);
        return mesh;
    }

    private static void AssertSameVertexPositions(DMesh3 expected, DMesh3 actual)
    {
        const double tolerance = 1e-5;

        var expectedPositions = new List<Vector3d>();
        foreach (int vid in expected.VertexIndices())
        {
            expectedPositions.Add(expected.GetVertex(vid));
        }

        var actualPositions = new List<Vector3d>();
        foreach (int vid in actual.VertexIndices())
        {
            actualPositions.Add(actual.GetVertex(vid));
        }

        Assert.Equal(expectedPositions.Count, actualPositions.Count);

        foreach (Vector3d expectedPosition in expectedPositions)
        {
            Assert.Contains(actualPositions, actualPosition => (actualPosition - expectedPosition).Length < tolerance);
        }
    }
}
