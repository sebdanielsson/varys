"""Runtime configuration, overridable via environment (prefix TRANSCRIBE_) or .env."""
from __future__ import annotations

import os
from functools import lru_cache

from pydantic import Field
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(
        env_prefix="TRANSCRIBE_", env_file=".env", extra="ignore"
    )

    # ASR
    model_id: str = "nvidia/parakeet-tdt-0.6b-v3"
    device: str = "auto"          # auto -> cuda if available else cpu
    sample_rate: int = 16000      # both engines expect 16 kHz mono

    # Language / engine selection. Parakeet has no language switch (auto-detects),
    # so Swedish uses KB-Whisper with the language forced for much better quality.
    language: str = "auto"        # auto | en -> Parakeet;  sv -> KB-Whisper
    whisper_model_sv: str = "KBLab/kb-whisper-large"
    whisper_compute_type: str = "float16"  # float16 | int8_float16 | int8

    # VAD (Phase 1)
    vad_threshold: float = 0.5
    vad_min_silence_ms: int = 600
    vad_min_speech_ms: int = 250
    vad_max_segment_s: float = 20.0

    # Streaming: while someone is still talking, emit an interim ("partial") hypothesis
    # this often (0 disables). Partials transcribe only the last `partial_window_s` to
    # bound GPU cost on long monologues; the final (on silence) re-does the full utterance.
    partial_interval_s: float = 1.2
    partial_window_s: float = 10.0

    # Server (Phase 2)
    host: str = "127.0.0.1"
    port: int = 8765

    # Summary (Phase 5) — verify exact Ollama tag before use
    ollama_url: str = "http://127.0.0.1:11434"
    summary_model: str = "gemma4:e2b"
    embed_model: str = "embeddinggemma"   # multilingual embeddings for semantic search

    # Storage
    transcript_dir: str = "transcripts"   # console runner output (legacy)
    data_dir: str = Field(default_factory=lambda: os.path.join(
        os.environ.get("LOCALAPPDATA") or os.path.expanduser("~/.local/share"), "Transcribe"))


@lru_cache
def get_settings() -> Settings:
    return Settings()
