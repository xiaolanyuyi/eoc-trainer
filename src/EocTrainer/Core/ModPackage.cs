using System.Text;

namespace EocTrainer.Core;

/// <summary>
/// Writes a Divinity: Original Sin 2 mod package (.pak).
///
/// The layout is the one the game itself writes for the Definitive Edition
/// (verified against <c>Game.pak</c>, <c>Engine.pak</c> and every workshop mod):
///
/// <code>
///   [file data, each chunk padded to 64 bytes with 0xAD]
///   [file list: uint32 count + LZ4 block of count * 280 byte entries]
///   [header: version(u32) fileListOffset(u32) fileListSize(u32) numParts(u16)
///            flags(u8) priority(u8) md5(16)]
///   [uint32 header size including this field and the magic = 40]
///   ["LSPK"]
/// </code>
///
/// Entry layout: 256 byte nul padded path, offsetInFile, sizeOnDisk,
/// uncompressedSize, archivePart, flags, crc32 (all u32, little endian).
///
/// Payloads are stored as literal-only LZ4 blocks. That keeps the writer free of
/// third party dependencies while still using the compression method the game
/// expects, and the archives stay small enough for a handful of Lua files.
/// </summary>
public static class ModPackage
{
    public const int PakVersion = 13;
    private const int HeaderSizeField = 40;
    private const int Alignment = 64;
    private const byte PadByte = 0xAD;
    private const int EntrySize = 280;
    private const uint EntryFlags = 0x42;      // LZ4 | "max compression" marker
    private const byte HeaderFlags = 0x02;     // PackageFlags.AllowMemoryMapping
    private const string ModsPrefix = "Mods";

    /// <summary>Builds the package for <paramref name="folder"/> from the given files.</summary>
    public static byte[] Build(IReadOnlyList<(string Path, byte[] Contents)> files, string folder)
    {
        if (files.Count == 0) throw new ArgumentException("no files to package", nameof(files));

        var ordered = files.OrderBy(f => f.Path, StringComparer.Ordinal).ToList();
        var data = new MemoryStream();
        var entries = new List<byte[]>(ordered.Count);

        foreach (var (path, contents) in ordered)
        {
            var name = $"{ModsPrefix}/{folder}/{path.Replace('\\', '/')}";
            var packed = Lz4Store(contents);

            var offset = (uint)data.Length;
            data.Write(packed);
            while (data.Length % Alignment != 0) data.WriteByte(PadByte);

            var entry = new byte[EntrySize];
            var encoded = Encoding.UTF8.GetBytes(name);
            if (encoded.Length >= 256) throw new InvalidOperationException($"path too long: {name}");
            encoded.CopyTo(entry, 0);

            WriteUInt32(entry, 256, offset);
            WriteUInt32(entry, 260, (uint)packed.Length);
            WriteUInt32(entry, 264, (uint)contents.Length);
            WriteUInt32(entry, 268, 0);                 // archive part
            WriteUInt32(entry, 272, EntryFlags);
            WriteUInt32(entry, 276, Crc32(packed));
            entries.Add(entry);
        }

        var entryBlob = new byte[entries.Count * EntrySize];
        for (var i = 0; i < entries.Count; i++) entries[i].CopyTo(entryBlob, i * EntrySize);

        var fileListOffset = (uint)data.Length;
        var fileList = new MemoryStream();
        WriteUInt32(fileList, (uint)entries.Count);
        fileList.Write(Lz4Store(entryBlob));
        var fileListBytes = fileList.ToArray();

        var output = new MemoryStream();
        data.WriteTo(output);
        output.Write(fileListBytes);

        // header
        WriteUInt32(output, PakVersion);
        WriteUInt32(output, fileListOffset);
        WriteUInt32(output, (uint)fileListBytes.Length);
        WriteUInt16(output, 1);                      // one part
        output.WriteByte(HeaderFlags);
        output.WriteByte(0);                         // priority
        output.Write(new byte[16]);                  // module hash (see ModDeployer)

        WriteUInt32(output, HeaderSizeField);
        output.Write("LSPK"u8);

        return output.ToArray();
    }

    /// <summary>
    /// Encodes a literal-only LZ4 block: a single sequence with no match, which
    /// any conformant LZ4 decoder accepts.
    /// </summary>
    public static byte[] Lz4Store(byte[] data)
    {
        var output = new MemoryStream(data.Length + 16);
        var literal = data.Length;
        var token = (byte)(Math.Min(literal, 15) << 4);
        output.WriteByte(token);

        if (literal >= 15)
        {
            var remaining = literal - 15;
            while (remaining >= 255)
            {
                output.WriteByte(255);
                remaining -= 255;
            }
            output.WriteByte((byte)remaining);
        }

        output.Write(data);
        return output.ToArray();
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BitConverter.TryWriteBytes(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BitConverter.TryWriteBytes(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteUInt32(byte[] target, int offset, uint value) =>
        BitConverter.TryWriteBytes(target.AsSpan(offset, 4), value);

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
            }
            table[i] = value;
        }
        return table;
    }

    public static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }
}
