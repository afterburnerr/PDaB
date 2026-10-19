#!/usr/bin/env python3
"""Copy original Half-Life 2 HUD fonts from the user's local installation.

Only standard-library modules are required. The Steam installation is read-only.
HalfLife2 supplies HUD digits; DejaVu Sans Bold is bundled with Source on Linux
and is used here for labels whose scheme requests the unbundled Verdana font.
"""
import argparse
import hashlib
import json
from pathlib import Path


FONTS = (
    ("hl2/resource/halflife2.ttf", "HalfLife2.ttf", "HudNumbers", "HalfLife2"),
    (
        "platform/resource/linux_fonts/dejavusans-bold.ttf",
        "DejaVuSans-Bold.ttf",
        "AUX POWER and SPRINT labels; bundled Linux substitute for requested Verdana",
        "DejaVu Sans Bold",
    ),
)


def extract(installation: Path, project: Path) -> None:
    output = project / "Assets/Art/UI/HalfLife2/Fonts"
    output.mkdir(parents=True, exist_ok=True)
    fonts = []
    for source_relative, filename, purpose, family in FONTS:
        source = installation / source_relative
        data = source.read_bytes()
        destination = output / filename
        destination.write_bytes(data)
        fonts.append({
            "source": str(source),
            "asset": str(destination.relative_to(project)),
            "purpose": purpose,
            "family": family,
            "bytes": len(data),
            "sha256": hashlib.sha256(data).hexdigest(),
        })

    definitions = []
    for relative in (
        "hl2/resource/clientscheme.res",
        "hl2/scripts/hudlayout.res",
        "hl2/scripts/hudanimations.txt",
        "hl2/resource/valve_english.txt",
    ):
        source = installation / relative
        definitions.append({
            "source": str(source),
            "sha256": hashlib.sha256(source.read_bytes()).hexdigest(),
        })

    manifest = {
        "fonts": fonts,
        "definitions": definitions,
        "hudNumbers": {
            "schemeName": "HudNumbers",
            "family": "HalfLife2",
            "tall": 32,
            "additive": True,
            "digits": "0123456789",
            "colonWarning": "The colon glyph is an icon, not timer punctuation; use separate UI dots.",
        },
        "auxPower": {
            "coordinateSystem": "Source proportional 640 x 480, non-Deck layout",
            "panel": {"x": 16, "y": 420, "width": 102, "height": 48},
            "bar": {"x": 8, "y": 15, "width": 92, "height": 4, "chunkWidth": 6, "chunkGap": 3},
            "caption": {"x": 8, "y": 4, "text": "AUX POWER"},
            "activeDevice": {"x": 8, "y": 22, "gap": 10, "text": "SPRINT"},
            "captionFontRequested": "Default / Verdana bold",
            "captionFontImported": "DejaVu Sans Bold, bundled Source Linux substitute",
            "icon": None,
            "highColor": [255, 220, 0, 220],
            "lowColor": [255, 0, 0, 220],
            "disabledAlpha": 70,
            "lowThresholdPercent": 25,
            "colorTransitionSeconds": 0.4,
            "backgroundColor": [0, 0, 0, 76],
            "originalBehavior": "SuitAuxPowerMax fades panel/bar out over 0.4 seconds; not-max fades in.",
        },
    }
    (project / "Tools/Art/HalfLife2-hud.json").write_text(json.dumps(manifest, indent=2) + "\n")
    print("Imported original HL2 HUD fonts: " + ", ".join(font[1] for font in FONTS))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("installation", type=Path)
    arguments = parser.parse_args()
    extract(arguments.installation, Path(__file__).resolve().parents[2])
