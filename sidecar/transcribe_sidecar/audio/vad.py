"""Silero-VAD utterance segmentation (Phase 1).

Wraps silero-vad to cut each capture stream at silence boundaries and emit
complete utterances (with start/end offsets) for transcription.
"""
from __future__ import annotations

# TODO(Phase 1): load silero-vad once, feed 16 kHz mono frames, and yield
# (samples, t0, t1) per detected utterance using the vad_* settings in config.
