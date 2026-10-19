#!/usr/bin/env python3
"""Create quiet, original positional radio crackles. No game audio is sampled."""
from pathlib import Path
import hashlib
import json
import math
import random
import struct
import wave

ROOT = Path(__file__).resolve().parents[2]
RATE = 22050
manifest = []
for index, duration in enumerate((0.18, 0.28, 0.38, 0.48), 1):
    seed = 19031986 + index
    rng = random.Random(seed)
    length = round(duration * RATE)
    lp = dc = 0.0
    values = []
    # Two one-pole filters bound the noise to roughly 450–4500 Hz.
    low_coefficient = 1 - math.exp(-2 * math.pi * 4500 / RATE)
    high_coefficient = 1 - math.exp(-2 * math.pi * 450 / RATE)
    for sample in range(length):
        t = sample / RATE
        lp += low_coefficient * (rng.uniform(-1, 1) - lp)
        dc += high_coefficient * (lp - dc)
        attack = min(1.0, t / 0.018)
        release = min(1.0, (duration - t) / 0.06)
        envelope = math.sin(attack * math.pi / 2) ** 2 * math.sin(release * math.pi / 2) ** 2
        modulation = 0.23 + 0.77 * math.sin(t * (37 + index * 11) + index) ** 6
        values.append((lp - dc) * envelope * modulation)
    peak = max(abs(value) for value in values)
    values = [value / peak * 0.42 for value in values]
    values[0] = values[-1] = 0.0
    output = ROOT / f'Assets/Audio/Horror/Stalker/Static{index:02d}.wav'
    output.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(output), 'wb') as wav:
        wav.setnchannels(1)
        wav.setsampwidth(2)
        wav.setframerate(RATE)
        wav.writeframes(b''.join(struct.pack('<h', round(value * 32767)) for value in values))
    manifest.append(dict(asset=str(output.relative_to(ROOT)), source='Original deterministic procedural DSP; no HL2 samples',
        generator='Tools/Art/GenerateStalkerStatic.py', seed=seed, sampleRate=RATE, channels=1, sampleBits=16,
        durationSeconds=length/RATE, peak=0.42, envelope='18ms smooth attack, 60ms smooth release',
        synthesis='Band-limited filtered random noise with irregular amplitude modulation; approximately 450–4500Hz',
        sha256=hashlib.sha256(output.read_bytes()).hexdigest()))
(ROOT/'Tools/Art/StalkerStaticSources.json').write_text(json.dumps(manifest, indent=2)+'\n')
print('Generated four original mono radio crackles (0.18–0.48 seconds).')
