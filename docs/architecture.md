# Architecture & build plan

Local-first meeting transcription for Windows. All processing stays on the
machine: speech-to-text via NVIDIA Parakeet-TDT, summaries via a local LLM.

## Components

| Component | Tech | Responsibility |
|-----------|------|----------------|
| UI app    | WinUI 3 (Windows App SDK 2.x, C#/.NET) | Live caption window, transcript view + export, start/stop, tray icon. Launches and supervises the sidecar. |
| Sidecar   | Python 3.13, FastAPI | Audio capture, VAD, Parakeet ASR, transcript store, Ollama summary. |

They communicate over **localhost**:

- **WebSocket** `/ws` — sidecar → UI stream of transcription events:
  `{ "type": "utterance", "speaker": "Me|Them", "text": str, "t0": float, "t1": float, "lang": str }`
- **REST** — UI → sidecar control: `POST /session/start`, `POST /session/stop`,
  `POST /session/summarize`, `GET /health`.

## Key design decisions

1. **Python sidecar (not pure .NET).** Parakeet only runs in Python
   (HF Transformers / PyTorch). We use the **HF Transformers** path, not the
   full NeMo toolkit, to keep dependencies light.
2. **Two independent capture streams.** Mic → "Me", system loopback → "Them".
   Separate transcription gives the speaker split for free and avoids
   re-transcribing meeting audio that leaks into the mic (echo doubling).
   Per-process loopback (capture only the meeting app) is a later enhancement.
3. **VAD-chunked near-real-time**, not true transducer streaming. A voice
   activity detector (Silero) segments each stream at silence; each utterance
   is transcribed when it completes (~1–3 s latency) with timestamps and
   punctuation. True low-latency streaming is a later optimization.
4. **Sequential GPU use.** ASR runs live; the summary LLM runs after the
   meeting, so the two models never contend for VRAM (matters on a 6 GB card).
5. **Per-language engine (manual switch).** Parakeet-TDT-0.6B-v3 has no language
   setting (confirmed in the installed code) — it auto-detects, and did so
   unreliably on short Swedish utterances. So the language is set manually:
   **English/auto → Parakeet** (fast), **Swedish → KB-Whisper** (KBLab, ~47% lower
   WER than whisper-large-v3) via faster-whisper with the language forced. Only the
   selected engine loads, so VRAM stays low. Set with `TRANSCRIBE_LANGUAGE` or
   `--language` / `--lang`.

## Pinned environment facts (verified 2026-06)

- **Python 3.13** is the ceiling: PyTorch ships no CUDA wheels for 3.14 yet.
- **PyTorch + CUDA 12.8** wheels (`--index-url .../whl/cu128`) — includes
  Turing (`sm_75`) for the work PC's RTX 20-series and Ada for the 4070 Super.
- **`transformers` from source (git main).** The TDT decoder for
  `nvidia/parakeet-tdt-0.6b-v3` is not yet in a stable release. Loading uses
  `AutoModelForTDT` / the ASR pipeline; **no NeMo** required. Pin a commit SHA
  once Phase 1 is validated.
- Model: `nvidia/parakeet-tdt-0.6b-v3`, CC-BY-4.0, ~2–3 GB VRAM, 25 European
  languages incl. Swedish, word/segment timestamps + automatic punctuation.
- Summary LLM: **Gemma 4 E2B** via Ollama (very light). Confirm the exact
  Ollama tag at Phase 5; bump to E4B if Swedish summaries are thin.
- **uv** manages the sidecar (`pyproject.toml` + `uv.lock`): `[tool.uv.sources]`
  routes torch/torchaudio to the cu128 index and `transformers` to git main.
- **faster-whisper + KB-Whisper** for the Swedish path (CTranslate2). On Windows,
  CTranslate2 locates cuDNN/cuBLAS via torch's bundled `nvidia/*` DLLs, which
  `asr/whisper.py` adds to the DLL search path. compute_type float16 (~3 GB) or
  int8 for the work PC.

## Machines

- **Dev/test:** desktop, RTX 4070 Super (12 GB).
- **Deploy target:** work PC, NVIDIA RTX 20-series (confirm exact model / VRAM).

## Build plan

| Phase | Deliverable |
|-------|-------------|
| 0 | Scaffold + environment (Python 3.13 venv, pinned deps). |
| 1 | Headless sidecar core: capture mic + loopback → 16 kHz mono → VAD → Parakeet → timestamped console transcript. Validate Swedish + English. |
| 2 | FastAPI service: WebSocket stream + REST control; save transcript JSON. |
| 3 | WinUI shell: launch/supervise sidecar, live caption window (Me/Them). |
| 4 | Transcript view + export (Markdown / SRT / VTT / txt). |
| 5 | Ollama summary (decisions + action items), runs after ASR releases GPU. |
| 6 | MSIX packaging, bundled Python, first-run model download. |
