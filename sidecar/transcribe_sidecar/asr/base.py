"""Shared ASR types for the pluggable backends (Parakeet / Whisper)."""
from __future__ import annotations

from dataclasses import dataclass


@dataclass
class AsrSegment:
    text: str
    start: float           # seconds
    end: float
    language: str | None = None
