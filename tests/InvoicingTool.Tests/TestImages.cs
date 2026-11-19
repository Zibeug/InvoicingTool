using System.IO.Compression;
using System.Text;

namespace InvoicingTool.Tests;

/// <summary>Génère de petites images PNG (logo, signature) pour les tests, sans bibliothèque graphique.</summary>
public static class TestImages
{
    /// <summary>Image RGBA : <paramref name="pixel"/> renvoie la couleur (r, g, b, a) de chaque point.</summary>
    public static byte[] Png(int width, int height, Func<int, int, (byte R, byte G, byte B, byte A)> pixel)
    {
        var raw = new MemoryStream();
        for (var y = 0; y < height; y++)
        {
            raw.WriteByte(0); // pas de filtre
            for (var x = 0; x < width; x++)
            {
                var (r, g, b, a) = pixel(x, y);
                raw.Write([r, g, b, a]);
            }
        }

        var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(raw.ToArray());

        var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        WriteInt(header, 0, width);
        WriteInt(header, 4, height);
        header[8] = 8; // 8 bits par canal
        header[9] = 6; // RGBA
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    /// <summary>Un trait façon signature, noir sur fond transparent.</summary>
    public static byte[] Signature() => Png(300, 100, (x, y) =>
    {
        var curve = 50 + (int)(30 * Math.Sin(x / 25.0));
        return Math.Abs(y - curve) <= 2 ? ((byte)20, (byte)20, (byte)60, (byte)255) : ((byte)0, (byte)0, (byte)0, (byte)0);
    });

    /// <summary>Un logo : cercle coloré.</summary>
    public static byte[] Logo() => Png(120, 120, (x, y) =>
    {
        var inside = (x - 60) * (x - 60) + (y - 60) * (y - 60) < 55 * 55;
        return inside ? ((byte)200, (byte)60, (byte)90, (byte)255) : ((byte)0, (byte)0, (byte)0, (byte)0);
    });

    private static void Chunk(Stream output, string type, byte[] data)
    {
        var length = new byte[4];
        WriteInt(length, 0, data.Length);
        output.Write(length);
        var typeAndData = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        output.Write(typeAndData);
        var crc = new byte[4];
        WriteInt(crc, 0, (int)Crc32(typeAndData));
        output.Write(crc);
    }

    private static void WriteInt(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }
}
