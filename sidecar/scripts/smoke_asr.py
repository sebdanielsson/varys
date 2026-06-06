"""Phase 1 smoke test: transcribe a WAV/FLAC/MP3 file with Parakeet-TDT.

Usage:
    .\\.venv\\Scripts\\python.exe scripts\\smoke_asr.py path\\to\\audio.wav

Validates that transformers (git main) + the TDT model load and run on the GPU,
and that Swedish / English audio transcribes with timestamps.
"""
from __future__ import annotations

import sys
import time
from pathlib import Path

import numpy as np
import soundfile as sf

# Allow running as a loose script without installing the package.
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from transcribe_sidecar.asr.parakeet import ParakeetAsr  # noqa: E402
from transcribe_sidecar.config import get_settings  # noqa: E402


def main(argv: list[str]) -> int:
    if len(argv) < 2:
        print(__doc__)
        return 2

    audio, sr = sf.read(argv[1], dtype="float32", always_2d=False)
    if audio.ndim > 1:
        audio = audio.mean(axis=1)  # downmix to mono

    s = get_settings()
    asr = ParakeetAsr(model_id=s.model_id, device=s.device, sample_rate=sr)

    t = time.perf_counter()
    asr.load()
    print(f"model loaded in {time.perf_counter() - t:.1f}s")

    t = time.perf_counter()
    segments = asr.transcribe(audio, sample_rate=sr)
    print(f"transcribed {len(audio) / sr:.1f}s of audio in {time.perf_counter() - t:.1f}s\n")

    for seg in segments:
        print(f"[{seg.start:6.2f} -> {seg.end:6.2f}] {seg.text}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
