"""Validate that system-loopback capture actually receives audio.

Plays a clip through the default speaker while capturing loopback ("Them"), then
reports the captured peak level. Audible for a few seconds.
"""
from __future__ import annotations

import queue
import sys
import threading
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import numpy as np  # noqa: E402
import soundfile as sf  # noqa: E402

from transcribe_sidecar.audio import capture  # noqa: E402
from transcribe_sidecar.config import get_settings  # noqa: E402


def _play(path: str) -> None:
    import ctypes

    import soundcard as sc

    ctypes.windll.ole32.CoInitializeEx(None, 0x0)
    audio, sr = sf.read(path, dtype="float32", always_2d=False)
    sc.default_speaker().play(audio, samplerate=sr)


def main() -> int:
    s = get_settings()
    q: queue.Queue = queue.Queue()
    stop = threading.Event()
    lb = capture.LoopbackCapture(q, stop, s.sample_rate)
    lb.start()
    threading.Thread(target=_play, args=("samples/test_en.wav",), daemon=True).start()

    peak = 0.0
    frames = 0
    t0 = time.perf_counter()
    while time.perf_counter() - t0 < 6.0:
        try:
            _src, samples = q.get(timeout=0.5)
        except queue.Empty:
            continue
        if samples is None:
            print("loopback capture error")
            return 1
        frames += len(samples)
        peak = max(peak, float(np.abs(samples).max()))

    stop.set()
    print(f"Them captured {frames / s.sample_rate:.2f}s, peak={peak:.4f}")
    print("PASS: loopback receives audio" if peak > 0.01 else "NO SIGNAL (check speaker/volume)")
    return 0 if peak > 0.01 else 2


if __name__ == "__main__":
    raise SystemExit(main())
