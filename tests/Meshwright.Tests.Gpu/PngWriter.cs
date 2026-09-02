using System;
using System.IO;
using System.IO.Compression;

namespace Meshwright.Tests.Gpu;

/// <summary>Minimal, dependency-free 8-bit RGBA PNG encoder (no filtering, one IDAT chunk) used
/// to save GPU test framebuffers to disk as visual evidence.</summary>
internal static class PngWriter
{
    private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    /// <summary>Writes <paramref name="rgba"/> (bottom-up rows, as returned by glReadPixels) to
    /// <paramref name="path"/> as a top-down PNG.</summary>
    public static void WriteRgba(string path, int width, int height, byte[] rgba)
    {
        using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write);
        fileStream.Write(Signature);

        WriteChunk(fileStream, "IHDR", BuildIhdr(width, height));
        WriteChunk(fileStream, "IDAT", BuildIdat(width, height, rgba));
        WriteChunk(fileStream, "IEND", Array.Empty<byte>());
    }

    private static byte[] BuildIhdr(int width, int height)
    {
        var data = new byte[13];
        WriteUInt32BigEndian(data, 0, (uint)width);
        WriteUInt32BigEndian(data, 4, (uint)height);
        data[8] = 8; // bit depth
        data[9] = 6; // color type: RGBA
        data[10] = 0; // compression method
        data[11] = 0; // filter method
        data[12] = 0; // interlace method
        return data;
    }

    private static byte[] BuildIdat(int width, int height, byte[] rgba)
    {
        int stride = width * 4;
        using var raw = new MemoryStream((stride + 1) * height);
        for (int y = 0; y < height; y++)
        {
            // glReadPixels rows are bottom-up; PNG rows are top-down.
            int srcRow = height - 1 - y;
            raw.WriteByte(0); // filter type: None
            raw.Write(rgba, srcRow * stride, stride);
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            raw.Position = 0;
            raw.CopyTo(zlib);
        }

        return compressed.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var typeBytes = new byte[4];
        for (int i = 0; i < 4; i++)
        {
            typeBytes[i] = (byte)type[i];
        }

        var lengthBytes = new byte[4];
        WriteUInt32BigEndian(lengthBytes, 0, (uint)data.Length);
        stream.Write(lengthBytes);
        stream.Write(typeBytes);
        stream.Write(data);

        uint crc = Crc32(typeBytes, data);
        var crcBytes = new byte[4];
        WriteUInt32BigEndian(crcBytes, 0, crc);
        stream.Write(crcBytes);
    }

    private static void WriteUInt32BigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        crc = UpdateCrc(crc, type);
        crc = UpdateCrc(crc, data);
        return crc ^ 0xFFFFFFFF;
    }

    private static uint UpdateCrc(uint crc, byte[] buffer)
    {
        foreach (byte b in buffer)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
        }

        return crc;
    }
}
