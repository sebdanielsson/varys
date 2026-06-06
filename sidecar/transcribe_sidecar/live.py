"""Headless real-time transcription runner (console).

Live (mic = "Me" + system loopback = "Them"):
    uv run --directory sidecar python -m transcribe_sidecar.live [--lang sv]
Validate on a file (no audio devices needed):
    uv run --directory sidecar python -m transcribe_sidecar.live --file samples/test_en.wav
"""
from __future__ import annotations

import argparse
import logging
import shutil
import sys
import time

import numpy as np

from .asr import make_backend
from .config import Settings, get_settings
from .session import Session, make_segmenter
from .transcript.store import Transcript, Utterance

logging.basicConfig(level=logging.WARNING, format="%(levelname)s %(name)s: %(message)s")


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


def run_file(path: str, s: Settings) -> None:
    import soundfile as sf
    import soxr

    asr = make_backend(s)
    t = time.perf_counter()
    asr.load()
    print(f"{asr.label} loaded in {time.perf_counter() - t:.1f}s")

    audio, sr = sf.read(path, dtype="float32", always_2d=False)
    if audio.ndim > 1:
        audio = audio.mean(axis=1)
    if sr != s.sample_rate:
        audio = soxr.resample(audio, sr, s.sample_rate).astype(np.float32)

    seg = make_segmenter("File", s)
    transcript = Transcript(title="file")
    block = int(s.sample_rate * 0.1)
    for i in range(0, len(audio), block):
        for u in seg.push(audio[i:i + block]):
            _emit_file(asr, transcript, u)
    tail = seg.flush()
    if tail:
        _emit_file(asr, transcript, tail)
    print(f"\n{len(transcript.utterances)} utterance(s).")


def _emit_file(asr, transcript: Transcript, seg) -> None:
    result = asr.transcribe(seg.samples)
    text = result[0].text if result else ""
    if not text:
        return
    transcript.add(Utterance(speaker=seg.source, text=text, start=seg.t0, end=seg.t1))
    print(f"[{seg.source:>4}] [{seg.t0:7.2f} -> {seg.t1:7.2f}] {text}", flush=True)


def run_live(s: Settings) -> None:
    console = _Console()

    def sink(ev: dict) -> None:
        kind = ev.get("type")
        if kind == "partial":
            console.partial(f"[{ev['speaker']:>4}]~ {ev['text']}")
        elif kind == "final":
            console.final(f"[{ev['speaker']:>4}] [{ev['t0']:7.2f} -> {ev['t1']:7.2f}] {ev['text']}")
        elif kind == "status" and ev.get("state") == "listening":
            print(f"{ev['engine']}  |  Me <- {ev['mic']}  |  Them <- {ev['loopback']}")
            print("Listening... press Ctrl-C to stop.\n")
        elif kind == "status" and ev.get("state") == "stopped":
            files = ev.get("files") or {}
            print(f"\nsaved transcript: {files['md']}" if files else "\nno speech captured.")

    print("loading model...")
    session = Session(s, on_event=sink)
    try:
        session.start()
        while session.running:
            time.sleep(0.3)
    except KeyboardInterrupt:
        print("\nstopping...")
    finally:
        session.stop()


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
