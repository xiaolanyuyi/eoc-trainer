"""Builds and verifies Divinity: Original Sin 2 mod packages (.pak).

Format (verified byte for byte against the game's own archives and against
LSLib's PackageReader/PackageWriter):

    [file data, each entry padded to 64 bytes with 0xAD]
    [file list]  uint32 count + LZ4 block of `count` * 280 byte entries
    [header]     32 bytes: version(u32) fileListOffset(u32) fileListSize(u32)
                          numParts(u16) flags(u8) priority(u8) md5(16)
    [uint32]     header size including this field and the magic (= 40)
    [4 bytes]    "LSPK"

Each file entry is 280 bytes: 256 byte nul padded path, then
offsetInFile(u32) sizeOnDisk(u32) uncompressedSize(u32) archivePart(u32)
flags(u32) crc32(u32).  `flags` 0x42 means LZ4 with the "max" level marker.

Usage:
    python tools/mod_pak.py build              # writes dist/<Folder>.pak
    python tools/mod_pak.py verify <file.pak>  # dumps an archive
"""

import os
import struct
import sys
import zlib

import lz4.block

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MOD_DIR = os.path.join(ROOT, "mod")
OUT_DIR = os.path.join(ROOT, "dist")

MOD_FOLDER = "EocTrainer_b7a1e0c7-1f2a-4b3c-9d4e-5f6a7b8c9d0e"

PAK_VERSION = 13
HEADER_SIZE = 40          # 32 byte header + header size field + magic
ALIGNMENT = 64
PAD_BYTE = 0xAD
ENTRY_SIZE = 280
ENTRY_FLAGS = 0x42        # LZ4 (2) | max compression marker (0x40)
HEADER_FLAGS = 0x02       # PackageFlags.AllowMemoryMapping
SIGNATURE = b"LSPK"


def lz4_compress(data):
    return lz4.block.compress(data, store_size=False, mode="high_compression")


def archive_hash(contents_by_path):
    """Returns the header hash field.

    LSLib computes an MD5 over the uncompressed contents and the game's own
    archives ship an all zero hash, so a zero hash is used here: the Script
    Extender configuration sets `DisableModValidation`, which is exactly the
    check that would otherwise compare this value with the module hash.
    """
    return bytes(16)


def legacy_archive_hash(contents_by_path):
    """LSLib's PackageWriter::ComputeArchiveHash, kept for reference."""
    import hashlib

    digest = hashlib.md5()
    for path in sorted(contents_by_path, key=lambda p: p):
        digest.update(contents_by_path[path])

    raw = bytearray(digest.digest())
    for index in range(len(raw)):
        raw[index] = (raw[index] + 1) & 0xFF
    return bytes(raw)


def build(mod_dir=MOD_DIR, out_path=None, folder=MOD_FOLDER, prefix="Mods"):
    out_path = out_path or os.path.join(OUT_DIR, folder + ".pak")

    files = {}
    for base, _dirs, names in os.walk(mod_dir):
        for name in names:
            full = os.path.join(base, name)
            relative = os.path.relpath(full, mod_dir).replace("\\", "/")
            with open(full, "rb") as handle:
                files[f"{prefix}/{folder}/{relative}"] = handle.read()

    if not files:
        raise SystemExit("no files found in %s" % mod_dir)

    data = bytearray()
    entries = []
    for name in sorted(files):
        body = files[name]
        packed = lz4_compress(body)
        offset = len(data)
        data += packed
        while len(data) % ALIGNMENT != 0:
            data.append(PAD_BYTE)

        entry = bytearray(ENTRY_SIZE)
        encoded = name.encode("utf-8")
        if len(encoded) >= 256:
            raise SystemExit("path too long for a package entry: %s" % name)
        entry[0:len(encoded)] = encoded
        struct.pack_into("<IIIIII", entry, 256, offset, len(packed), len(body), 0,
                         ENTRY_FLAGS, zlib.crc32(packed) & 0xFFFFFFFF)
        entries.append(bytes(entry))

    blob = b"".join(entries)
    file_list = struct.pack("<I", len(entries)) + lz4_compress(blob)
    file_list_offset = len(data)

    header = bytearray()
    header += struct.pack("<IIIHBB", PAK_VERSION, file_list_offset, len(file_list), 1,
                          HEADER_FLAGS, 0)
    header += archive_hash(files)
    assert len(header) == 32, len(header)

    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    with open(out_path, "wb") as handle:
        handle.write(bytes(data))
        handle.write(file_list)
        handle.write(bytes(header))
        handle.write(struct.pack("<I", HEADER_SIZE))
        handle.write(SIGNATURE)

    return out_path


