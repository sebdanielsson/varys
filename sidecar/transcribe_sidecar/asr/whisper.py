"""faster-whisper backend with a forced language (KB-Whisper for Swedish).

Used when the language is pinned. faster-whisper (CTranslate2) loads the model
directly from Hugging Face and runs ~12x realtime on a 4070. We feed it already
VAD-segmented 16 kHz utterances, so its own VAD is disabled.
"""
from __future__ import annotations

import logging
import os

import numpy as np

from .base import AsrSegment

logger = logging.getLogger(__name__)


def _add_cuda_dll_dirs() -> None:
    """Let CTranslate2 find torch's bundled cuDNN/cuBLAS DLLs on Windows."""
    if os.name != "nt":
        return
    try:
        import glob

        import nvidia

        base = os.path.dirname(nvidia.__file__)
        for sub in ("cudnn", "cublas"):
            for d in glob.glob(os.path.join(base, sub, "bin")):
                if os.path.isdir(d):
                    os.add_dll_directory(d)
    except Exception:
        logger.debug("cuda DLL dir setup skipped", exc_info=True)


class WhisperAsr:
    def __init__(self, model_id: str, device: str = "auto", language: str = "sv",
                 compute_type: str = "float16", sample_rate: int = 16000):
        self.model_id = model_id
        self.language = language
        self.sample_rate = sample_rate
        self._device = device
        self._compute_type = compute_type
        self._model = None

    @property
    def label(self) -> str:
        return f"KB-Whisper ({self.language})"

    def load(self) -> None:
        import torch
        from faster_whisper import WhisperModel

        _add_cuda_dll_dirs()
        use_cuda = self._device in ("auto", "cuda") and torch.cuda.is_available()
        device = "cuda" if use_cuda else "cpu"
        compute = self._compute_type if use_cuda else "int8"
        logger.info("Loading %s on %s (%s), language=%s", self.model_id, device, compute, self.language)
        self._model = WhisperModel(self.model_id, device=device, compute_type=compute)

    def transcribe(self, audio: np.ndarray, sample_rate: int | None = None) -> list[AsrSegment]:
        if self._model is None:
            raise RuntimeError("WhisperAsr.load() must be called before transcribe().")
        audio = np.ascontiguousarray(audio, dtype=np.float32)
        segments, _info = self._model.transcribe(
            audio,
            language=self.language,
            beam_size=5,
            condition_on_previous_text=False,
            vad_filter=False,
        )
        text = " ".join(s.text.strip() for s in segments).strip()
        if not text:
            return []
        dur = len(audio) / (sample_rate or self.sample_rate)
        return [AsrSegment(text=text, start=0.0, end=dur, language=self.language)]
