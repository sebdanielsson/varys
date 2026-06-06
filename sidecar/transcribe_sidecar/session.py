"""Session: owns the capture -> VAD -> ASR pipeline and emits events to a sink.

Shared by the console runner (live.py) and the FastAPI server (server/app.py). The
`on_event` sink receives plain JSON-serializable dicts:

    {"type": "status",  "state": "listening"|"stopped", "engine", "language", "mic", "loopback", ...}
    {"type": "partial", "speaker", "text", "t0", "t1"}
    {"type": "final",   "speaker", "text", "t0", "t1", "language"}
    {"type": "error",   "source", "message"}
"""
from __future__ import annotations

import logging
import queue
import threading
import time
from typing import Callable

from .asr import make_backend
from .audio import capture
from .audio.vad import StreamSegmenter
from .config import Settings
from .transcript.store import Transcript, Utterance

logger = logging.getLogger(__name__)

EventSink = Callable[[dict], None]


def make_segmenter(source: str, s: Settings) -> StreamSegmenter:
    return StreamSegmenter(
        source, sample_rate=s.sample_rate, threshold=s.vad_threshold,
        min_silence_ms=s.vad_min_silence_ms, min_speech_ms=s.vad_min_speech_ms,
        max_segment_s=s.vad_max_segment_s,
    )


class Session:
    def __init__(self, settings: Settings, on_event: EventSink):
        self._s = settings
        self._emit = on_event
        self._stop = threading.Event()
        self._q: queue.Queue = queue.Queue()
        self._lock = threading.Lock()      # serialize GPU access to the ASR model
        self._threads: list = []
        self._consumer: threading.Thread | None = None
        self._segmenters: dict[str, StreamSegmenter] = {}
        self._asr = None
        self.transcript = Transcript(title=time.strftime("meeting-%Y%m%d-%H%M%S"))
        self.running = False
        self.engine_label = ""

    def start(self) -> dict:
        """Load the model and start capture + consumer threads. Blocking (model load)."""
        self._asr = make_backend(self._s)
        self._asr.load()
        self.engine_label = self._asr.label
        self._segmenters = {"Me": make_segmenter("Me", self._s), "Them": make_segmenter("Them", self._s)}
        self._threads = [
            capture.MicCapture(self._q, self._stop, self._s.sample_rate),
            capture.LoopbackCapture(self._q, self._stop, self._s.sample_rate),
        ]
        for t in self._threads:
            t.start()
        self._consumer = threading.Thread(target=self._run, name="session-consumer", daemon=True)
        self._consumer.start()
        self.running = True
        status = {
            "type": "status", "state": "listening", "engine": self.engine_label,
            "language": self._s.language, "mic": capture.mic_name(), "loopback": capture.loopback_name(),
        }
        self._emit(status)
        return status

    def _transcribe(self, samples):
        with self._lock:
            return self._asr.transcribe(samples)

    def _run(self) -> None:
        last_partial = {"Me": 0.0, "Them": 0.0}
        while not self._stop.is_set():
            try:
                source, samples = self._q.get(timeout=0.25)
            except queue.Empty:
                continue
            if samples is None:
                logger.error("capture stream '%s' ended", source)
                self._emit({"type": "error", "source": source, "message": "capture stream ended"})
                continue
            for u in self._segmenters[source].push(samples):
                self._finalize(u)
                last_partial[u.source] = 0.0
            if self._s.partial_interval_s > 0 and \
                    time.perf_counter() - last_partial[source] >= self._s.partial_interval_s:
                pend = self._segmenters[source].pending()
                if pend is not None:
                    win = int(self._s.partial_window_s * self._s.sample_rate)
                    clip = pend.samples[-win:] if len(pend.samples) > win else pend.samples
                    res = self._transcribe(clip)
                    if res and res[0].text:
                        self._emit({"type": "partial", "speaker": source, "text": res[0].text,
                                    "t0": pend.t0, "t1": pend.t1})
                    last_partial[source] = time.perf_counter()

    def _finalize(self, u) -> None:
        res = self._transcribe(u.samples)
        if not res or not res[0].text:
            return
        seg = res[0]
        self.transcript.add(Utterance(u.source, seg.text, u.t0, u.t1, seg.language))
        self._emit({"type": "final", "speaker": u.source, "text": seg.text,
                    "t0": u.t0, "t1": u.t1, "language": seg.language})

    def stop(self) -> dict:
        if not self.running:
            return {}
        self._stop.set()
        for t in self._threads:
            t.join(timeout=2)
        if self._consumer:
            self._consumer.join(timeout=3)
        for seg in self._segmenters.values():
            tail = seg.flush()
            if tail:
                self._finalize(tail)
        self.running = False
        meta = {}
        if self.transcript.utterances:
            from . import library
            meta = library.save_meeting(self.transcript, settings=self._s,
                                        language=self._s.language, engine=self.engine_label)
        self._emit({"type": "status", "state": "stopped", "meeting": meta,
                    "utterances": len(self.transcript.utterances)})
        return meta
