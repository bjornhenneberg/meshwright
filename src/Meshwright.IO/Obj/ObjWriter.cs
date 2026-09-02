using System.Globalization;
using g3;

namespace Meshwright.IO.Obj;

/// <summary>Writes indexed <see cref="g3.DMesh3"/> meshes as ASCII Wavefront OBJ (§5.1).</summary>
public static class ObjWriter
{
    public static void WriteFile(string path, DMesh3 mesh)
    {
        using var stream = File.Create(path);
        Write(stream, mesh);
    }

    public static void Write(Stream stream, DMesh3 mesh)
    {
        using var writer = new StreamWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        Write(writer, mesh);
        writer.Flush();
    }

    public static void Write(TextWriter writer, DMesh3 mesh)
    {
        // OBJ vertex indices are 1-based and sequential; DMesh3's internal vertex ids may be
        // sparse (e.g. after RemoveVertex), so map internal id -> emitted 1-based OBJ index.
        var objIndex = new Dictionary<int, int>();
        int nextIndex = 1;

        foreach (int vid in mesh.VertexIndices())
        {
            Vector3d v = mesh.GetVertex(vid);
            writer.Write("v ");
            writer.Write(v.x.ToString(CultureInfo.InvariantCulture));
            writer.Write(' ');
            writer.Write(v.y.ToString(CultureInfo.InvariantCulture));
            writer.Write(' ');
            writer.Write(v.z.ToString(CultureInfo.InvariantCulture));
            writer.Write('\n');

            objIndex.Add(vid, nextIndex);
            nextIndex++;
        }

        foreach (int tid in mesh.TriangleIndices())
        {
            Index3i tri = mesh.GetTriangle(tid);
            writer.Write("f ");
            writer.Write(objIndex[tri.a].ToString(CultureInfo.InvariantCulture));
            writer.Write(' ');
            writer.Write(objIndex[tri.b].ToString(CultureInfo.InvariantCulture));
            writer.Write(' ');
            writer.Write(objIndex[tri.c].ToString(CultureInfo.InvariantCulture));
            writer.Write('\n');
        }
    }
}
