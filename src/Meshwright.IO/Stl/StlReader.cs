using System.Globalization;
using System.Numerics;
using System.Text;
namespace Meshwright.IO.Stl;


/// <summary>Reads binary and ASCII STL streams into indexed <see cref="g3.DMesh3"/> meshes.</summary>
public static class StlReader
{
    private const int HeaderSize = 80;
    private const int TriangleCountSize = sizeof(uint);
    private const int BinaryTriangleRecordSize = 12 * sizeof(float) + 2; // normal + 3 vertices + attribute bytes

    public static g3.DMesh3 ReadFile(string path) => ReadFileWithDiagnostics(path).Mesh;

    public static g3.DMesh3 Read(Stream stream) => ReadWithDiagnostics(stream).Mesh;

    /// <summary>
    /// Reads the mesh and reports how many triangles the file contained that
    /// <see cref="g3.DMesh3"/> could not represent. See <see cref="MeshImportResult"/> — a silent
    /// drop here makes every downstream diagnostic a statement about a different mesh.
    /// </summary>
    public static MeshImportResult ReadFileWithDiagnostics(string path)
    {
        using var stream = File.OpenRead(path);
        return ReadWithDiagnostics(stream);
    }

    /// <inheritdoc cref="ReadFileWithDiagnostics"/>
    public static MeshImportResult ReadWithDiagnostics(Stream stream)
    {
        byte[] buffer = ReadAllBytes(stream);

        return LooksBinary(buffer) ? ReadBinary(buffer) : ReadAscii(buffer);
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        if (stream is MemoryStream ms && ms.TryGetBuffer(out var segment))
        {
            return segment.Array is not null && segment.Offset == 0 && segment.Count == segment.Array.Length
                ? segment.Array
                : ms.ToArray();
        }

        using var buffered = new MemoryStream();
        stream.CopyTo(buffered);
        return buffered.ToArray();
    }

    private static bool LooksBinary(byte[] buffer)
    {
        if (buffer.Length < HeaderSize + TriangleCountSize)
        {
            // Too short to be binary; let ASCII parsing raise a clear error.
            return false;
        }

        uint triangleCount = BitConverter.ToUInt32(buffer, HeaderSize);
        long expectedBinaryLength = HeaderSize + TriangleCountSize + (long)triangleCount * BinaryTriangleRecordSize;
        return expectedBinaryLength == buffer.Length;
    }

    private static MeshImportResult ReadBinary(byte[] buffer)
    {
        if (buffer.Length < HeaderSize + TriangleCountSize)
        {
            throw new InvalidDataException("STL file is too short to contain a binary header and triangle count.");
        }

        uint triangleCount = BitConverter.ToUInt32(buffer, HeaderSize);
        long expectedLength = HeaderSize + TriangleCountSize + (long)triangleCount * BinaryTriangleRecordSize;
        if (expectedLength != buffer.Length)
        {
            throw new InvalidDataException(
                $"Truncated binary STL: expected {expectedLength} bytes for {triangleCount} triangles, but the stream has {buffer.Length} bytes.");
        }

        var positions = new Vector3[triangleCount * 3];
        int offset = HeaderSize + TriangleCountSize;
        for (int i = 0; i < triangleCount; i++)
        {
            offset += 12;

            positions[i * 3 + 0] = ReadVector3(buffer, offset);
            offset += 12;
            positions[i * 3 + 1] = ReadVector3(buffer, offset);
            offset += 12;
            positions[i * 3 + 2] = ReadVector3(buffer, offset);
            offset += 12;

            offset += 2; // attribute byte count, unused
        }

        return BuildIndexedMesh(positions);
    }

    private static Vector3 ReadVector3(byte[] buffer, int offset)
    {
        float x = BitConverter.ToSingle(buffer, offset);
        float y = BitConverter.ToSingle(buffer, offset + 4);
        float z = BitConverter.ToSingle(buffer, offset + 8);
        return new Vector3(x, y, z);
    }

