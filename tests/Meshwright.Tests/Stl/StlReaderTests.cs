using System.Numerics;
using System.Text;
using Meshwright.IO.Stl;
using Xunit;

namespace Meshwright.Tests.Stl;

public class StlReaderTests
{
    // A unit cube, 12 triangles, axis-aligned normals.
    private static readonly (Vector3 Normal, Vector3 A, Vector3 B, Vector3 C)[] CubeTriangles = BuildCubeTriangles();

    [Fact]
    public void ReadsBinaryStl()
    {
        byte[] bytes = BuildBinaryStl(CubeTriangles);
        using var stream = new MemoryStream(bytes);

        var mesh = StlReader.Read(stream);

        AssertMatchesCube(mesh);
    }

    [Fact]
    public void ReadsAsciiStl()
    {
        string text = BuildAsciiStl(CubeTriangles);
        byte[] bytes = Encoding.ASCII.GetBytes(text);
        using var stream = new MemoryStream(bytes);

        var mesh = StlReader.Read(stream);

        AssertMatchesCube(mesh);
    }

    [Fact]
    public void ThrowsOnTruncatedBinaryStl()
    {
        byte[] bytes = BuildBinaryStl(CubeTriangles);
        byte[] truncated = bytes[..(bytes.Length - 10)];
        using var stream = new MemoryStream(truncated);

        Assert.Throws<InvalidDataException>(() => StlReader.Read(stream));
    }

    [Fact]
    public void ThrowsOnMalformedAsciiStl()
    {
        const string text = "solid broken\nfacet normal 0 0 1\nnot-a-loop\n";
        byte[] bytes = Encoding.ASCII.GetBytes(text);
        using var stream = new MemoryStream(bytes);

        Assert.Throws<InvalidDataException>(() => StlReader.Read(stream));
    }

    [Fact]
    public void ThrowsOnEmptyInput()
    {
        using var stream = new MemoryStream(Array.Empty<byte>());

        Assert.Throws<InvalidDataException>(() => StlReader.Read(stream));
    }

    private static void AssertMatchesCube(g3.DMesh3 mesh)
    {
        Assert.Equal(CubeTriangles.Length, mesh.TriangleCount);
        Assert.Equal(8, mesh.VertexCount);

        for (int i = 0; i < CubeTriangles.Length; i++)
        {
            g3.Index3i triangle = mesh.GetTriangle(i);
            AssertClose(CubeTriangles[i].A, ToVector3(mesh.GetVertex(triangle.a)));
            AssertClose(CubeTriangles[i].B, ToVector3(mesh.GetVertex(triangle.b)));
            AssertClose(CubeTriangles[i].C, ToVector3(mesh.GetVertex(triangle.c)));
        }
    }

    private static Vector3 ToVector3(g3.Vector3d vector) => new((float)vector.x, (float)vector.y, (float)vector.z);

    private static void AssertClose(Vector3 expected, Vector3 actual)
    {
        const float tolerance = 1e-5f;
        Assert.True((expected - actual).Length() < tolerance,
            $"Expected {expected} but got {actual}.");
    }

    private static (Vector3 Normal, Vector3 A, Vector3 B, Vector3 C)[] BuildCubeTriangles()
    {
        Vector3[] v =
        {
            new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0),
            new(0, 0, 1), new(1, 0, 1), new(1, 1, 1), new(0, 1, 1),
        };

        (int a, int b, int c, Vector3 n)[] faces =
        {
            (0, 1, 2, new Vector3(0, 0, -1)), (0, 2, 3, new Vector3(0, 0, -1)), // bottom
            (4, 6, 5, new Vector3(0, 0, 1)), (4, 7, 6, new Vector3(0, 0, 1)),   // top
            (0, 4, 5, new Vector3(0, -1, 0)), (0, 5, 1, new Vector3(0, -1, 0)), // front
            (1, 5, 6, new Vector3(1, 0, 0)), (1, 6, 2, new Vector3(1, 0, 0)),   // right
            (2, 6, 7, new Vector3(0, 1, 0)), (2, 7, 3, new Vector3(0, 1, 0)),   // back
            (3, 7, 4, new Vector3(-1, 0, 0)), (3, 4, 0, new Vector3(-1, 0, 0)), // left
        };

        var result = new (Vector3, Vector3, Vector3, Vector3)[faces.Length];
        for (int i = 0; i < faces.Length; i++)
        {
            var f = faces[i];
            result[i] = (f.n, v[f.a], v[f.b], v[f.c]);
        }

        return result;
    }

    private static byte[] BuildBinaryStl((Vector3 Normal, Vector3 A, Vector3 B, Vector3 C)[] triangles)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(new byte[80]);
        writer.Write((uint)triangles.Length);

        foreach (var t in triangles)
        {
            WriteVector3(writer, t.Normal);
            WriteVector3(writer, t.A);
            WriteVector3(writer, t.B);
            WriteVector3(writer, t.C);
            writer.Write((ushort)0);
        }

        writer.Flush();
        return ms.ToArray();
    }

    private static void WriteVector3(BinaryWriter writer, Vector3 v)
    {
        writer.Write(v.X);
        writer.Write(v.Y);
        writer.Write(v.Z);
    }

    private static string BuildAsciiStl((Vector3 Normal, Vector3 A, Vector3 B, Vector3 C)[] triangles)
    {
        var sb = new StringBuilder();
        sb.AppendLine("solid cube");
        foreach (var t in triangles)
        {
            sb.AppendLine($"facet normal {Fmt(t.Normal)}");
            sb.AppendLine("outer loop");
            sb.AppendLine($"vertex {Fmt(t.A)}");
            sb.AppendLine($"vertex {Fmt(t.B)}");
            sb.AppendLine($"vertex {Fmt(t.C)}");
            sb.AppendLine("endloop");
            sb.AppendLine("endfacet");
        }

        sb.AppendLine("endsolid cube");
        return sb.ToString();
    }

    private static string Fmt(Vector3 v) =>
        $"{v.X.ToString(System.Globalization.CultureInfo.InvariantCulture)} {v.Y.ToString(System.Globalization.CultureInfo.InvariantCulture)} {v.Z.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}
