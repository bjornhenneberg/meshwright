using g3;

namespace Meshwright.IO.Stl;

/// <summary>Writes indexed <see cref="g3.DMesh3"/> meshes as binary STL (per §5.1: STL export is binary-only).</summary>
public static class StlWriter
{
    // Mirrors the layout StlReader.ReadBinary expects: 80-byte header, uint32 triangle
    // count, then per-triangle 12-byte normal + 3x 12-byte vertex + 2-byte attribute count.
    private const int HeaderSize = 80;

    public static void WriteFile(string path, DMesh3 mesh)
    {
        using var stream = File.Create(path);
        Write(stream, mesh);
    }

    public static void Write(Stream stream, DMesh3 mesh)
    {
        using var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);

        // Header content is arbitrary per the STL spec; identify the writer for anyone
        // inspecting the raw bytes, padded/truncated to exactly 80 bytes.
        byte[] header = new byte[HeaderSize];
        byte[] headerText = System.Text.Encoding.ASCII.GetBytes("Meshwright binary STL export");
        Array.Copy(headerText, header, Math.Min(headerText.Length, HeaderSize));
        writer.Write(header);

        writer.Write((uint)mesh.TriangleCount);

        foreach (int tid in mesh.TriangleIndices())
        {
            Index3i tri = mesh.GetTriangle(tid);
            Vector3d v0 = mesh.GetVertex(tri.a);
            Vector3d v1 = mesh.GetVertex(tri.b);
            Vector3d v2 = mesh.GetVertex(tri.c);

            Vector3d normal = ComputeNormal(v0, v1, v2);

            WriteVector3(writer, normal);
            WriteVector3(writer, v0);
            WriteVector3(writer, v1);
            WriteVector3(writer, v2);
            writer.Write((ushort)0); // attribute byte count, unused
        }

        writer.Flush();
    }

    private static Vector3d ComputeNormal(Vector3d v0, Vector3d v1, Vector3d v2)
    {
        Vector3d cross = (v1 - v0).Cross(v2 - v0);
        return cross.LengthSquared > 0 ? cross.Normalized : Vector3d.Zero;
    }

    private static void WriteVector3(BinaryWriter writer, Vector3d v)
    {
        writer.Write((float)v.x);
        writer.Write((float)v.y);
        writer.Write((float)v.z);
    }
}
