"""Dual-stream audio capture: microphone ("Me") and system loopback ("Them").

Phase 1 implements `capture_stream`. `list_devices` is usable now to verify that
loopback capture is available on this machine.
"""
from __future__ import annotations

import logging
from dataclasses import dataclass
from typing import Iterator

import numpy as np

logger = logging.getLogger(__name__)


@dataclass
class AudioChunk:
    source: str          # "mic" (Me) or "loopback" (Them)
    samples: np.ndarray  # float32 mono at the target sample rate
    t0: float            # seconds since capture start


def list_devices() -> dict:
    """Enumerate input + loopback-capable output devices (validation helper)."""
    import soundcard as sc

    return {
        "microphones": [m.name for m in sc.all_microphones(include_loopback=False)],
        "loopback": [
            m.name for m in sc.all_microphones(include_loopback=True) if m.isloopback
        ],
        "default_speaker": sc.default_speaker().name,
        "default_mic": sc.default_microphone().name,
    }


def capture_stream(source: str, sample_rate: int, block_ms: int = 100) -> Iterator[AudioChunk]:
    """Yield resampled mono float32 chunks from the mic or the system loopback.

    TODO(Phase 1): implement with soundcard (loopback via include_loopback=True)
    and soxr for resampling to `sample_rate`.
    """
    raise NotImplementedError("Implemented in Phase 1.")
