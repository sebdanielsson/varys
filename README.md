# Transcribe — local AI meeting transcription

Real-time, fully local transcription of online meetings (Rocket.Chat, Jitsi, etc.)
on Windows, using NVIDIA **Parakeet-TDT** for speech-to-text and a local LLM for
post-meeting summaries. Nothing leaves the machine.

## How it works

A **WinUI 3** desktop app (C#/.NET) provides the UI and supervises a **Python
sidecar** that does the heavy lifting. They talk over a localhost WebSocket.

```
WinUI 3 app  ◄── WebSocket (live text) ──►  Python sidecar
(C# / .NET)      REST (start/stop)           (FastAPI + Parakeet-TDT + Ollama)
```

- **Two capture streams:** microphone → **"Me"**, system loopback → **"Them"**.
  Transcribed separately (clean speaker split, no echo doubling).
- **VAD-chunked near-real-time:** each utterance is transcribed at the silence
  boundary (~1–3 s latency) with timestamps and punctuation.
- **Sequential GPU use:** ASR during the meeting, summary LLM afterwards.
- **Language:** Swedish or English, auto-detected per utterance.

See [docs/architecture.md](docs/architecture.md) for the full design and the
phase-by-phase build plan.

## Layout

```
sidecar/   Python 3.13 service (audio capture, VAD, Parakeet ASR, summary)
app/       WinUI 3 desktop app (Phase 3)
docs/      architecture & decisions
```

## Setup (sidecar)

Managed with [uv](https://docs.astral.sh/uv/). Requires an NVIDIA GPU; uv handles
Python 3.13 and every dependency (PyTorch is pulled from the CUDA 12.8 index,
`transformers` from git main for the TDT decoder).

```powershell
cd sidecar
uv sync                                          # create .venv + install everything
uv run python scripts/smoke_asr.py <audio.wav>   # quick model check
```

## Run (live transcription)

```powershell
cd sidecar
uv run python -m transcribe_sidecar.live            # English/auto (Parakeet)
uv run python -m transcribe_sidecar.live --lang sv  # Swedish (KB-Whisper, forced)
```

Speak (shows as `[  Me]`) and let meeting audio play (`[Them]`). `Ctrl-C` saves the
transcript (JSON / Markdown / SRT) to `sidecar/transcripts/`.

## Server (Phase 2)

```powershell
cd sidecar
uv run python -m transcribe_sidecar          # FastAPI on http://127.0.0.1:8765
```

- `GET /health` · `POST /session/start` `{ "language": "auto|en|sv" }` · `POST /session/stop`
- `WS /ws` streams events: `status`, `partial`, `final` (`{speaker, text, t0, t1, language}`).

## Status

- [x] Phase 0 — scaffold & environment
- [x] Phase 1 — capture + VAD + per-language ASR (Parakeet en / KB-Whisper sv)
- [x] Phase 2 — FastAPI WebSocket service
- [ ] Phase 3 — WinUI shell + live captions
- [ ] Phase 4 — transcript view + export
- [ ] Phase 5 — local LLM summary (Gemma 4 E2B via Ollama)
- [ ] Phase 6 — MSIX packaging
