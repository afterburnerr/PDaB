#!/usr/bin/env python3
"""Copy the original HL2 radio track unchanged from the user's local VPK."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess

from import_hl2_flashlight import read_entry


def extract(installation: Path, project: Path) -> None:
    vpk = installation / "hl2/hl2_sound_misc_dir.vpk"
    entry = "sound/music/radio1.mp3"
    data, crc = read_entry(vpk, entry)
    asset = project / "Assets/Audio/HalfLife2/Ambience/DistortedTrumpets.mp3"
    asset.parent.mkdir(parents=True, exist_ok=True)
    asset.write_bytes(data)
    probe = json.loads(subprocess.check_output([
        "ffprobe", "-v", "error", "-show_streams", "-show_format", "-of", "json", str(asset)
    ]))
    audio = probe["streams"][0]
    manifest = {
        "title": "Distorted Trumpets (Half-Life 2 radio1)",
        "sourceVpk": str(vpk), "sourceEntry": entry,
        "asset": str(asset.relative_to(project)), "bytes": len(data),
        "crc32": f"{crc:08x}", "sha256": hashlib.sha256(data).hexdigest(),
        "durationSeconds": float(probe["format"]["duration"]),
        "sampleRate": int(audio["sample_rate"]), "channels": audio["channels"],
        "conversion": "None; original MP3 bytes preserved, no pitch or audio effects",
        "playback": "2D loop during gameplay; pauses with session and resumes at same position",
        "volume": 0.2,
    }
    (project / "Tools/Art/HalfLife2-distorted-trumpets.json").write_text(
        json.dumps(manifest, indent=2) + "\n"
    )
    print(json.dumps(manifest, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("installation", type=Path)
    extract(parser.parse_args().installation, Path(__file__).resolve().parents[2])
