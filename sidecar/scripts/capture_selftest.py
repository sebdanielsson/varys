"""Quick check that mic + loopback capture threads work (no model, ~2 s).

Reports blocks captured and peak level per source. A near-zero 'Them' peak just
means nothing was playing through the speakers during the test; what matters is
that neither stream errors out.
"""
from __future__ import annotations

import queue
import sys
import threading
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import numpy as np  # noqa: E402

from transcribe_sidecar.audio import capture  # noqa: E402
from transcribe_sidecar.config import get_settings  # noqa: E402


def main() -> int:
    s = get_settings()
    q: queue.Queue = queue.Queue()
    stop = threading.Event()
    print(f"Me   <- {capture.mic_name()}")
    print(f"Them <- {capture.loopback_name()}")

    threads = [
        capture.MicCapture(q, stop, s.sample_rate),
        capture.LoopbackCapture(q, stop, s.sample_rate),
    ]
    for t in threads:
        t.start()

    blocks = {"Me": 0, "Them": 0}
    frames = {"Me": 0, "Them": 0}
    peak = {"Me": 0.0, "Them": 0.0}
    errors = []
    t0 = time.perf_counter()
    while time.perf_counter() - t0 < 2.0:
        try:
            source, samples = q.get(timeout=0.5)
        except queue.Empty:
            continue
        if samples is None:
            errors.append(source)
            continue
        blocks[source] += 1
        frames[source] += len(samples)
        peak[source] = max(peak[source], float(np.abs(samples).max()))

    stop.set()
    for t in threads:
        t.join(timeout=2)

    for src in ("Me", "Them"):
        print(f"{src}: {blocks[src]} blocks, {frames[src] / s.sample_rate:.2f}s, peak={peak[src]:.4f}")
    if errors:
        print(f"CAPTURE ERROR on: {', '.join(errors)}")
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
