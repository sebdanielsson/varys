"""Silero-VAD streaming segmentation: turn a 16 kHz mono stream into utterances.

Feed arbitrary-length blocks via `push()`; get back complete `Segment`s as the VAD
detects speech end. Utterance start/end times come from the VAD boundaries (the TDT
ASR model does not expose reliable timestamps through the HF pipeline).
"""
from __future__ import annotations

import logging
from dataclasses import dataclass

import numpy as np

logger = logging.getLogger(__name__)

VAD_FRAME = 512  # Silero VAD step size at 16 kHz


@dataclass
class Segment:
    source: str
    samples: np.ndarray  # float32 mono, 16 kHz
    t0: float            # seconds since stream start
    t1: float


class StreamSegmenter:
    def __init__(self, source: str, sample_rate: int = 16000, threshold: float = 0.5,
                 min_silence_ms: int = 600, min_speech_ms: int = 250, max_segment_s: float = 20.0):
        import torch
        from silero_vad import VADIterator, load_silero_vad

        if sample_rate != 16000:
            raise ValueError("StreamSegmenter requires 16 kHz audio")
        self._torch = torch
        self.source = source
        self.sr = sample_rate
        self.min_speech = int(min_speech_ms * sample_rate / 1000)
        self.max_segment = int(max_segment_s * sample_rate)
        self._vad = VADIterator(load_silero_vad(), threshold=threshold,
                                sampling_rate=sample_rate, min_silence_duration_ms=min_silence_ms)
        self._tail = np.zeros(0, dtype=np.float32)  # samples not yet a full VAD frame
        self._buf = np.zeros(0, dtype=np.float32)   # retained audio from _buf_start
        self._buf_start = 0   # abs sample index of _buf[0]
        self._abs = 0         # total samples fed to the VAD
        self._start: int | None = None  # abs index of the open utterance, or None

    def push(self, samples: np.ndarray) -> list[Segment]:
        out: list[Segment] = []
        data = np.concatenate([self._tail, np.asarray(samples, dtype=np.float32)])
        n = (len(data) // VAD_FRAME) * VAD_FRAME
        frames, self._tail = data[:n], data[n:]
        self._buf = np.concatenate([self._buf, frames])
        for i in range(0, n, VAD_FRAME):
            res = self._vad(self._torch.from_numpy(frames[i:i + VAD_FRAME]), return_seconds=False)
            self._abs += VAD_FRAME
            if res:
                if "start" in res:
                    self._start = int(res["start"])
                if "end" in res and self._start is not None:
                    seg = self._finish(int(res["end"]))
                    if seg:
                        out.append(seg)
            if self._start is not None and self._abs - self._start >= self.max_segment:
                seg = self._finish(self._abs)   # cut an over-long utterance, keep going
                if seg:
                    out.append(seg)
                self._start = self._abs
            elif self._start is None and self._abs - self._buf_start > self.sr:
                self._trim(self._abs - self.sr)  # keep ~1 s lookback during silence
        return out

    def flush(self) -> Segment | None:
        """End of stream: emit any still-open utterance."""
        seg = None
        if self._start is not None and self._abs - self._start >= self.min_speech:
            seg = self._finish(self._abs)
        self._start = None
        return seg

    def pending(self) -> Segment | None:
        """The in-progress (not yet finalized) utterance, for interim transcription."""
        if self._start is None:
            return None
        a = max(0, self._start - self._buf_start)
        b = self._abs - self._buf_start
        if b - a < self.min_speech:
            return None
        return Segment(self.source, self._buf[a:b], self._start / self.sr, self._abs / self.sr)

    def _finish(self, end_abs: int) -> Segment | None:
        start = self._start
        self._start = None
        if start is None or end_abs - start < self.min_speech:
            self._trim(end_abs)
            return None
        a = max(0, start - self._buf_start)
        b = max(a, end_abs - self._buf_start)
        samples = self._buf[a:b].copy()
        self._trim(end_abs)
        return Segment(self.source, samples, start / self.sr, end_abs / self.sr)

    def _trim(self, upto_abs: int) -> None:
        if upto_abs > self._buf_start:
            self._buf = self._buf[upto_abs - self._buf_start:]
            self._buf_start = upto_abs
