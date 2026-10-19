#!/usr/bin/env python3
"""Copy local HL2 Citadel voices for the stalker's directional murmurs.
Source WAV bytes remain unchanged; Unity applies spatialization and filtering.
"""
import hashlib
import io
import json
from pathlib import Path
import sys
import wave
sys.path.insert(0, str(Path(__file__).resolve().parent))
from import_hl2_flashlight import read_entry

project = Path(__file__).resolve().parents[2]
vpk = Path.home() / '.local/share/Steam/steamapps/common/Half-Life 2/hl2/hl2_sound_misc_dir.vpk'
assets = [('ambient/levels/citadel/citadel_ambient_voices1.wav', 'DistantMurmurs.wav')]
assets += [(f'ambient/levels/citadel/strange_talk{i}.wav', f'Mutter{i:02d}.wav') for i in (1,3,4,5,6,7,8,9,10,11)]
assets += [('ambient/voices/m_scream1.wav', 'CaughtScream.wav')]
manifest = []
for entry, name in assets:
    data, crc = read_entry(vpk, 'sound/' + entry)
    output = project / 'Assets/Audio/Horror/Stalker' / name
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(data)
    with wave.open(io.BytesIO(data)) as sound:
        info = dict(duration=sound.getnframes()/sound.getframerate(), channels=sound.getnchannels(), sampleRate=sound.getframerate(), sampleBits=sound.getsampwidth()*8)
    manifest.append(dict(asset=str(output.relative_to(project)), sourceVpk=str(vpk), sourceEntry='sound/'+entry, crc32=f'{crc:08x}', sha256=hashlib.sha256(data).hexdigest(), processing='Original WAV bytes; positional playback, low-pass filtering and gain are applied by Unity.', **info))
(project / 'Tools/Art/StalkerAudioSources.json').write_text(json.dumps(manifest, indent=2)+'\n')
print(json.dumps(manifest, indent=2))
