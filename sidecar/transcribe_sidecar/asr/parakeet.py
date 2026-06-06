"""nvidia/parakeet-tdt-0.6b-v3 via Hugging Face Transformers.

TDT decoding currently requires `transformers` installed from source (git main);
it is not yet in a stable release. Language is auto-detected, no NeMo required.
"""
from __future__ import annotations

import logging

import numpy as np

from .base import AsrSegment

logger = logging.getLogger(__name__)


class ParakeetAsr:
    def __init__(self, model_id: str, device: str = "auto", sample_rate: int = 16000):
        self.model_id = model_id
        self.sample_rate = sample_rate
        self._device = device
        self._pipe = None

    @property
    def label(self) -> str:
        return "Parakeet (auto)"

    def load(self) -> None:
        import torch
        from transformers import pipeline

        if self._device == "auto":
            self._device = "cuda" if torch.cuda.is_available() else "cpu"
        dtype = torch.float16 if self._device == "cuda" else torch.float32
        logger.info("Loading %s on %s (%s)", self.model_id, self._device, dtype)
        self._pipe = pipeline(
            task="automatic-speech-recognition",
            model=self.model_id,
            device=0 if self._device == "cuda" else -1,
            dtype=dtype,
        )

    def transcribe(self, audio: np.ndarray, sample_rate: int | None = None) -> list[AsrSegment]:
        if self._pipe is None:
            raise RuntimeError("ParakeetAsr.load() must be called before transcribe().")
        sr = sample_rate or self.sample_rate
        audio = np.ascontiguousarray(audio, dtype=np.float32)
        # transformers v5 expects the "raw" key for a numpy array. Pipeline-level
        # timestamps (return_timestamps) currently break for the TDT decoder, so we
        # take plain text per call; utterance start/end come from VAD segmentation
        # upstream. TODO(Phase 1): word-level timestamps via the AutoModelForTDT API.
        out = self._pipe({"raw": audio, "sampling_rate": sr})
        text = (out.get("text") or "").strip()
        if not text:
            return []
        return [AsrSegment(text=text, start=0.0, end=len(audio) / sr)]
