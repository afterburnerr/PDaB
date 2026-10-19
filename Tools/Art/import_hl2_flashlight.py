#!/usr/bin/env python3
"""Import Half-Life 2's flashlight click and decode its original light cookie.

Uses only Python's standard library; never modifies the Steam installation.
The installed flashlight VTFs are single-frame BGR888 with mips (7.1 and 7.3).
The PNGs retain the top mip's RGB pixels exactly, without an invented alpha.
"""
import argparse
import hashlib
import io
import json
from pathlib import Path
import struct
import wave
import zlib


def read_entry(directory: Path, wanted: str) -> tuple[bytes, int]:
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

    while extension := read_string():
        while folder := read_string():
            while name := read_string():
                crc, preload_size, archive, offset, length, terminator = struct.unpack_from("<IHHIIH", tree, cursor)
                cursor += 18
                preload = tree[cursor:cursor + preload_size]
                cursor += preload_size
                if terminator != 0xFFFF:
                    raise ValueError("Invalid VPK directory entry")
                if f"{folder}/{name}.{extension}".lower() != wanted.lower():
                    continue
                data_file = directory if archive == 0x7FFF else directory.with_name(
                    directory.name.removesuffix("_dir.vpk") + f"_{archive:03}.vpk"
                )
                if archive == 0x7FFF:
                    offset += header_size + tree_size
                with data_file.open("rb") as source:
                    source.seek(offset)
                    data = preload + source.read(length)
                if len(data) != len(preload) + length or zlib.crc32(data) != crc:
                    raise ValueError("Incomplete or corrupt VPK entry: " + wanted)
                return data, crc
    raise FileNotFoundError(wanted + " in " + str(directory))


def cookie_png(data: bytes) -> tuple[bytes, int, int]:
    if data[:4] != b"VTF\0":
        raise ValueError("Expected VTF")
    major, minor, header_size = struct.unpack_from("<III", data, 4)
    width, height = struct.unpack_from("<HH", data, 16)
    flags = struct.unpack_from("<I", data, 20)[0]
    frames = struct.unpack_from("<H", data, 24)[0]
    image_format = struct.unpack_from("<I", data, 52)[0]
    mip_count = data[56]
    thumbnail_format = struct.unpack_from("<I", data, 57)[0]
    thumbnail_width, thumbnail_height = data[61:63]
    if major != 7 or minor not in (1, 3) or (frames, image_format, thumbnail_format) != (1, 3, 13) or flags & 0x4000:
        raise ValueError("Expected the installed single-face VTF7.1/7.3 BGR888 cookies with DXT1 thumbnails")
    thumbnail_bytes = max(1, (thumbnail_width + 3) // 4) * max(1, (thumbnail_height + 3) // 4) * 8
    offset = header_size + thumbnail_bytes
    if minor == 3:
        resource_count = struct.unpack_from("<I", data, 68)[0]
        image_offset = None
        for index in range(resource_count):
            entry = 80 + index * 8
            if data[entry:entry + 3] == b"\x30\0\0" and data[entry + 3] == 0:
                image_offset = struct.unpack_from("<I", data, entry + 4)[0]
        if image_offset is None:
            raise ValueError("VTF7.3 image resource is missing")
        offset = image_offset
    # VTF stores high-resolution mips smallest first; skip all but mip zero.
    for level in range(mip_count - 1, 0, -1):
        offset += max(1, width >> level) * max(1, height >> level) * 3
    bgr = data[offset:offset + width * height * 3]
    if len(bgr) != width * height * 3:
        raise ValueError("Incomplete VTF top mip")
    rgb = bytearray(len(bgr))
    rgb[0::3], rgb[1::3], rgb[2::3] = bgr[2::3], bgr[1::3], bgr[0::3]
    rows = b"".join(b"\0" + rgb[y * width * 3:(y + 1) * width * 3] for y in range(height))

    def chunk(kind: bytes, payload: bytes) -> bytes:
        return struct.pack(">I", len(payload)) + kind + payload + struct.pack(">I", zlib.crc32(kind + payload))

    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
    return png + chunk(b"IDAT", zlib.compress(rows)) + chunk(b"IEND", b""), width, height


def extract(installation: Path, project: Path) -> None:
    manifest = []
    audio_vpk = installation / "hl2/hl2_sound_misc_dir.vpk"
    audio_entry = "sound/items/flashlight1.wav"
    audio, crc = read_entry(audio_vpk, audio_entry)
    audio_path = project / "Assets/Audio/HalfLife2/Flashlight/flashlight1.wav"
    audio_path.parent.mkdir(parents=True, exist_ok=True)
    audio_path.write_bytes(audio)
    with wave.open(io.BytesIO(audio), "rb") as wav:
        audio_info = {"durationSeconds": wav.getnframes() / wav.getframerate(), "sampleRate": wav.getframerate(),
                      "channels": wav.getnchannels(), "sampleBits": wav.getsampwidth() * 8}
    manifest.append({"sourceVpk": str(audio_vpk), "sourceEntry": audio_entry,
                     "asset": str(audio_path.relative_to(project)), "bytes": len(audio), "crc32": f"{crc:08x}",
                     "sha256": hashlib.sha256(audio).hexdigest(), **audio_info,
                     "soundScript": "hl2/scripts/game_sounds_player.txt",
                     "soundEvents": ["HL2Player.FlashLightOn", "HL2Player.FlashLightOff"], "scriptVolume": 0.8})

    texture_vpk = installation / "hl2/hl2_textures_dir.vpk"
    texture_folder = project / "Assets/Art/UI/HalfLife2/Flashlight"
    texture_folder.mkdir(parents=True, exist_ok=True)
    for name in ("flashlight_border", "flashlight001"):
        texture_entry = f"materials/effects/{name}.vtf"
        vtf, crc = read_entry(texture_vpk, texture_entry)
        png, width, height = cookie_png(vtf)
        original_path = texture_folder / f"{name}.vtf"
        png_path = texture_folder / f"{name}.png"
        original_path.write_bytes(vtf)
        png_path.write_bytes(png)
        major, minor = struct.unpack_from("<II", vtf, 4)
        manifest.append({"sourceVpk": str(texture_vpk), "sourceEntry": texture_entry,
                         "originalAsset": str(original_path.relative_to(project)), "bytes": len(vtf),
                         "crc32": f"{crc:08x}", "sha256": hashlib.sha256(vtf).hexdigest(),
                         "asset": str(png_path.relative_to(project)), "convertedSha256": hashlib.sha256(png).hexdigest(),
                         "width": width, "height": height, "originalFormat": f"VTF{major}.{minor} BGR888, {vtf[56]} mips",
                         "conversion": "Decode mip zero to lossless RGB PNG; preserve pixel order, RGB and no-alpha semantics",
                         "material": None,
                         "materialNote": "No flashlight VMT exists in the installed hl2 directory VPKs; cookie is a texture asset."})
    (project / "Tools/Art/HalfLife2-flashlight.json").write_text(json.dumps(manifest, indent=2) + "\n")
    print(f"Imported original HL2 flashlight click ({audio_info['durationSeconds']:.3f}s) and both original cookies.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("installation", type=Path)
    arguments = parser.parse_args()
    extract(arguments.installation, Path(__file__).resolve().parents[2])
