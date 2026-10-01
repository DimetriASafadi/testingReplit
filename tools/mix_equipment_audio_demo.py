#!/usr/bin/env python3
"""Mix the exported CityAudioMix envelope with its authored WAV resources.

This is a deterministic offline approximation of the source mixer, not Unity audio
playback. The fixture exports the runtime gain envelope; this script interpolates
those gains at 24 kHz and applies equal-power stereo panning.
"""

import argparse
import json
import math
import struct
import wave
from array import array
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SAMPLE_RATE = 24000
CHANNELS = 2
SECONDS = 15.0
STOP_THRESHOLD = 0.0015
ENVELOPE_INTERVAL = 0.02

VOICE_SPECS = (
    ("excavator_engine", "excavatorGain", "excavatorPan", True),
    ("truck_engine", "truckGain", "truckPan", True),
    ("dozer_engine", "dozerGain", "dozerPan", True),
    ("hydraulics", "hydraulicsGain", "hydraulicsPan", True),
    ("tracks", "tracksGain", "tracksPan", True),
    ("wind_high", "windGain", "windPan", True),
    ("coastal_surf", "surfGain", "surfPan", True),
)


def cli_arguments():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--envelope",
        type=Path,
        default=ROOT / "testingReplic/Tests/Audio/exports/audio/Camera-Audio-Demo-Envelope.json",
        help="Fixture-exported Camera-Audio-Demo-Envelope.json",
    )
    parser.add_argument(
        "--audio-dir",
        type=Path,
        default=ROOT / "testingReplic/Assets/NewGaza/Resources/Audio",
        help="Authored PCM16 mono WAV resources",
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=ROOT / "exports/audio/Camera-Audio-Demo.wav",
        help="Output stereo PCM16 WAV",
    )
    return parser.parse_args()


def load_clip(path):
    if not path.is_file() or path.stat().st_size == 0:
        raise FileNotFoundError(f"Required authored audio clip is missing or empty: {path}")
    with wave.open(str(path), "rb") as source:
        if (
            source.getframerate() != SAMPLE_RATE
            or source.getnchannels() != 1
            or source.getsampwidth() != 2
            or source.getcomptype() != "NONE"
        ):
            raise ValueError(f"{path} must be uncompressed mono PCM16 at {SAMPLE_RATE} Hz.")
        frames = source.readframes(source.getnframes())
    pcm = array("h")
    pcm.frombytes(frames)
    if struct.pack("=H", 1) != struct.pack("<H", 1):
        pcm.byteswap()
    if not pcm:
        raise ValueError(f"Authored audio clip contains no PCM samples: {path}")
    return [sample / 32768.0 for sample in pcm]


def interpolated_field(frames, field, frame_index):
    coordinate = frame_index / (SAMPLE_RATE * ENVELOPE_INTERVAL)
    left = min(int(coordinate), len(frames) - 1)
    right = min(left + 1, len(frames) - 1)
    fraction = coordinate - left
    return float(frames[left][field]) * (1.0 - fraction) + float(frames[right][field]) * fraction


def equal_power_pan(pan):
    pan = max(-1.0, min(1.0, pan))
    angle = (pan + 1.0) * math.pi * 0.25
    return math.cos(angle), math.sin(angle)


def loop_sample(clip, phase):
    index = phase % len(clip)
    left = int(index)
    right = (left + 1) % len(clip)
    fraction = index - left
    return clip[left] * (1.0 - fraction) + clip[right] * fraction


def calculate_rms(squared_sum, sample_count):
    return math.sqrt(squared_sum / sample_count) if sample_count else 0.0


