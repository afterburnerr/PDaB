#!/usr/bin/env python3
"""Extract the original HL2 drone identified in Citadel Pod Ride Ambience."""
import argparse
import hashlib
import io
import json
from pathlib import Path
import struct
import wave

from import_hl2_flashlight import read_entry


def extract(installation: Path, project: Path) -> None:
    vpk = installation / "hl2/hl2_sound_misc_dir.vpk"
    entry = "sound/ambient/levels/citadel/citadel_drone_loop6.wav"
    data, crc = read_entry(vpk, entry)
    with wave.open(io.BytesIO(data), "rb") as sound:
        frames, rate = sound.getnframes(), sound.getframerate()
        channels, bits = sound.getnchannels(), sound.getsampwidth() * 8
    loops = []
    offset = 12
    while offset + 8 <= len(data):
        tag, size = struct.unpack_from("<4sI", data, offset)
        payload = offset + 8
        if tag == b"smpl" and size >= 36:
            count = struct.unpack_from("<I", data, payload + 28)[0]
            for index in range(count):
                cue, kind, start, end, fraction, play_count = struct.unpack_from(
                    "<6I", data, payload + 36 + index * 24
                )
                loops.append({"type": kind, "startSample": start,
                              "endSampleInclusive": end, "playCount": play_count})
        offset = payload + size + size % 2
    if not any(loop["startSample"] == 0 and loop["endSampleInclusive"] == frames - 1
               for loop in loops):
        raise ValueError("Expected original loop markers to cover the complete WAV")
    asset = project / "Assets/Audio/HalfLife2/Ambience/CitadelPodRideAmbience.wav"
    asset.parent.mkdir(parents=True, exist_ok=True)
    asset.write_bytes(data)
    manifest = {
        "title": "Citadel Pod Ride Ambience",
        "sourceVpk": str(vpk), "sourceEntry": entry,
        "asset": str(asset.relative_to(project)), "bytes": len(data),
        "crc32": f"{crc:08x}", "sha256": hashlib.sha256(data).hexdigest(),
        "durationSeconds": frames / rate, "sampleFrames": frames,
        "sampleRate": rate, "channels": channels, "sampleBits": bits,
        "sourceLoopMarkers": loops,
        "conversion": "None; original stereo PCM WAV bytes and smpl loop markers preserved",
        "identification": {
            "referenceTitle": "Half Life 2 Citadel Pod Ride Ambience",
            "referenceUrl": "https://www.youtube.com/watch?v=NXuyrj_ghkc",
            "referenceDurationSeconds": 600,
            "method": "Normalized PCM cross-correlation against installed Citadel sounds; 22050 Hz channel-separated comparison over reference first 60 seconds",
            "correlationLeft": 0.4941, "correlationRight": 0.5934,
            "alignedLoopStartsSecondsLeft": [2.53048, 8.85773, 15.18490, 21.51220, 27.83941, 34.16671, 40.49397],
            "finding": "Reference contains this exact full-length repeating drone; peaks repeat at the original 6.327256-second duration. Reference may contain game DSP or other ambience; import uses original source only.",
            "confidence": "High for identification of the repeating drone"
        },
        "soundscapeScript": "hl2/scripts/soundscapes_citadel.txt",
        "soundscapes": ["d3_citadel.generic", "d3_citadel.generic_moody2"],
        "playback": "2D loop in main menu and pause menu",
        "volume": 0.3,
    }
    manifest_path = project / "Tools/Art/HalfLife2-citadel-pod-ride.json"
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n")
    print(json.dumps(manifest, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("installation", type=Path)
    extract(parser.parse_args().installation, Path(__file__).resolve().parents[2])
