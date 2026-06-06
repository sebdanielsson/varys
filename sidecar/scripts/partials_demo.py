"""Headless demo of the streaming partial/final logic over an audio file.

Feeds the file through the VAD segmenter on an audio-time clock, printing interim
partials and committed finals -- the same logic as live mode, minus real-time
capture. Validates streaming behavior without a mic.

Usage: python scripts/partials_demo.py samples/test_multi.wav [--lang sv]
"""
from __future__ import annotations

import argparse
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import numpy as np  # noqa: E402
import soundfile as sf  # noqa: E402
import soxr  # noqa: E402

from transcribe_sidecar.asr import make_backend  # noqa: E402
from transcribe_sidecar.audio.vad import StreamSegmenter  # noqa: E402
from transcribe_sidecar.config import get_settings  # noqa: E402


def main(argv=None) -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("file")
    ap.add_argument("--language", "--lang", dest="language", choices=["auto", "en", "sv"])
    a = ap.parse_args(argv)

    s = get_settings()
    if a.language:
        s = s.model_copy(update={"language": a.language})

    asr = make_backend(s)
    t = time.perf_counter()
    asr.load()
    print(f"{asr.label} loaded in {time.perf_counter() - t:.1f}s\n")

    audio, sr = sf.read(a.file, dtype="float32", always_2d=False)
    if audio.ndim > 1:
        audio = audio.mean(1)
    if sr != s.sample_rate:
        audio = soxr.resample(audio, sr, s.sample_rate).astype(np.float32)
        sr = s.sample_rate

    seg = StreamSegmenter("File", sample_rate=sr, threshold=s.vad_threshold,
                          min_silence_ms=s.vad_min_silence_ms, min_speech_ms=s.vad_min_speech_ms,
                          max_segment_s=s.vad_max_segment_s)
    block = int(sr * 0.1)
    fed = 0.0
    last = 0.0
    for i in range(0, len(audio), block):
        chunk = audio[i:i + block]
        fed += len(chunk) / sr
        for u in seg.push(chunk):
            res = asr.transcribe(u.samples)
            print(f"[FINAL] [{u.t0:6.2f} -> {u.t1:6.2f}] {res[0].text if res else ''}")
            last = 0.0
        if s.partial_interval_s > 0 and fed - last >= s.partial_interval_s:
            pend = seg.pending()
            if pend is not None:
                win = int(s.partial_window_s * sr)
                clip = pend.samples[-win:] if len(pend.samples) > win else pend.samples
                res = asr.transcribe(clip)
                if res and res[0].text:
                    print(f"    ~partial @ {fed:5.2f}s: {res[0].text}")
                last = fed

    tail = seg.flush()
    if tail:
        res = asr.transcribe(tail.samples)
        print(f"[FINAL] [{tail.t0:6.2f} -> {tail.t1:6.2f}] {res[0].text if res else ''}")


if __name__ == "__main__":
    main()