    private static MeshImportResult ReadAscii(byte[] buffer)
    {
        string text = Encoding.ASCII.GetString(buffer);
        string[] tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        var positions = new List<Vector3>();

        int i = 0;
        bool sawSolid = false;
        while (i < tokens.Length)
        {
            string token = tokens[i];
            if (token.Equals("solid", StringComparison.OrdinalIgnoreCase))
            {
                sawSolid = true;
                i++;
                // Skip the (optional) solid name.
                while (i < tokens.Length && !tokens[i].Equals("facet", StringComparison.OrdinalIgnoreCase)
                                          && !tokens[i].Equals("endsolid", StringComparison.OrdinalIgnoreCase))
                {
                    i++;
                }

                continue;
            }

            if (token.Equals("facet", StringComparison.OrdinalIgnoreCase))
            {
                i = ParseFacet(tokens, i, positions);
                continue;
            }

            if (token.Equals("endsolid", StringComparison.OrdinalIgnoreCase))
            {
                i++;
                // Skip the (optional) solid name.
                while (i < tokens.Length && !tokens[i].Equals("solid", StringComparison.OrdinalIgnoreCase))
                {
                    i++;
                }

                continue;
            }

            throw new InvalidDataException($"Unexpected token '{token}' while parsing ASCII STL.");
        }

        if (!sawSolid)
        {
            throw new InvalidDataException("Input is neither a valid binary STL nor a valid ASCII STL (missing 'solid' keyword).");
        }

        return BuildIndexedMesh(positions);
    }

    private static int ParseFacet(string[] tokens, int i, List<Vector3> positions)
    {
        i = Expect(tokens, i, "facet");
        i = Expect(tokens, i, "normal");
        _ = ParseVector3(tokens, ref i);

        i = Expect(tokens, i, "outer");
        i = Expect(tokens, i, "loop");

        int vertexCount = 0;
        var triangleVerts = new Vector3[3];
        while (i < tokens.Length && tokens[i].Equals("vertex", StringComparison.OrdinalIgnoreCase))
        {
            i++;
            if (vertexCount >= 3)
            {
                throw new InvalidDataException("ASCII STL facet has more than 3 vertices; only triangles are supported.");
            }

            triangleVerts[vertexCount] = ParseVector3(tokens, ref i);
            vertexCount++;
        }

        if (vertexCount != 3)
        {
            throw new InvalidDataException($"ASCII STL facet has {vertexCount} vertices; expected 3.");
        }

        i = Expect(tokens, i, "endloop");
        i = Expect(tokens, i, "endfacet");

        positions.Add(triangleVerts[0]);
        positions.Add(triangleVerts[1]);
        positions.Add(triangleVerts[2]);

        return i;
    }

    private static MeshImportResult BuildIndexedMesh(IReadOnlyList<Vector3> positions)
    {
        var mesh = new g3.DMesh3();
        var vertexIds = new Dictionary<g3.Vector3d, int>();
        int triangleCount = 0;
        int nonManifoldDropped = 0;
        int degenerateDropped = 0;

        for (int positionIndex = 0; positionIndex < positions.Count; positionIndex += 3)
        {
            int[] triangle = new int[3];
            for (int corner = 0; corner < 3; corner++)
            {
                Vector3 position = positions[positionIndex + corner];
                var vertex = new g3.Vector3d(position.X, position.Y, position.Z);
                if (!vertexIds.TryGetValue(vertex, out int vertexId))
                {
                    vertexId = mesh.AppendVertex(vertex);
                    vertexIds.Add(vertex, vertexId);
                }

                triangle[corner] = vertexId;
            }

            triangleCount++;

            // AppendTriangle refuses rather than throws: NonManifoldID when the triangle would give
            // an edge a third face, InvalidID when welding has collapsed two corners onto one
            // vertex. Ignoring the result silently discards precisely the broken geometry this tool
            // exists to report, so count both.
            int appended = mesh.AppendTriangle(triangle[0], triangle[1], triangle[2]);
            if (appended == g3.DMesh3.NonManifoldID)
            {
                nonManifoldDropped++;
            }
            else if (appended < 0)
            {
                degenerateDropped++;
            }
        }

        return new MeshImportResult(mesh, triangleCount, nonManifoldDropped, degenerateDropped);
    }

    private static int Expect(string[] tokens, int i, string expected)
    {
        if (i >= tokens.Length)
        {
            throw new InvalidDataException($"Unexpected end of input while expecting '{expected}'.");
        }

        if (!tokens[i].Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Expected token '{expected}' but found '{tokens[i]}'.");
        }

        return i + 1;
    }

    private static Vector3 ParseVector3(string[] tokens, ref int i)
    {
        if (i + 2 >= tokens.Length)
        {
            throw new InvalidDataException("Unexpected end of input while parsing a vector.");
        }

        float x = ParseFloat(tokens[i]);
        float y = ParseFloat(tokens[i + 1]);
        float z = ParseFloat(tokens[i + 2]);
        i += 3;
        return new Vector3(x, y, z);
    }

    private static float ParseFloat(string token)
    {
        if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
        {
            throw new InvalidDataException($"Could not parse '{token}' as a floating point number.");
        }

        return value;
    }
}
