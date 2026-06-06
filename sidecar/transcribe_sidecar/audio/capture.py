"""Dual-stream audio capture: microphone ("Me") and system loopback ("Them").

Two libraries, each for its strength:
- mic      -> sounddevice (PortAudio); robust across capture devices. Captured at the
              device's native rate and stream-resampled to 16 kHz with soxr.
- loopback -> soundcard; reliable WASAPI loopback of the default speaker.

Both deliver float32 mono @ 16 kHz, ready for VAD + Parakeet.
"""
from __future__ import annotations

import logging
import queue
import threading
from typing import Iterator

import numpy as np

logger = logging.getLogger(__name__)


def list_devices() -> dict:
    import soundcard as sc

    return {
        "microphones": [m.name for m in sc.all_microphones(include_loopback=False)],
        "loopback": [m.name for m in sc.all_microphones(include_loopback=True) if m.isloopback],
        "default_speaker": sc.default_speaker().name,
        "default_mic": sc.default_microphone().name,
    }


def mic_name() -> str:
    import sounddevice as sd

    return str(sd.query_devices(kind="input")["name"])


def loopback_name() -> str:
    import soundcard as sc

    return sc.default_speaker().name


def _init_com() -> None:
    """soundcard relies on COM; a background thread must initialize it (MTA)."""
    try:
        import ctypes

        ctypes.windll.ole32.CoInitializeEx(None, 0x0)  # COINIT_MULTITHREADED
    except Exception:  # already initialized / non-Windows — safe to ignore
        logger.debug("CoInitializeEx ignored", exc_info=True)


class _ProducerThread(threading.Thread):
    """Runs a frame generator, pushing (source, mono_float32) onto a queue.

    On error pushes (source, None) and exits, so the consumer can react.
    """

    def __init__(self, source: str, out: "queue.Queue", stop: threading.Event,
                 sample_rate: int = 16000, block_ms: int = 100):
        super().__init__(name=f"capture-{source}", daemon=True)
        self.source = source
        self.sr = sample_rate
        self.block_ms = block_ms
        self._out = out
        self._stop = stop

    def _frames(self) -> Iterator[np.ndarray]:
        raise NotImplementedError

    def run(self) -> None:
        try:
            for mono in self._frames():
                if self._stop.is_set():
                    break
                self._out.put((self.source, np.ascontiguousarray(mono, dtype=np.float32)))
        except Exception:
            logger.exception("capture thread '%s' failed", self.source)
            self._out.put((self.source, None))


class MicCapture(_ProducerThread):
    def __init__(self, out, stop, sample_rate=16000, block_ms=100):
        super().__init__("Me", out, stop, sample_rate, block_ms)

    def _frames(self) -> Iterator[np.ndarray]:
        import sounddevice as sd
        import soxr

        native = int(sd.query_devices(kind="input")["default_samplerate"])
        block = max(1, int(native * self.block_ms / 1000))
        resampler = soxr.ResampleStream(native, self.sr, 1, dtype="float32") if native != self.sr else None
        with sd.InputStream(samplerate=native, channels=1, dtype="float32", blocksize=block) as st:
            while not self._stop.is_set():
                data, _ = st.read(block)
                mono = data[:, 0]
                yield resampler.resample_chunk(mono) if resampler is not None else mono


class LoopbackCapture(_ProducerThread):
    def __init__(self, out, stop, sample_rate=16000, block_ms=100):
        super().__init__("Them", out, stop, sample_rate, block_ms)

    def _frames(self) -> Iterator[np.ndarray]:
        import soundcard as sc

        _init_com()
        spk = sc.default_speaker()
        mic = sc.get_microphone(spk.name, include_loopback=True)
        block = max(512, int(self.sr * self.block_ms / 1000))
        with mic.recorder(samplerate=self.sr, channels=mic.channels) as rec:
            while not self._stop.is_set():
                data = rec.record(numframes=block)
                yield data.mean(axis=1) if data.ndim > 1 else data
