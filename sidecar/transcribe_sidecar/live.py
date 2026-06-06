"""Headless real-time transcription: capture -> VAD -> Parakeet -> live transcript.

Live (mic = "Me" + system loopback = "Them"):
    uv run --directory sidecar python -m transcribe_sidecar.live
Validate on a file (no audio devices needed):
    uv run --directory sidecar python -m transcribe_sidecar.live --file samples/test_en.wav
"""
from __future__ import annotations

import argparse
import logging
import queue
import shutil
import sys
import threading
import time

import numpy as np

from .asr import make_backend
from .audio import capture
from .audio.vad import Segment, StreamSegmenter
from .config import Settings, get_settings
from .transcript.store import Transcript, Utterance

logging.basicConfig(level=logging.WARNING, format="%(levelname)s %(name)s: %(message)s")
log = logging.getLogger("live")


class _Console:
    """Renders one updating 'partial' line plus committed 'final' lines below it."""

    def __init__(self) -> None:
        self._partial_len = 0

    def _width(self) -> int:
        try:
            return shutil.get_terminal_size().columns
        except Exception:
            return 100

    def partial(self, line: str) -> None:
        line = line[: self._width() - 1]
        pad = max(0, self._partial_len - len(line))
        sys.stdout.write("\r" + line + " " * pad)
        sys.stdout.flush()
        self._partial_len = len(line)

    def final(self, line: str) -> None:
        if self._partial_len:
            sys.stdout.write("\r" + " " * self._partial_len + "\r")
        print(line, flush=True)
        self._partial_len = 0


def _segmenter(source: str, s: Settings) -> StreamSegmenter:
    return StreamSegmenter(
        source, sample_rate=s.sample_rate, threshold=s.vad_threshold,
        min_silence_ms=s.vad_min_silence_ms, min_speech_ms=s.vad_min_speech_ms,
        max_segment_s=s.vad_max_segment_s,
    )


def _load_asr(s: Settings):
    backend = make_backend(s)
    t = time.perf_counter()
    backend.load()
    print(f"{backend.label} loaded in {time.perf_counter() - t:.1f}s")
    return backend


def _emit(asr, transcript: Transcript, seg: Segment, console: "_Console | None" = None) -> None:
    result = asr.transcribe(seg.samples)
    text = result[0].text if result else ""
    if not text:
        return
    transcript.add(Utterance(speaker=seg.source, text=text, start=seg.t0, end=seg.t1))
    line = f"[{seg.source:>4}] [{seg.t0:7.2f} -> {seg.t1:7.2f}] {text}"
    if console is not None:
        console.final(line)
    else:
        print(line, flush=True)


def run_file(path: str, s: Settings) -> None:
    import soundfile as sf
    import soxr

    asr = _load_asr(s)
    audio, sr = sf.read(path, dtype="float32", always_2d=False)
    if audio.ndim > 1:
        audio = audio.mean(axis=1)
    if sr != s.sample_rate:
        audio = soxr.resample(audio, sr, s.sample_rate).astype(np.float32)

    seg = _segmenter("File", s)
    transcript = Transcript(title="file")
    block = int(s.sample_rate * 0.1)
    for i in range(0, len(audio), block):
        for u in seg.push(audio[i:i + block]):
            _emit(asr, transcript, u)
    tail = seg.flush()
    if tail:
        _emit(asr, transcript, tail)
    print(f"\n{len(transcript.utterances)} utterance(s).")


def run_live(s: Settings) -> None:
    asr = _load_asr(s)
    print(f"Me   <- {capture.mic_name()}\nThem <- {capture.loopback_name()}\n"
          "Listening... press Ctrl-C to stop.\n")

    q: queue.Queue = queue.Queue()
    stop = threading.Event()
    segmenters = {"Me": _segmenter("Me", s), "Them": _segmenter("Them", s)}
    threads = [
        capture.MicCapture(q, stop, s.sample_rate),
        capture.LoopbackCapture(q, stop, s.sample_rate),
    ]
    for th in threads:
        th.start()

    transcript = Transcript(title=time.strftime("meeting-%Y%m%d-%H%M%S"))
    console = _Console()
    last_partial = {"Me": 0.0, "Them": 0.0}
    try:
        while True:
            source, samples = q.get()
            if samples is None:
                log.error("capture stream '%s' ended unexpectedly", source)
                continue
            for u in segmenters[source].push(samples):
                _emit(asr, transcript, u, console)
                last_partial[u.source] = 0.0
            if s.partial_interval_s > 0 and time.perf_counter() - last_partial[source] >= s.partial_interval_s:
                pend = segmenters[source].pending()
                if pend is not None:
                    win = int(s.partial_window_s * s.sample_rate)
                    clip = pend.samples[-win:] if len(pend.samples) > win else pend.samples
                    res = asr.transcribe(clip)
                    if res and res[0].text:
                        console.partial(f"[{source:>4}]~ {res[0].text}")
                    last_partial[source] = time.perf_counter()
    except KeyboardInterrupt:
        print("\nstopping...")
    finally:
        stop.set()
        for th in threads:
            th.join(timeout=2)
        for seg in segmenters.values():
            tail = seg.flush()
            if tail:
                _emit(asr, transcript, tail, console)
        if transcript.utterances:
            paths = transcript.save(s.transcript_dir)
            print(f"saved transcript: {paths['md']}")
        else:
            print("no speech captured.")


def main(argv: list[str] | None = None) -> None:
    ap = argparse.ArgumentParser(description="Real-time transcription (capture -> VAD -> ASR).")
    ap.add_argument("--file", help="transcribe an audio file instead of live capture")
    ap.add_argument("--language", "--lang", dest="language", choices=["auto", "en", "sv"],
                    help="override language/engine (sv -> KB-Whisper, en/auto -> Parakeet)")
    args = ap.parse_args(argv)
    s = get_settings()
    if args.language:
        s = s.model_copy(update={"language": args.language})
    if args.file:
        run_file(args.file, s)
    else:
        run_live(s)


if __name__ == "__main__":
    main()
