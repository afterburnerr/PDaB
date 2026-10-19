#!/usr/bin/env python3
"""Decode local HL2 diffuse textures as image-generation references only.

These files stay under Tools, never Assets: all production environment textures
must be generated separately. Uses installed Pillow and the existing VPK reader.
"""
import argparse
import hashlib
import io
import json
from pathlib import Path
import struct

from PIL import Image
from import_hl2_flashlight import read_entry


REFERENCES = (
    "plaster/plasterwall034c",
    "plaster/plasterwall034a",
    "concrete/concretefloor013a",
    "tile/tilefloor012a",
    "metal/metaldoor005a",
)


def image_size(width: int, height: int, fmt: int) -> int:
    if fmt in (13, 14, 15):
        return max(1, (width + 3) // 4) * max(1, (height + 3) // 4) * (8 if fmt == 13 else 16)
    return width * height * {0: 4, 2: 3, 3: 3, 12: 4}[fmt]


def decode_vtf(data: bytes) -> Image.Image:
    if data[:4] != b"VTF\0":
        raise ValueError("Expected VTF")
    major, minor, header_size = struct.unpack_from("<III", data, 4)
    width, height = struct.unpack_from("<HH", data, 16)
    flags, frames = struct.unpack_from("<IH", data, 20)
    fmt = struct.unpack_from("<I", data, 52)[0]
    mips = data[56]
    if major != 7 or frames != 1 or flags & 0x4000:
        raise ValueError("Reference decoder expects a single-frame, single-face VTF7")
    if minor >= 3:
        resources = struct.unpack_from("<I", data, 68)[0]
        offset = None
        for index in range(resources):
            entry = 80 + index * 8
            if data[entry:entry + 3] == b"\x30\0\0" and data[entry + 3] == 0:
                offset = struct.unpack_from("<I", data, entry + 4)[0]
        if offset is None:
            raise ValueError("VTF image resource missing")
    else:
        thumb_fmt = struct.unpack_from("<I", data, 57)[0]
        tw, th = data[61:63]
        offset = header_size + (image_size(tw, th, thumb_fmt) if tw and th else 0)
    for level in range(mips - 1, 0, -1):
        offset += image_size(max(1, width >> level), max(1, height >> level), fmt)
    pixels = data[offset:offset + image_size(width, height, fmt)]
    if len(pixels) != image_size(width, height, fmt):
        raise ValueError("Incomplete top mip")
    if fmt in (13, 14, 15):
        header = bytearray(128)
        header[:4] = b"DDS "
        struct.pack_into("<7I", header, 4, 124, 0x81007, height, width, len(pixels), 0, 1)
        struct.pack_into("<II", header, 76, 32, 4)
        header[84:88] = {13: b"DXT1", 14: b"DXT3", 15: b"DXT5"}[fmt]
        struct.pack_into("<I", header, 108, 0x1000)
        return Image.open(io.BytesIO(header + pixels)).convert("RGB")
    mode = "RGBA" if fmt in (0, 12) else "RGB"
    raw = {0: "RGBA", 2: "RGB", 3: "BGR", 12: "BGRA"}[fmt]
    return Image.frombytes(mode, (width, height), pixels, "raw", raw).convert("RGB")


def extract(installation: Path, project: Path, references: list[str]) -> None:
    vpk = installation / "hl2/hl2_textures_dir.vpk"
    output = project / "Tools/Art/References/HL2"
    output.mkdir(parents=True, exist_ok=True)
    manifest = []
    for reference in references:
        entry = "materials/" + reference + ".vtf"
        data, crc = read_entry(vpk, entry)
        image = decode_vtf(data)
        path = output / (reference.replace("/", "_") + ".png")
        image.save(path)
        manifest.append({"sourceVpk": str(vpk), "sourceEntry": entry,
                         "reference": str(path.relative_to(project)), "width": image.width, "height": image.height,
                         "crc32": f"{crc:08x}", "sourceSha256": hashlib.sha256(data).hexdigest(),
                         "referenceSha256": hashlib.sha256(path.read_bytes()).hexdigest(),
                         "purpose": "Imagegen visual reference only; not a production game asset",
                         "conversion": "Original diffuse top mip decoded to RGB PNG without lighting or color edits"})
    (output / "provenance.json").write_text(json.dumps(manifest, indent=2) + "\n")
    print("Reference-only PNGs: " + ", ".join(item["reference"] for item in manifest))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("installation", type=Path)
    parser.add_argument("references", nargs="*")
    args = parser.parse_args()
    extract(args.installation, Path(__file__).resolve().parents[2], args.references or REFERENCES)
