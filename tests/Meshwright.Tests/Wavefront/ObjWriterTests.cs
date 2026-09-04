using System.Globalization;
using g3;
using Meshwright.IO.Wavefront;
using Xunit;

namespace Meshwright.Tests.Wavefront;

public class ObjWriterTests
{
    [Fact]
    public void WritesVertexAndFaceLinesForTetrahedron()
    {
        DMesh3 mesh = BuildTetrahedron();

        using var stream = new MemoryStream();
        ObjWriter.Write(stream, mesh);
        stream.Position = 0;
        string text = new StreamReader(stream).ReadToEnd();

        (List<Vector3d> vertices, List<(int A, int B, int C)> faces) = ParseObj(text);

        Assert.Equal(mesh.VertexCount, vertices.Count);
        Assert.Equal(mesh.TriangleCount, faces.Count);

        foreach ((int a, int b, int c) in faces)
        {
            AssertValidIndex(a, vertices.Count);
            AssertValidIndex(b, vertices.Count);
            AssertValidIndex(c, vertices.Count);
        }
    }

    [Fact]
    public void RemapsSparseInternalVertexIdsToSequentialObjIndices()
    {
        // Two disjoint triangles; removing the first (with its now-isolated vertices)
        // leaves the second triangle referencing internal vertex ids 3,4,5 rather than
        // 0,1,2 — proving the writer remaps ids instead of emitting raw internal ones.
        var mesh = new DMesh3();
        int v0 = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int v1 = mesh.AppendVertex(new Vector3d(1, 0, 0));
        int v2 = mesh.AppendVertex(new Vector3d(0, 1, 0));
        int v3 = mesh.AppendVertex(new Vector3d(5, 5, 5));
        int v4 = mesh.AppendVertex(new Vector3d(6, 5, 5));
        int v5 = mesh.AppendVertex(new Vector3d(5, 6, 5));
        int firstTriangle = mesh.AppendTriangle(v0, v1, v2);
        mesh.AppendTriangle(v3, v4, v5);

        MeshResult result = mesh.RemoveTriangle(firstTriangle, bRemoveIsolatedVertices: true);
        Assert.Equal(MeshResult.Ok, result);
        Assert.Equal(3, mesh.VertexCount);
        Assert.Equal(1, mesh.TriangleCount);

        using var stream = new MemoryStream();
        ObjWriter.Write(stream, mesh);
        stream.Position = 0;
        string text = new StreamReader(stream).ReadToEnd();

        (List<Vector3d> vertices, List<(int A, int B, int C)> faces) = ParseObj(text);

        Assert.Equal(3, vertices.Count);
        Assert.Single(faces);

        (int a, int b, int c) = faces[0];
        var referenced = new HashSet<int> { a, b, c };
        Assert.Equal(3, referenced.Count);
        AssertValidIndex(a, vertices.Count);
        AssertValidIndex(b, vertices.Count);
        AssertValidIndex(c, vertices.Count);
    }

    [Fact]
    public void WritesEmptyMesh()
    {
        var mesh = new DMesh3();

        using var stream = new MemoryStream();
        ObjWriter.Write(stream, mesh);
        stream.Position = 0;
        string text = new StreamReader(stream).ReadToEnd();

        (List<Vector3d> vertices, List<(int A, int B, int C)> faces) = ParseObj(text);

        Assert.Empty(vertices);
        Assert.Empty(faces);
    }

    [Fact]
    public void WriteFileRoundTripsThroughManualParse()
    {
        DMesh3 mesh = BuildTetrahedron();
        string path = Path.Combine(Path.GetTempPath(), $"meshwright-objwriter-{Guid.NewGuid():N}.obj");

        try
        {
            ObjWriter.WriteFile(path, mesh);
            string text = File.ReadAllText(path);

            (List<Vector3d> vertices, List<(int A, int B, int C)> faces) = ParseObj(text);

            Assert.Equal(mesh.VertexCount, vertices.Count);
            Assert.Equal(mesh.TriangleCount, faces.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void AssertValidIndex(int index, int vertexCount)
    {
        Assert.True(index >= 1 && index <= vertexCount, $"Face index {index} out of range [1,{vertexCount}].");
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

    /// <summary>Minimal manual OBJ parse sufficient for test assertions — not a general-purpose reader.</summary>
    private static (List<Vector3d> Vertices, List<(int, int, int)> Faces) ParseObj(string text)
    {
        var vertices = new List<Vector3d>();
        var faces = new List<(int, int, int)>();

        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens[0] == "v")
            {
                double x = double.Parse(tokens[1], CultureInfo.InvariantCulture);
                double y = double.Parse(tokens[2], CultureInfo.InvariantCulture);
                double z = double.Parse(tokens[3], CultureInfo.InvariantCulture);
                vertices.Add(new Vector3d(x, y, z));
            }
            else if (tokens[0] == "f")
            {
                int a = int.Parse(tokens[1], CultureInfo.InvariantCulture);
                int b = int.Parse(tokens[2], CultureInfo.InvariantCulture);
                int c = int.Parse(tokens[3], CultureInfo.InvariantCulture);
                faces.Add((a, b, c));
            }
            else
            {
                throw new InvalidDataException($"Unexpected OBJ line: '{line}'");
            }
        }

        return (vertices, faces);
    }
}
