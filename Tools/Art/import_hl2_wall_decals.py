#!/usr/bin/env python3
"""Extract selected original HL2 wall decals from the user's installed game.

Preserves the top mip's straight RGBA pixels and original alpha exactly. No AI,
background removal, recoloring, or invented transparency is applied. Never writes
into the Steam installation. Pillow decodes the original DXT payload losslessly
relative to its compressed source (the source itself is lossy DXT).
"""
import argparse
import hashlib
import io
import json
from pathlib import Path
import re
import struct

from PIL import Image, ImageDraw
from import_hl2_flashlight import read_entry

# Source material names, not arbitrary remapped texture variants.
SELECTION = {
    "graffiti": ["decalgraffiti" + x for x in (
        "001a", "001c", "001d", "005a", "007a", "008a", "015a", "018a",
        "020a", "022a", "028a", "031a", "036a", "046a", "061a")],
    "poster": ["decal_posters001a", "decal_posters003a", "bills01a", "bills02a", "bills04a", "bills05a"],
    "grime": ["decalstain" + x + "a" for x in ("001", "002", "006", "008", "009", "010", "011", "015")] + ["decalplaster002a"],
}


def image_size(width, height, fmt):
    if fmt in (13, 14, 15):
        return max(1, (width + 3) // 4) * max(1, (height + 3) // 4) * (8 if fmt == 13 else 16)
    return width * height * {0: 4, 2: 3, 3: 3, 12: 4}[fmt]


def decode_vtf(data):
    if data[:4] != b"VTF\0":
        raise ValueError("Expected VTF")
    major, minor, header_size = struct.unpack_from("<III", data, 4)
    width, height = struct.unpack_from("<HH", data, 16)
    flags, frames = struct.unpack_from("<IH", data, 20)
    fmt = struct.unpack_from("<I", data, 52)[0]
    mips = data[56]
    if major != 7 or frames != 1 or flags & 0x4000:
        raise ValueError("Expected a single-frame, single-face VTF7")
    if minor >= 3:
        offset = None
        for index in range(struct.unpack_from("<I", data, 68)[0]):
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
        image = Image.open(io.BytesIO(header + pixels)).convert("RGBA")
    else:
        mode = "RGBA" if fmt in (0, 12) else "RGB"
        raw = {0: "RGBA", 2: "RGB", 3: "BGR", 12: "BGRA"}[fmt]
        image = Image.frombytes(mode, (width, height), pixels, "raw", raw).convert("RGBA")
    return image, f"VTF{major}.{minor} format {fmt}, {mips} mips"


def extract(installation, project):
    texture_vpk = installation / "hl2/hl2_textures_dir.vpk"
    material_vpk = installation / "hl2/hl2_misc_dir.vpk"
    output = project / "Assets/Art/Environment/HalfLife2/WallDecals"
    output.mkdir(parents=True, exist_ok=True)
    manifest = []
    images = []
    for category, names in SELECTION.items():
        for name in names:
            vmt_entry = f"materials/decals/{name}.vmt"
            vmt_bytes, vmt_crc = read_entry(material_vpk, vmt_entry)
            vmt = vmt_bytes.decode("utf-8-sig")
            clean = re.sub(r"//[^\n]*", "", vmt)
            texture_name = re.search(r'"?\$basetexture"?\s+"?([^"\s}]+)', clean, re.I)[1]
            scale_match = re.search(r'"?\$decalscale"?\s+"?([.\d]+)', clean, re.I)
            scale = float(scale_match[1]) if scale_match else 0.25
            if not re.search(r'"?\$translucent"?\s+"?1', clean, re.I):
                raise ValueError("Selected decal is not Source alpha blended: " + name)
            source_entry = "materials/" + texture_name.lower() + ".vtf"
            data, crc = read_entry(texture_vpk, source_entry)
            image, original_format = decode_vtf(data)
            path = output / (name + ".png")
            image.save(path)
            alpha_min, alpha_max = image.getchannel("A").getextrema()
            record = {
                "name": name, "category": category,
                "asset": str(path.relative_to(project)),
                "sourceVpk": str(texture_vpk), "sourceEntry": source_entry,
                "sourceCrc32": f"{crc:08x}", "sourceSha256": hashlib.sha256(data).hexdigest(),
                "sourceMaterialVpk": str(material_vpk), "sourceMaterialEntry": vmt_entry,
                "sourceMaterialCrc32": f"{vmt_crc:08x}", "sourceMaterialSha256": hashlib.sha256(vmt_bytes).hexdigest(),
                "sourceMaterialText": vmt,
                "convertedSha256": hashlib.sha256(path.read_bytes()).hexdigest(),
                "width": image.width, "height": image.height, "originalFormat": original_format,
                "conversion": "Decode original top mip to straight RGBA PNG; preserve RGB and alpha, no color or transparency edits",
                "sourceShader": "LightmappedGeneric", "blendMode": "AlphaBlend",
                "hasAlpha": alpha_min < 255, "alphaRange": [alpha_min, alpha_max],
                "sourceDecalScale": scale,
                "worldWidth": round(image.width * scale * 0.0254, 4),
                "worldHeight": round(image.height * scale * 0.0254, 4),
                "worldSizeNote": "Original Source decal dimensions, texture pixels * $decalscale * 0.0254 metres per Source unit",
            }
            manifest.append(record)
            images.append((name, image))
    (project / "Tools/Art/HalfLife2-wall-decals.json").write_text(json.dumps({"assets": manifest}, indent=2) + "\n")
    columns, cell_w, cell_h = 6, 220, 230
    sheet = Image.new("RGB", (columns * cell_w, ((len(images) + columns - 1) // columns) * cell_h), (116, 116, 103))
    draw = ImageDraw.Draw(sheet)
    for i, (name, image) in enumerate(images):
        preview = image.copy()
        preview.thumbnail((cell_w - 16, cell_h - 30))
        x, y = (i % columns) * cell_w, (i // columns) * cell_h
        sheet.paste(preview, (x + (cell_w - preview.width) // 2, y + 24), preview)
        draw.text((x + 5, y + 5), name, fill="white")
    sheet.save(project / "Tools/Art/HalfLife2-wall-decals-contact.jpg", quality=90)
    print(json.dumps({"count": len(manifest), "categories": {key: len(value) for key, value in SELECTION.items()}, "allAlpha": all(item["hasAlpha"] for item in manifest)}))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("installation", type=Path)
    args = parser.parse_args()
    extract(args.installation, Path(__file__).resolve().parents[2])
