#!/usr/bin/env python3
"""Import four original residential HL2 wall albedos without image edits.

Reads the installed VPKs only; decodes each original diffuse top mip to RGB PNG.
Source detail textures and cubemap reflections are intentionally not baked into
these albedos. World scale is an explicit Unity recommendation: Source VMTs do
not encode brush UV scale, which is normally stored in the map geometry.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re

from PIL import Image, ImageDraw
from import_hl2_flashlight import read_entry
from import_hl2_wall_decals import decode_vtf

SELECTION = (
    ("plaster/plasterwall003a", "plaster", "Grey-beige weathered plaster; aged style upper wall"),
    ("plaster/plasterwall032a", "plaster", "Dirty off-white plaster with small chips; tiled style upper wall"),
    ("plaster/plasterwall042a", "paint", "Muted grey-green painted plaster; aged style lower wall"),
    ("tile/tilewall006b", "tile", "Old small square grey-beige tiles; tiled style lower wall"),
)


def extract(installation, project):
    texture_vpk = installation / "hl2/hl2_textures_dir.vpk"
    material_vpk = installation / "hl2/hl2_misc_dir.vpk"
    output = project / "Assets/Art/Environment/HalfLife2/WallSurfaces"
    output.mkdir(parents=True, exist_ok=True)
    manifest = []
    sheet = Image.new("RGB", (4 * 300, 336), (65, 65, 60))
    draw = ImageDraw.Draw(sheet)
    for index, (material_name, category, role) in enumerate(SELECTION):
        material_entry = "materials/" + material_name + ".vmt"
        material_bytes, material_crc = read_entry(material_vpk, material_entry)
        material_text = material_bytes.decode("utf-8-sig")
        clean = re.sub(r"//[^\n]*", "", material_text)
        texture_name = re.search(r'"?\$basetexture"?\s+"?([^"\s}]+)', clean, re.I)[1]
        entry = "materials/" + texture_name.lower() + ".vtf"
        data, crc = read_entry(texture_vpk, entry)
        image, original_format = decode_vtf(data)
        image = image.convert("RGB")
        name = material_name.rsplit("/", 1)[1]
        path = output / (name + ".png")
        image.save(path)
        manifest.append({
            "name": name, "category": category, "asset": str(path.relative_to(project)),
            "recommendedRole": role,
            "sourceVpk": str(texture_vpk), "sourceEntry": entry,
            "sourceCrc32": f"{crc:08x}", "sourceSha256": hashlib.sha256(data).hexdigest(),
            "sourceMaterialVpk": str(material_vpk), "sourceMaterialEntry": material_entry,
            "sourceMaterialCrc32": f"{material_crc:08x}",
            "sourceMaterialSha256": hashlib.sha256(material_bytes).hexdigest(),
            "sourceMaterialText": material_text,
            "convertedSha256": hashlib.sha256(path.read_bytes()).hexdigest(),
            "width": image.width, "height": image.height, "originalFormat": original_format,
            "conversion": "Decode original diffuse top mip to RGB PNG, preserving decoded RGB samples without recoloring, filtering, or baked lighting",
            "uvPerMetre": 0.5, "worldTextureScale": [2.0, 2.0],
            "scaleNote": "Recommended Unity scale: one full texture repeat per 2 metres on each axis. Source brush UV scale is map data, not supplied by these VMTs.",
            "sourceBaseTextureTransform": None,
            "materialNote": "Albedo only. Original Source detail maps and environment cubemap reflections are not baked into the RGB; Unity may derive a subtle normal from albedo.",
        })
        preview = image.copy()
        preview.thumbnail((288, 288))
        x = index * 300
        sheet.paste(preview, (x + (300 - preview.width) // 2, 34))
        draw.text((x + 8, 8), name + " (" + category + ")", fill="white")
    (project / "Tools/Art/HalfLife2-wall-surfaces.json").write_text(json.dumps({"assets": manifest}, indent=2) + "\n")
    sheet.save(project / "Tools/Art/HalfLife2-wall-surfaces-contact.jpg", quality=92)
    print(json.dumps({"count": len(manifest), "assets": [item["asset"] for item in manifest]}))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("installation", type=Path)
    args = parser.parse_args()
    extract(args.installation, Path(__file__).resolve().parents[2])
