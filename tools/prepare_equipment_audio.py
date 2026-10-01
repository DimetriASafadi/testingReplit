#!/usr/bin/env python3
"""Cook generated source effects into bounded, seamless Unity-compatible PCM clips."""
import array
import hashlib
import json
import math
import pathlib
import subprocess
import tempfile
import wave

ROOT = pathlib.Path(__file__).resolve().parents[1]
TARGET = ROOT / "testingReplic/Assets/NewGaza/Resources/Audio"
RATE = 24000
LOOPS = ("excavator_engine", "truck_engine", "dozer_engine", "hydraulics",
         "tracks", "wind_high", "coastal_surf")
ONESHOTS = ("ui_click", "ui_confirm")


def cook(key, temporary):
    source = ROOT / f"attached_assets/generated_audio/new-gaza-{key}.mp3"
    if not source.is_file():
        raise FileNotFoundError(source)
    decoded = temporary / f"{key}.wav"
    loudness = -28 if key in ("wind_high", "coastal_surf") else -23
    subprocess.run(
        ["ffmpeg", "-v", "error", "-y", "-i", str(source), "-af",
         f"loudnorm=I={loudness}:TP=-5:LRA=7", "-ar", str(RATE), "-ac", "1",
         "-c:a", "pcm_s16le", str(decoded)], check=True)
    with wave.open(str(decoded), "rb") as reader:
        assert reader.getnchannels() == 1 and reader.getsampwidth() == 2
        samples = array.array("h", reader.readframes(reader.getnframes()))
    if key in LOOPS:
        # Overlap the original tail with the head, then rotate the loop. Both seams
        # follow adjacent waveform samples; MP3 padding is not carried into Unity.
        overlap = int(RATE * .06)
        if len(samples) < overlap * 4:
            raise ValueError("Loop is too short: " + key)
        blend = array.array("h")
        for i in range(overlap):
            angle = i / (overlap - 1) * math.pi / 2
            value = samples[-overlap + i] * math.cos(angle) + samples[i] * math.sin(angle)
            blend.append(max(-32768, min(32767, round(value))))
        samples = samples[overlap:-overlap] + blend
    else:
        threshold = max(120, max(abs(value) for value in samples) * .015)
        audible = next((i for i, value in enumerate(samples) if abs(value) >= threshold), None)
        if audible is None:
            raise ValueError("Silent interface effect: " + key)
        samples = samples[max(0, audible - int(RATE * .004)):]
        for i in range(min(int(RATE * .02), len(samples))):
            samples[-1 - i] = round(samples[-1 - i] * i / (RATE * .02))
    if not samples or max(abs(value) for value in samples) < 100:
        raise ValueError("Effect has no audible signal: " + key)
    output = TARGET / f"{key}.wav"
    with wave.open(str(output), "wb") as writer:
        writer.setnchannels(1)
        writer.setsampwidth(2)
        writer.setframerate(RATE)
        writer.writeframes(samples.tobytes())
    peak = max(abs(value) for value in samples) / 32768
    rms = math.sqrt(sum((value / 32768) ** 2 for value in samples) / len(samples))
    if peak >= .99:
        raise ValueError("Clipping effect: " + key)
    return {
        "key": key, "loop": key in LOOPS, "sampleRate": RATE, "channels": 1,
        "durationSeconds": round(len(samples) / RATE, 4),
        "peak": round(peak, 6), "rms": round(rms, 6),
        "boundaryJump": abs(samples[-1] - samples[0]) / 32768 if key in LOOPS else None,
        "bytes": output.stat().st_size,
        "sha256": hashlib.sha256(output.read_bytes()).hexdigest(),
        "sourceSha256": hashlib.sha256(source.read_bytes()).hexdigest(),
    }


def main():
    TARGET.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="new-gaza-audio-") as directory:
        clips = [cook(key, pathlib.Path(directory)) for key in LOOPS + ONESHOTS]
    document = {
        "schema": 1, "provider": "ElevenLabs / Replit sound-effect generation",
        "description": "Original generated effects, not recordings of actual Gaza equipment.",
        "processing": "PCM16 mono 24kHz; loudness limited; equal-power loop seam crossfade.",
        "clips": clips,
    }
    (TARGET / "AudioManifest.json").write_text(json.dumps(document, indent=2) + "\n")
    print(json.dumps({"clips": len(clips), "totalBytes": sum(c["bytes"] for c in clips),
                      "maxBoundaryJump": max(c["boundaryJump"] or 0 for c in clips)}, indent=2))


if __name__ == "__main__":
    main()