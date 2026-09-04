using System.Globalization;
using g3;
using Meshwright.IO;

namespace Meshwright.IO.Wavefront;

/// <summary>
/// Reads ASCII Wavefront OBJ streams into indexed <see cref="DMesh3"/> meshes (§5.1 import scope),
/// the counterpart to <see cref="ObjWriter"/>.
///
/// <para>
/// Unlike <see cref="Meshwright.IO.Stl.StlReader"/>, this reader does <em>not</em> weld vertices by
/// position. STL is triangle soup, so welding is the only way to recover an indexed mesh from it;
/// OBJ already carries the author's own vertex indexing, and silently merging coincident vertices
/// here would repair the file during import — hiding exactly the duplicate-vertex and
/// non-manifold defects Inspect exists to report. Import stays faithful; repair is
/// <c>Meshwright.Geometry.Repair</c>'s job and the user's choice.
/// </para>
/// </summary>
public static class ObjReader
{
    public static DMesh3 ReadFile(string path) => ReadFileWithDiagnostics(path).Mesh;

    public static DMesh3 Read(Stream stream) => ReadWithDiagnostics(stream).Mesh;

    public static DMesh3 Read(TextReader reader) => ReadWithDiagnostics(reader).Mesh;

    /// <summary>
    /// Reads the mesh and reports how many of the file's triangles <see cref="DMesh3"/> could not
    /// represent. See <see cref="MeshImportResult"/>.
    /// </summary>
    public static MeshImportResult ReadFileWithDiagnostics(string path)
    {
        using var stream = File.OpenRead(path);
        return ReadWithDiagnostics(stream);
    }

    /// <inheritdoc cref="ReadFileWithDiagnostics"/>
    public static MeshImportResult ReadWithDiagnostics(Stream stream)
    {
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return ReadWithDiagnostics(reader);
    }

    /// <inheritdoc cref="ReadFileWithDiagnostics"/>
    public static MeshImportResult ReadWithDiagnostics(TextReader reader)
    {
        var mesh = new DMesh3();

        // OBJ face indices may refer to vertices positionally (1-based) or relatively (negative,
        // counting back from the most recent vertex), so both need the running vertex count.
        int vertexCount = 0;
        bool sawFace = false;
        int lineNumber = 0;
        int triangleCount = 0;
        int nonManifoldDropped = 0;
        int degenerateDropped = 0;

        var corners = new List<int>();

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;

            ReadOnlySpan<char> span = line.AsSpan().Trim();
            if (span.IsEmpty || span[0] == '#')
            {
                continue;
            }

            if (StartsWithKeyword(span, "v"))
            {
                mesh.AppendVertex(ParseVertex(span[1..], lineNumber));
                vertexCount++;
                continue;
            }

            if (StartsWithKeyword(span, "f"))
            {
                ParseFaceCorners(span[1..], vertexCount, lineNumber, corners);

                // OBJ faces may be arbitrary polygons; fan-triangulate, preserving winding so the
                // orientation defects an inverted-normal detector looks for survive import.
                for (int i = 1; i + 1 < corners.Count; i++)
                {
                    triangleCount++;

                    // AppendTriangle refuses rather than throws; see MeshImportResult for why a
                    // silent drop here would misreport the whole mesh.
                    int appended = mesh.AppendTriangle(corners[0], corners[i], corners[i + 1]);
                    if (appended == DMesh3.NonManifoldID)
                    {
                        nonManifoldDropped++;
                    }
                    else if (appended < 0)
                    {
                        degenerateDropped++;
                    }
                }

                sawFace = true;
                continue;
            }

            // Everything else - vt, vn, vp, g, o, s, usemtl, mtllib, l, p - carries no geometry
            // Meshwright models, and an unknown directive is not a reason to reject a file that a
            // slicer would happily open.
        }

        if (vertexCount == 0)
        {
            throw new InvalidDataException("OBJ contains no vertices; it is not a valid mesh file.");
        }

        if (!sawFace)
        {
            throw new InvalidDataException("OBJ contains vertices but no faces; Meshwright imports surface meshes, not point clouds.");
        }

        return new MeshImportResult(mesh, triangleCount, nonManifoldDropped, degenerateDropped);
    }

    /// <summary>True when <paramref name="span"/> begins with <paramref name="keyword"/> as a whole token.</summary>
    private static bool StartsWithKeyword(ReadOnlySpan<char> span, ReadOnlySpan<char> keyword)
    {
        if (!span.StartsWith(keyword, StringComparison.Ordinal))
        {
            return false;
        }

        ReadOnlySpan<char> rest = span[keyword.Length..];
        return rest.Length > 0 && char.IsWhiteSpace(rest[0]);
    }

    private static Vector3d ParseVertex(ReadOnlySpan<char> span, int lineNumber)
    {
        Span<double> values = stackalloc double[3];
        int count = 0;

        foreach (Range range in SplitWhitespace(span))
        {
            ReadOnlySpan<char> token = span[range];
            if (token.IsEmpty)
            {
                continue;
            }

            // A 'v' may carry a fourth (rational w) component and, by common extension, r/g/b
            // colour after that. Both are beyond §5.1's scope, so take the first three and stop.
            if (count == 3)
            {
                break;
            }

            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                throw new InvalidDataException($"Could not parse '{token}' as a coordinate on line {lineNumber}.");
            }

            values[count++] = value;
        }

        if (count < 3)
        {
            throw new InvalidDataException($"Vertex on line {lineNumber} has {count} coordinates; expected at least 3.");
        }

        return new Vector3d(values[0], values[1], values[2]);
    }

    private static void ParseFaceCorners(ReadOnlySpan<char> span, int vertexCount, int lineNumber, List<int> corners)
    {
        corners.Clear();

        foreach (Range range in SplitWhitespace(span))
        {
            ReadOnlySpan<char> token = span[range];
            if (token.IsEmpty)
            {
                continue;
            }

            // Corners are "v", "v/vt", "v//vn" or "v/vt/vn"; only the position index is geometry.
            int slash = token.IndexOf('/');
            ReadOnlySpan<char> positionToken = slash < 0 ? token : token[..slash];

            if (!int.TryParse(positionToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
            {
                throw new InvalidDataException($"Could not parse '{positionToken}' as a vertex index on line {lineNumber}.");
            }

            // Positive indices are 1-based; negative ones count back from the last vertex read.
            int vertexId = index > 0 ? index - 1 : vertexCount + index;
            if (index == 0 || vertexId < 0 || vertexId >= vertexCount)
            {
                throw new InvalidDataException(
                    $"Face on line {lineNumber} references vertex {index}, which is out of range for the {vertexCount} vertices read so far.");
            }

            corners.Add(vertexId);
        }

        if (corners.Count < 3)
        {
            throw new InvalidDataException($"Face on line {lineNumber} has {corners.Count} corners; expected at least 3.");
        }
    }

    /// <summary>Splits on runs of whitespace without allocating a string per token.</summary>
    private static List<Range> SplitWhitespace(ReadOnlySpan<char> span)
    {
        var ranges = new List<Range>();
        int start = -1;

        for (int i = 0; i < span.Length; i++)
        {
            if (char.IsWhiteSpace(span[i]))
            {
                if (start >= 0)
                {
                    ranges.Add(new Range(start, i));
                    start = -1;
                }
            }
            else if (start < 0)
            {
                start = i;
            }
        }

        if (start >= 0)
        {
            ranges.Add(new Range(start, span.Length));
        }

        return ranges;
    }
}
