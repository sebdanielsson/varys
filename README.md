# Varys — local AI meeting transcription

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
  Varysd separately (clean speaker split, no echo doubling).
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

## Desktop app (Phase 3)

WinUI 3 (Windows App SDK 2.1.3), unpackaged + self-contained. Launches and supervises
the sidecar, then shows live Me/Them captions over the WebSocket.

```powershell
cd app/VarysApp
dotnet run -c Debug -p:Platform=x64        # needs `uv` on PATH; auto-launches the sidecar
```

The **Summarize** button runs a local LLM (Gemma 4 E2B) over the transcript via Ollama —
install [Ollama](https://ollama.com) and `ollama pull gemma4:e2b` first.

## Meeting library & search (Meetings tab)

Every meeting is saved as its own folder under
`%LOCALAPPDATA%\Varys\meetings\<id>\` (`meta.json`, `transcript.{json,md,srt}`,
`summary.md`, `index.json`). The **Meetings** tab lists them with transcript + notes,
and search is **keyword** or **semantic** — local multilingual embeddings via Ollama
`embeddinggemma`, so you can find what someone said even months later. Pull the
embedder first: `ollama pull embeddinggemma`.

## Logs

The app writes a combined log — its own messages plus the sidecar's stdout/stderr — to:

```
%LOCALAPPDATA%\Varys\logs\app.log
```

Open it from the app via the **Open logs** link (bottom-right). Running the sidecar
standalone (`uv run python -m transcribe_sidecar`) logs to the console instead.

## Status

- [x] Phase 0 — scaffold & environment
- [x] Phase 1 — capture + VAD + per-language ASR (Parakeet en / KB-Whisper sv)
- [x] Phase 2 — FastAPI WebSocket service
- [x] Phase 3 — WinUI app: supervises sidecar, live Me/Them captions
- [x] Phase 4 — in-app transcript view (Meetings tab); export via Open folder
- [x] Phase 5 — local LLM summary (Gemma 4 E2B via Ollama)
- [x] Meeting library + keyword/semantic search (NavigationView UI)
- [ ] Phase 6 — MSIX packaging
