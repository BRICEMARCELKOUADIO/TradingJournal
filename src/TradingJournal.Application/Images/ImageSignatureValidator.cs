using System.Buffers.Binary;
using System.ComponentModel.DataAnnotations;

namespace TradingJournal.Application.Images;

// Format/container validation, not an antivirus or a full pixel decoder. Downloads are attachments.
public static class ImageSignatureValidator
{
    public static void Validate(byte[] bytes, string mime)
    {
        var valid = bytes.Length is > 0 and <= ImageOptions.MaxBytes && mime switch
        {
            "image/png" => Png(bytes), "image/jpeg" => Jpeg(bytes), "image/webp" => Webp(bytes), _ => false
        };
        if (!valid) throw new ValidationException("Le contenu ne correspond pas à une image autorisée.");
    }

    private static bool Dimensions(uint width, uint height) => width > 0 && height > 0 && (ulong)width * height <= 40_000_000;
    private static bool Png(ReadOnlySpan<byte> data)
    {
        if (data.Length < 45 || !data[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return false;
        var offset = 8;
        var header = false;
        var pixels = false;
        while (offset <= data.Length - 12)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));
            if (length > (uint)(data.Length - offset - 12)) return false;
            var chunk = data.Slice(offset + 4, (int)length + 4);
            var crc = 0xffffffffu;
            foreach (var value in chunk)
            {
                crc ^= value;
                for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
            }
            if (~crc != BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset + 8 + (int)length, 4))) return false;
            var type = chunk[..4];
            if (!header)
            {
                if (!type.SequenceEqual("IHDR"u8) || length != 13
                    || !Dimensions(BinaryPrimitives.ReadUInt32BigEndian(chunk.Slice(4, 4)), BinaryPrimitives.ReadUInt32BigEndian(chunk.Slice(8, 4)))) return false;
                header = true;
            }
            else if (type.SequenceEqual("IHDR"u8)) return false;
            if (type.SequenceEqual("IDAT"u8) && length > 0) pixels = true;
            offset += (int)length + 12;
            if (type.SequenceEqual("IEND"u8)) return length == 0 && pixels && offset == data.Length;
        }
        return false;
    }

    private static bool Jpeg(ReadOnlySpan<byte> data)
    {
        if (data.Length < 20 || data[0] != 0xff || data[1] != 0xd8) return false;
        var offset = 2;
        var frame = false;
        var scan = false;
        while (offset < data.Length)
        {
            if (data[offset++] != 0xff) return false;
            while (offset < data.Length && data[offset] == 0xff) offset++;
            if (offset >= data.Length) return false;
            var marker = data[offset++];
            if (marker == 0xd9) return frame && scan && offset == data.Length;
            if (marker is 0 or 0xd8 || marker is >= 0xd0 and <= 0xd7 || offset + 2 > data.Length) return false;
            var size = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
            if (size < 2 || offset + size > data.Length) return false;
            if (marker is 0xc0 or 0xc1 or 0xc2)
            {
                if (size < 8 || !Dimensions(BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset + 5, 2)),
                    BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset + 3, 2)))) return false;
                frame = true;
            }
            offset += size;
            if (marker != 0xda) continue;
            if (!frame) return false;
            scan = true;
            while (offset < data.Length)
            {
                if (data[offset] != 0xff) { offset++; continue; }
                if (offset + 1 >= data.Length) return false;
                if (data[offset + 1] == 0 || data[offset + 1] is >= 0xd0 and <= 0xd7) { offset += 2; continue; }
                break;
            }
        }
        return false;
    }

    private static bool Webp(ReadOnlySpan<byte> data)
    {
        if (data.Length < 25 || !data[..4].SequenceEqual("RIFF"u8) || !data.Slice(8, 4).SequenceEqual("WEBP"u8)
            || BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4, 4)) != data.Length - 8) return false;
        var offset = 12;
        var pixels = false;
        while (offset <= data.Length - 8)
        {
            var type = data.Slice(offset, 4);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 4, 4));
            if (size > data.Length - offset - 8) return false;
            var chunk = data.Slice(offset + 8, (int)size);
            if (type.SequenceEqual("VP8 "u8))
            {
                if (size < 10 || !chunk.Slice(3, 3).SequenceEqual(new byte[] { 0x9d, 1, 0x2a })
                    || !Dimensions((uint)(BinaryPrimitives.ReadUInt16LittleEndian(chunk.Slice(6, 2)) & 0x3fff),
                        (uint)(BinaryPrimitives.ReadUInt16LittleEndian(chunk.Slice(8, 2)) & 0x3fff))) return false;
                pixels = true;
            }
            if (type.SequenceEqual("VP8L"u8))
            {
                if (size < 5 || chunk[0] != 0x2f) return false;
                var bits = BinaryPrimitives.ReadUInt32LittleEndian(chunk.Slice(1, 4));
                if ((bits >> 29) != 0 || !Dimensions((bits & 0x3fff) + 1, ((bits >> 14) & 0x3fff) + 1)) return false;
                pixels = true;
            }
            // Animated WebP is deliberately excluded for screenshot uploads.
            if (type.SequenceEqual("ANIM"u8) || type.SequenceEqual("ANMF"u8)) return false;
            offset += 8 + (int)size + (int)(size & 1);
        }
        return pixels && offset == data.Length;
    }
}