def mix_demo(envelope_path, audio_dir, output_path):
    if not envelope_path.is_file() or envelope_path.stat().st_size == 0:
        raise FileNotFoundError(f"Required source mixer envelope is missing or empty: {envelope_path}")
    frames = json.loads(envelope_path.read_text(encoding="utf-8"))
    expected_frames = round(SECONDS / ENVELOPE_INTERVAL) + 1
    if len(frames) != expected_frames or abs(float(frames[-1]["t"]) - SECONDS) > 1e-4:
        raise ValueError(
            f"Expected {expected_frames} source envelope samples ending at {SECONDS:g}s; "
            f"received {len(frames)}."
        )
    required_fields = {
        "t",
        "excavatorGain",
        "truckGain",
        "dozerGain",
        "hydraulicsGain",
        "tracksGain",
        "windGain",
        "surfGain",
        "excavatorPan",
        "truckPan",
        "dozerPan",
        "hydraulicsPan",
        "tracksPan",
        "windPan",
        "surfPan",
    }
    if not required_fields.issubset(frames[0]):
        raise ValueError(f"Source envelope is missing fields: {sorted(required_fields - set(frames[0]))}")
    for index, frame in enumerate(frames):
        if abs(float(frame["t"]) - index * ENVELOPE_INTERVAL) > 1e-4:
            raise ValueError(f"Envelope timestamps are not 20 ms apart at sample {index}.")
        for key, value in frame.items():
            if not math.isfinite(float(value)):
                raise ValueError(f"Non-finite source mixer value at envelope sample {index}: {key}.")

    clips = {
        name: load_clip(audio_dir / f"{name}.wav")
        for name, _, _, _ in VOICE_SPECS
    }
    clips["ui_click"] = load_clip(audio_dir / "ui_click.wav")
    clips["ui_confirm"] = load_clip(audio_dir / "ui_confirm.wav")

    sample_count = int(SAMPLE_RATE * SECONDS)
    left_mix = [0.0] * sample_count
    right_mix = [0.0] * sample_count
    loop_starts = {name: None for name, _, _, _ in VOICE_SPECS}
    wide_square_sum = close_square_sum = 0.0
    wide_samples = close_samples = 0
    peak = 0.0

    # CityAudio.PlayInterface's default output is master(.68) * interface(.52) *
    # MaximumUiGain(.8), with the click's authored .72 scale and full confirm scale.
    ui_channel_gain = 0.68 * 0.52 * 0.8
    ui_events = (
        (SAMPLE_RATE, clips["ui_click"], ui_channel_gain * 0.72),
        (13 * SAMPLE_RATE, clips["ui_confirm"], ui_channel_gain),
    )
    for start, clip, gain in ui_events:
        end = min(sample_count, start + len(clip))
        for index in range(start, end):
            value = clip[index - start] * gain
            left_mix[index] += value
            right_mix[index] += value

    for name, gain_field, pan_field, is_loop in VOICE_SPECS:
        clip = clips[name]
        started_at = None
        for index in range(sample_count):
            gain = max(0.0, interpolated_field(frames, gain_field, index))
            if gain <= STOP_THRESHOLD:
                started_at = None
                loop_starts[name] = None
                continue
            if started_at is None:
                started_at = index
                loop_starts[name] = index
            pan = interpolated_field(frames, pan_field, index)
            left_pan, right_pan = equal_power_pan(pan)
            if is_loop:
                value = loop_sample(clip, index - started_at) * gain
            else:
                value = 0.0
            left_mix[index] += value * left_pan
            right_mix[index] += value * right_pan

    # Check the unprocessed sum before PCM conversion: no normalization, limiter,
    # or silent clipping fallback is permitted for this source-level demo.
    for index, (left, right) in enumerate(zip(left_mix, right_mix)):
        frame_peak = max(abs(left), abs(right))
        peak = max(peak, frame_peak)
        if frame_peak > 1.0:
            raise ValueError(
                f"Source-level mix clips at {index / SAMPLE_RATE:.3f}s "
                f"(peak {frame_peak:.6f}); refusing to normalize or clip it."
            )
        time = index / SAMPLE_RATE
        power = left * left + right * right
        if 0.0 <= time < 3.0:
            wide_square_sum += power
            wide_samples += 2
        elif 6.0 <= time < 10.0:
            close_square_sum += power
            close_samples += 2

    output_path.parent.mkdir(parents=True, exist_ok=True)
    pcm = array("h")
    for left, right in zip(left_mix, right_mix):
        pcm.append(max(-32768, min(32767, int(left * 32767.0))))
        pcm.append(max(-32768, min(32767, int(right * 32767.0))))
    if struct.pack("=H", 1) != struct.pack("<H", 1):
        pcm.byteswap()
    with wave.open(str(output_path), "wb") as target:
        target.setnchannels(CHANNELS)
        target.setsampwidth(2)
        target.setframerate(SAMPLE_RATE)
        target.writeframes(pcm.tobytes())

    wide_rms = calculate_rms(wide_square_sum, wide_samples)
    close_rms = calculate_rms(close_square_sum, close_samples)
    print(f"Wrote {output_path} ({SECONDS:.1f}s, {SAMPLE_RATE} Hz stereo PCM16).")
    print(
        "Source-math offline mix; no Unity playback, normalization, or limiter. "
        f"Unprocessed peak={peak:.6f}; 0–3s wide-view RMS={wide_rms:.6f}; "
        f"6–10s close-focus RMS={close_rms:.6f}; "
        f"wide/close RMS ratio={wide_rms / close_rms if close_rms else 0.0:.3f}."
    )


if __name__ == "__main__":
    arguments = cli_arguments()
    mix_demo(arguments.envelope, arguments.audio_dir, arguments.output)