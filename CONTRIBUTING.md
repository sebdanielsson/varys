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
installer/      Varys.wxs — WiX v5 authoring for the MSI
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
- `.github/renovate.json` maps each update type to the conventional type that carries the right
  release impact: patch → `fix(deps)`, minor → `feat(deps)`, major → `feat(deps)!`, and a Python
  feature release is treated as breaking. **GitHub Actions are the exception** — they run in CI and
  never ship inside the MSI, so they're forced to `chore(deps)` and never move the app version.
  Anything a user actually receives should drive the version; nothing else should.
- By the same rule, Dependabot **security** updates to `sidecar/uv.lock` are forced to `fix(deps)`
  in `.github/dependabot.yml` so they cut a patch release. The lockfile ships in the MSI and the
  app runs `uv sync` against it on first launch, so a CVE fix in a locked transitive dependency
  only reaches users once a new version goes out. Dependabot's own default (`build(deps):`) would
  be ignored by Release Please and the fix would sit on `main` unreleased. Renovate still owns
  ordinary version updates; Dependabot's scheduled PRs are disabled with a zero limit.

## CI / CD

- **`.github/workflows/ci.yml`** — builds the WinUI app (`dotnet build`) and lints the sidecar
  (`ruff`). Runs on push and PRs.
- **`.github/workflows/release-please.yml`** — maintains the release PR, then (in the same run,
  gated on `release_created`) publishes a self-contained **win-x64 MSI** (installs to Program
  Files) and a portable **zip**, both containing the app + sidecar source + `uv.exe`, and submits
  the new version to winget. The first-run welcome provisions the engine, speech/language models,
  and Ollama (so the installer stays small).
- **`.github/workflows/sidecar-smoke.yml`** — `uv sync --frozen` + imports the whole native stack
  on Windows. Runs only when `sidecar/pyproject.toml`, `uv.lock`, or `.python-version` change,
  since the lint job never installs the dependencies. Runners have no GPU, so it can't exercise
  CUDA, but it catches a missing wheel or an ABI break before it reaches a release.
- **`.github/workflows/lint-pr.yml`** — fails the PR if its title isn't a conventional commit,
  since that title becomes the squash commit Release Please reads.

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

Releases are automated with [Release Please](https://github.com/googleapis/release-please) — **don't
create tags by hand**. Every push to `main` updates a standing release PR that bumps
`app/Varys/Varys.csproj` and `CHANGELOG.md` from the conventional commits since the last release.

Merging that PR *is* the release: it tags the commit, creates the GitHub Release, builds and
attaches the MSI + zip, and submits the version to winget.

Which means the commit subjects — i.e. the **PR titles**, since PRs are squash-merged — decide the
version: `feat:` → minor, `fix:` → patch, `feat!:` or a `BREAKING CHANGE:` footer → major, and
`chore:`/`docs:`/`ci:` → no release. To force a specific version, add a `Release-As: 1.2.3` footer
to a commit on `main`.

## Logs

`%LOCALAPPDATA%\Varys\logs\app.log` holds the app's own messages plus the sidecar's
stdout/stderr. Running the sidecar standalone logs to the console instead.

## Roadmap

Shipped:

- [x] Phase 0–5 — scaffold · capture + VAD + per-language ASR · FastAPI WS · WinUI app · summaries
- [x] Meeting library + keyword/semantic search
- [x] Standalone win-x64 release — MSI + portable zip, with first-run engine setup
- [x] Automated releases (Release Please) and winget submission

Next:

- [ ] Phase 6 — one-click MSIX installer, so there's no first-run download. Blocked on the
      `WindowsPackageType=None` constraint noted under [Conventions](#conventions): a packaged app
      can't reach `127.0.0.1` by default, which is how the UI talks to the sidecar.
