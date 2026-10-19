#!/usr/bin/env python3
"""Import the three UI WAVs from a local Half-Life 2 installation.

Uses Python's standard library; does not modify the Steam installation.
VPK layout reference: https://developer.valvesoftware.com/wiki/VPK_(file_format)
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct
import zlib


SOUNDS = ("buttonrollover.wav", "buttonclick.wav", "buttonclickrelease.wav")


def extract(installation: Path, project: Path) -> None:
    directory = installation / "hl2/hl2_sound_misc_dir.vpk"
    with directory.open("rb") as source:
        signature, version, tree_size = struct.unpack("<III", source.read(12))
        if signature != 0x55AA1234 or version not in (1, 2):
            raise ValueError("Expected a Source VPK version 1 or 2")
        if version == 2:
            source.read(16)
        header_size = source.tell()
        tree = source.read(tree_size)

    cursor = 0

    def read_string() -> str:
        nonlocal cursor
        end = tree.index(b"\0", cursor)
        value = tree[cursor:end].decode("utf-8")
        cursor = end + 1
        return value

    selected = {}
    while extension := read_string():
        while folder := read_string():
            while name := read_string():
                crc, preload_size, archive, offset, length, terminator = struct.unpack_from(
                    "<IHHIIH", tree, cursor
                )
                cursor += 18
                preload = tree[cursor:cursor + preload_size]
                cursor += preload_size
                if terminator != 0xFFFF:
                    raise ValueError("Invalid VPK directory entry")
                filename = name + "." + extension
                if folder.lower() == "sound/ui" and filename.lower() in SOUNDS:
                    selected[filename.lower()] = (crc, archive, offset, length, preload)

    output = project / "Assets/Audio/HalfLife2/UI"
    output.mkdir(parents=True, exist_ok=True)
    manifest = []
    for filename in SOUNDS:
        crc, archive, offset, length, preload = selected[filename]
        data_file = directory if archive == 0x7FFF else directory.with_name(
            directory.name.removesuffix("_dir.vpk") + f"_{archive:03}.vpk"
        )
        if archive == 0x7FFF:
            offset += header_size + tree_size
        with data_file.open("rb") as source:
            source.seek(offset)
            data = preload + source.read(length)
        if len(data) != len(preload) + length or zlib.crc32(data) != crc:
            raise ValueError("Incomplete or corrupt audio entry: " + filename)
        (output / filename).write_bytes(data)
        manifest.append({
            "sourceVpk": str(directory),
            "sourceEntry": "sound/ui/" + filename,
            "asset": str((output / filename).relative_to(project)),
            "bytes": len(data),
            "sha256": hashlib.sha256(data).hexdigest(),
        })

    (project / "Tools/Art/HalfLife2-menu-audio.json").write_text(
        json.dumps(manifest, indent=2) + "\n"
    )
    print("Imported original HL2 menu sounds: " + ", ".join(SOUNDS))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("installation", type=Path)
    arguments = parser.parse_args()
    extract(arguments.installation, Path(__file__).resolve().parents[2])
