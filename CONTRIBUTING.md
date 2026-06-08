# Contributing to Varys

Thanks for hacking on Varys! This covers the architecture, building/running from source, and
how releases work. (For installing and using the app, see the [README](README.md).)

## Architecture

A **WinUI 3** desktop app (C# / .NET 10) provides the UI and supervises a **Python sidecar**
that does audio capture, VAD, ASR, and the LLM calls. They talk over a localhost WebSocket
(live text) plus REST (control + library).

```
WinUI 3 app   ◄── WebSocket (live captions) ──►   Python sidecar
(C# / .NET 10)     REST (start/stop, library)      (FastAPI · Parakeet-TDT / KB-Whisper · Ollama)
```

- **Two capture streams:** microphone → **Me**, system loopback → **Them**, transcribed
  separately (clean speaker split, no echo doubling).
- **VAD-chunked near-real-time:** each utterance is transcribed at the silence boundary
  (~1–3 s latency), with interim partials for a streaming feel.
- **Per-language engines:** English/auto → **Parakeet-TDT** (HF Transformers); Swedish →
  **KB-Whisper** (faster-whisper / CTranslate2), which roughly halves WER on Swedish.
- **Sequential GPU use:** ASR during the meeting, the summary LLM afterwards.
- **Summaries + search:** local **Ollama** — `gemma4:e2b` for summaries, `embeddinggemma` for
  multilingual embeddings (brute-force cosine search).

The C# side stays pure UI; all audio and ML lives in the sidecar. See
[docs/architecture.md](docs/architecture.md) for the full design.

## Repo layout

```
app/Varys/      WinUI 3 desktop app (C#)
app/branding/   logo.svg + gen_assets.py (icon / tile generation)
sidecar/        Python 3.13 engine (the transcribe_sidecar package)
.github/        CI + release workflows
docs/           architecture & decisions
```

## Prerequisites (dev)

- **.NET 10 SDK** (10.0.300 or newer).
- **[uv](https://docs.astral.sh/uv/)** — manages Python 3.13 and every Python dependency.
- **NVIDIA GPU + recent driver** (the CUDA 12.8 PyTorch wheels are pulled automatically).
- **[Ollama](https://ollama.com)** with `gemma4:e2b` and `embeddinggemma` pulled (or let the
  app's first-run greeter install them).

## Build & run

### Sidecar

```powershell
cd sidecar
uv sync                                          # create .venv + install everything
uv run python scripts/smoke_asr.py <audio.wav>   # quick model check
```

PyTorch comes from the CUDA 12.8 index and `transformers` from git `main` — the TDT decoder
for `parakeet-tdt-0.6b-v3` isn't in a stable release yet. See `sidecar/pyproject.toml`
(`[tool.uv.sources]`).

### App

```powershell
cd app/Varys
dotnet run -c Debug -p:Platform=x64    # auto-launches and supervises the sidecar
```

In dev the app finds the sidecar's `.venv` automatically. In a standalone build it instead
creates a per-user venv with `uv sync` on first run.

### Sidecar standalone (no UI)

```powershell
cd sidecar
uv run python -m transcribe_sidecar.live            # English/auto (Parakeet)
uv run python -m transcribe_sidecar.live --lang sv  # Swedish (KB-Whisper)
uv run python -m transcribe_sidecar                 # FastAPI server on http://127.0.0.1:8765
```

API surface: `GET /health` · `POST /session/start {language}` · `POST /session/stop` ·
`/meetings` CRUD · `GET /search` · `WS /ws` (streams `status` / `partial` / `final` events).
Handy scripts live in `sidecar/scripts/` (e.g. `server_e2e.py`, `library_test.py`).

## Conventions

- All files are **UTF-8 (no BOM) + LF**, enforced via `.gitattributes`.
- Keep the C# project self-contained and unpackaged (`WindowsPackageType=None`) — a packaged
  MSIX app is blocked from `127.0.0.1` by default, which would break the sidecar link.
- Dependencies (NuGet + GitHub Actions) are **pinned to exact versions**; Renovate opens the
  bump PRs.

## CI / CD

- **`.github/workflows/ci.yml`** — builds the WinUI app (`dotnet build`) and lints the sidecar
  (`ruff`). Runs on push and PRs.
- **`.github/workflows/release.yml`** — on a `v*` tag, publishes a self-contained **win-x64
  MSI** (installs to Program Files) and a portable **zip**, both containing the app + sidecar
  source + `uv.exe`. The first-run welcome provisions the engine, speech/language models, and
  Ollama (so the installer stays small).

The MSI is authored in `installer/Varys.wxs` and built with **WiX v5** (`dotnet tool install
--global wix --version "5.*"`, then `wix build`). We pin v5 because WiX v6+ requires accepting
the paid OSMF EULA; v5 is the MIT-licensed release. Build it locally with:

```powershell
dotnet publish app/Varys/Varys.csproj -c Release -p:Platform=x64 -p:DebugType=None -o publish
git archive HEAD sidecar -o sidecar.tar; tar -xf sidecar.tar -C publish
Copy-Item (Get-Command uv).Source publish/uv.exe
wix build installer/Varys.wxs -d Version=0.1.0 -d PublishDir=publish -o Varys.msi
```

### Cutting a release

```powershell
git tag v0.1.0
git push origin v0.1.0
```

The release workflow builds the zip and creates the GitHub Release.

## Logs

`%LOCALAPPDATA%\Varys\logs\app.log` holds the app's own messages plus the sidecar's
stdout/stderr. Running the sidecar standalone logs to the console instead.

## Roadmap

- [x] Phase 0–5 — scaffold · capture + VAD + per-language ASR · FastAPI WS · WinUI app · summaries
- [x] Meeting library + keyword/semantic search
- [~] Phase 6 — standalone win-x64 release done; one-click MSIX next
