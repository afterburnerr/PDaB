#!/usr/bin/env python3
"""Build the capture sting from original DSP and the locally extracted HL2 scream.
No external synthesis service/dependencies. Does not alter the source recording.
"""
from pathlib import Path
import hashlib
import json
import math
import random
import struct
import wave

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT/'Assets/Audio/Horror/Stalker/CaughtScream.wav'
OUTPUT = ROOT/'Assets/Audio/Horror/Stalker/CaptureSting.wav'
RATE = 44100
DURATION = 1.02
SEED = 19860426
rng = random.Random(SEED)
with wave.open(str(SOURCE)) as wav:
    assert wav.getsampwidth() == 2 and wav.getnchannels() == 1
    source_rate = wav.getframerate()
    scream = struct.unpack('<'+'h'*wav.getnframes(), wav.readframes(wav.getnframes()))
source_peak = max(abs(v) for v in scream)
count = round(RATE*DURATION)
low = bass_phase = metal_phase = 0.0
values = []
for index in range(count):
    t = index/RATE
    # Fast descending low impact, not subsonic energy.
    bass_phase += 2*math.pi*(54+112*math.exp(-t*24))/RATE
    impact = math.sin(bass_phase)*math.exp(-t*8)*0.78
    # Inharmonic, formant-like dissonance rises out of the attack, then decays.
    metal_phase += 2*math.pi*(610-260*min(t, 0.8))/RATE
    metal = (math.sin(metal_phase)+0.48*math.sin(metal_phase*1.417)+0.3*math.sin(metal_phase*2.31))
    metal *= math.exp(-t*4.4)*(0.32+0.12*math.sin(t*93))
    # Band-limited rasp: a one-pole low-pass prevents a white-noise ear blast.
    low += (1-math.exp(-2*math.pi*4200/RATE))*(rng.uniform(-1,1)-low)
    rasp = low*math.exp(-t*5.5)*0.33
    # The already imported HL2 scream is layered with a modest pitch drop.
    cursor = max(0.0, t-0.025)*source_rate*0.91
    lower = int(cursor)
    vocal = 0.0
    if t >= 0.025 and lower+1 < len(scream):
        fraction = cursor-lower
        vocal = (scream[lower]*(1-fraction)+scream[lower+1]*fraction)/source_peak
        vocal *= 0.92
    attack = min(1.0, t/0.005)
    release = min(1.0, (DURATION-t)/0.16)
    envelope = math.sin(attack*math.pi/2)**2*math.sin(release*math.pi/2)**2
    # Soft saturation contains the combined transient; normalize below full scale.
    values.append(math.tanh((impact+metal+rasp+vocal)*1.45)*envelope)
peak = max(abs(v) for v in values)
values = [value/peak*0.82 for value in values]
values[0] = values[-1] = 0.0
with wave.open(str(OUTPUT),'wb') as wav:
    wav.setnchannels(1); wav.setsampwidth(2); wav.setframerate(RATE)
    wav.writeframes(b''.join(struct.pack('<h',round(value*32767)) for value in values))
rms = math.sqrt(sum(value*value for value in values)/len(values))
manifest = dict(asset=str(OUTPUT.relative_to(ROOT)), durationSeconds=DURATION, channels=1, sampleBits=16, sampleRate=RATE,
    peak=0.82, rms=rms, generator='Tools/Art/GenerateStalkerCaptureSting.py', seed=SEED,
    synthesis='Original descending impact, inharmonic screech and low-pass-filtered rasp, soft saturation, 5ms attack and 160ms smooth release.',
    sampledLayer=dict(asset=str(SOURCE.relative_to(ROOT)), sourceEntry='sound/ambient/voices/m_scream1.wav',
        sourceGame='Local Half-Life 2 installation', sourceSha256=hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
        processing='Linear-resampled at 0.91 pitch, peak-normalized before mixing; original WAV is untouched.'),
    sha256=hashlib.sha256(OUTPUT.read_bytes()).hexdigest(),
    playback='One-shot 2D, volume 0.80, pitch 1, ignoreListenerPause, no distance attenuation. Runs on the same Caught state event as the still illustration.')
(ROOT/'Tools/Art/StalkerCaptureStingSource.json').write_text(json.dumps(manifest,indent=2)+'\n')
print(json.dumps(dict(path=str(OUTPUT),duration=DURATION,peak=0.82,rms=rms,sha256=manifest['sha256']),indent=2))
