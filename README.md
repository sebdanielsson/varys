# Varys — local AI meeting transcription

Varys transcribes your online meetings (Rocket.Chat, Jitsi, Teams-in-the-browser, …) in real
time and keeps a searchable library of transcripts and AI notes — **entirely on your own
Windows PC**. It captures both your microphone and the meeting audio, labels who said what,
and can summarize each meeting with a local LLM. Nothing is ever sent to the cloud.

> Built for an NVIDIA GPU: speech-to-text runs on CUDA, and the summary LLM runs locally via
> [Ollama](https://ollama.com).

## Features

- **Live captions** with a clean **Me / Them** split — your mic vs. the meeting audio,
  transcribed separately so there's no echo doubling.
- **Per-language ASR for accuracy:** English/auto uses NVIDIA **Parakeet-TDT**; Swedish uses
  **KB-Whisper** (KBLab), which is far more accurate on Swedish.
- **Meeting library:** every meeting is saved as a transcript plus Markdown notes — browse,
  edit, and preview (rendered tables and all).
- **Local LLM summaries:** one click turns a transcript into a summary, key decisions, and
  action items.
- **Semantic search:** find what someone said months ago — even across languages — using
  local embeddings.
- **Private by design:** GPU transcription and the LLM both run on your machine. No telemetry.

## Requirements

- **Windows 11 23H2** (build 22631) or newer, x64.
- An **NVIDIA GPU** with a recent driver (GeForce RTX 20-series or newer recommended) —
  speech-to-text runs on CUDA.
- **~10 GB free disk** for the one-time engine, model, and LLM downloads.
- **[Ollama](https://ollama.com)** for summaries and semantic search. **Varys offers to set it
  up for you on first launch** — one click installs it (via winget) and pulls the two models
  it needs (`gemma4:e2b` for summaries, `embeddinggemma` for search). You can also install it
  yourself from [ollama.com/download](https://ollama.com/download).
- The **WebView2 runtime** — always preinstalled on Windows 11.

No separate .NET install is needed — the app is self-contained.

## Install

1. Download the latest **`Varys-vX.Y.Z-win-x64.zip`** from the
   [Releases page](https://github.com/sebdanielsson/varys/releases).
2. Unzip it anywhere (e.g. `C:\Tools\Varys`).
3. Run **`Varys.exe`**.

**On first launch** Varys sets up its local transcription engine — a one-time download of
PyTorch and the speech models (a few minutes). If Ollama isn't ready, a short greeter offers
to install it and the models for you. There's no installer, and nothing is written outside
your user profile.

## Using Varys

### Live transcription
1. Open the **Live** tab.
2. Pick the language — **Auto/English** (Parakeet) or **Swedish** (KB-Whisper). For best
   results don't mix languages within one meeting.
3. Click **Start**, then join your meeting. Your voice shows as **Me**, the meeting audio as
   **Them**.
4. Click **Stop** to end — the full timestamped transcript is saved automatically.
5. Click **Summarize** to generate notes (requires Ollama).

### Meetings library
- The **Meetings** tab lists every saved meeting with its transcript and notes.
- **Edit / preview** notes and transcripts as Markdown (with rendered tables).
- **Rename** a meeting by right-clicking it in the list, or double-clicking its title.
- **Open folder**, **Summarize**, or **Delete** (Delete moves it to a recoverable trash folder).
- **Search** from the box at the top — toggle **Semantic** (meaning-based, multilingual) or
  **Keyword**.

### Where your data lives
Everything stays under your user profile:

```
%LOCALAPPDATA%\Varys\
  meetings\<id>\   meta.json · transcript.{json,md,srt} · summary.md · index.json
  logs\app.log
```

## Troubleshooting

- The app writes a combined log (app + engine) to `%LOCALAPPDATA%\Varys\logs\app.log` — open it
  from the **Open logs** link at the bottom-right of the window.
- **Transcription never starts:** make sure your NVIDIA driver is current; the first run also
  needs a few minutes to download the engine.
- **Summarize / Search fail:** make sure Ollama is installed and running with the two models —
  the first-launch greeter can do this, or run `ollama pull gemma4:e2b` and
  `ollama pull embeddinggemma` yourself.

## Privacy

Varys is fully local: audio, transcripts, embeddings, and LLM inference never leave your
machine. There is no telemetry or account.

## Building from source / contributing

See **[CONTRIBUTING.md](CONTRIBUTING.md)** for the architecture, development setup, and release
process.

## Status

- ✅ Live capture + per-language ASR (Parakeet EN / KB-Whisper SV)
- ✅ WinUI app · meeting library · keyword + semantic search
- ✅ Local LLM summaries (Gemma via Ollama)
- ✅ Standalone win-x64 release with first-run engine setup
- 🚧 One-click MSIX installer (no first-run download)
