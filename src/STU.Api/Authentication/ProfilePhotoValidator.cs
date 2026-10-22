using System.Buffers.Binary;
using System.IO.Compression;

namespace STU.Api.Authentication;

// Accept the small, static PNGs produced by the profile canvas; never serve uploaded SVG/HTML.
internal static class ProfilePhotoValidator
{
    public static bool IsValid(byte[] data)
    {
        if (data.Length is < 57 or > 524288 || !data.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return false;
        try
        {
            var position = 8; var width = 0; var height = 0; var channels = 0; var ended = false;
            using var compressed = new MemoryStream();
            while (position + 12 <= data.Length)
            {
                var size = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(position, 4));
                if (size > data.Length - position - 12) return false;
                var length = (int)size;
                var type = System.Text.Encoding.ASCII.GetString(data, position + 4, 4);
                var chunk = data.AsSpan(position + 8, length);
                if (Crc(data.AsSpan(position + 4, length + 4)) != BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(position + 8 + length, 4))) return false;
                if (position == 8 && type != "IHDR") return false;
                if (type == "IHDR")
                {
                    if (position != 8 || length != 13) return false;
                    var w = BinaryPrimitives.ReadUInt32BigEndian(chunk[..4]); var h = BinaryPrimitives.ReadUInt32BigEndian(chunk.Slice(4, 4));
                    if (w is 0 or > 512 || h is 0 or > 512 || chunk[8] != 8 || chunk[9] is not (2 or 6) || chunk[10] != 0 || chunk[11] != 0 || chunk[12] != 0) return false;
                    width = (int)w; height = (int)h; channels = chunk[9] == 6 ? 4 : 3;
                }
                else if (type == "IDAT") compressed.Write(chunk);
                else if (type == "IEND") { if (length != 0 || position + 12 != data.Length) return false; ended = true; break; }
                else if (type is not ("sRGB" or "gAMA" or "pHYs")) return false;
                position += length + 12;
            }
            if (!ended || compressed.Length == 0 || channels == 0) return false;
            compressed.Position = 0;
            using var inflater = new ZLibStream(compressed, CompressionMode.Decompress);
            var row = new byte[width * channels + 1];
            for (var y = 0; y < height; y++) { inflater.ReadExactly(row); if (row[0] > 4) return false; }
            return inflater.ReadByte() == -1;
        }
        catch (Exception error) when (error is IOException or InvalidDataException) { return false; }
    }

    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes) { crc ^= value; for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u); }
        return ~crc;
    }
}