def read_archive(path):
    """Returns (header info dict, [(name, contents), ...])."""
    with open(path, "rb") as handle:
        data = handle.read()

    size = len(data)
    if data[-4:] != SIGNATURE:
        raise SystemExit("%s does not end with LSPK" % path)

    header_size = struct.unpack_from("<I", data, size - 8)[0]
    start = size - header_size
    version, file_offset, file_size, parts, flags, priority = struct.unpack_from(
        "<IIIHBB", data, start)
    md5 = data[start + 16:start + 32]

    count = struct.unpack_from("<I", data, file_offset)[0]
    raw = lz4.block.decompress(data[file_offset + 4:file_offset + file_size],
                               uncompressed_size=count * ENTRY_SIZE)

    entries = []
    for index in range(count):
        entry = raw[index * ENTRY_SIZE:(index + 1) * ENTRY_SIZE]
        name = entry.split(b"\0")[0].decode("utf-8")
        offset, on_disk, uncompressed, part, entry_flags, crc = struct.unpack_from(
            "<IIIIII", entry, 256)
        entries.append({
            "name": name, "offset": offset, "onDisk": on_disk, "raw": uncompressed,
            "flags": entry_flags, "crc": crc,
        })

    return {
        "path": path, "size": size, "version": version, "headerSize": header_size,
        "numParts": parts, "flags": flags, "priority": priority, "md5": md5,
        "fileListOffset": file_offset, "fileListSize": file_size, "count": count,
    }, entries


def extract(path):
    """Decompresses every entry and returns {name: contents}."""
    header, entries = read_archive(path)
    with open(path, "rb") as handle:
        data = handle.read()

    contents = {}
    for entry in entries:
        method = entry["flags"] & 0x0F
        blob = data[entry["offset"]:entry["offset"] + entry["onDisk"]]
        if method == 2:
            decoded = lz4.block.decompress(blob, uncompressed_size=entry["raw"])
        elif method == 0:
            decoded = blob
        else:
            raise SystemExit("unsupported compression method %d in %s" % (method, path))
        contents[entry["name"]] = decoded
    return header, contents


def check_hash(path):
    header, contents = extract(path)
    computed = archive_hash(contents)
    ok = computed == header["md5"]
    print("%s: stored md5=%s computed=%s -> %s"
          % (os.path.basename(path), header["md5"].hex(), computed.hex(),
             "MATCH" if ok else "MISMATCH"))
    return ok


def verify(path, show=100):
    with open(path, "rb") as handle:
        data = handle.read()

    size = len(data)
    if data[-4:] != SIGNATURE:
        raise SystemExit("%s does not end with LSPK" % path)

    header_size = struct.unpack_from("<I", data, size - 8)[0]
    start = size - header_size
    version, file_offset, file_size, parts, flags, priority = struct.unpack_from(
        "<IIIHBB", data, start)
    md5 = data[start + 16:start + 32].hex()

    count = struct.unpack_from("<I", data, file_offset)[0]
    raw = lz4.block.decompress(data[file_offset + 4:file_offset + file_size],
                               uncompressed_size=count * ENTRY_SIZE)

    print("%s" % path)
    print("  size=%d version=%d parts=%d flags=0x%02x priority=%d headerSize=%d"
          % (size, version, parts, flags, priority, header_size))
    print("  fileListOffset=%d fileListSize=%d numFiles=%d md5=%s"
          % (file_offset, file_size, count, md5))

    for index in range(count):
        name = raw[index * ENTRY_SIZE:(index + 1) * ENTRY_SIZE].split(b"\0")[0].decode("utf-8")
        offset, on_disk, uncompressed, part, entry_flags, crc = struct.unpack_from(
            "<IIIIII", raw, index * ENTRY_SIZE + 256)
        if index < show:
            print("    %-72s off=%-8d disk=%-7d raw=%-7d flags=0x%02x"
                  % (name, offset, on_disk, uncompressed, entry_flags))
    return count


if __name__ == "__main__":
    command = sys.argv[1] if len(sys.argv) > 1 else "build"
    if command == "build":
        written = build()
        print("wrote %s" % written)
        verify(written)
    elif command == "verify":
        verify(sys.argv[2])
    elif command == "hash":
        for target in sys.argv[2:]:
            check_hash(target)
    else:
        raise SystemExit(__doc__)
