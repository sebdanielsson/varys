"""Runtime configuration, overridable via environment (prefix TRANSCRIBE_) or .env."""
from __future__ import annotations

from functools import lru_cache

from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(
        env_prefix="TRANSCRIBE_", env_file=".env", extra="ignore"
    )

    # ASR
    model_id: str = "nvidia/parakeet-tdt-0.6b-v3"
    device: str = "auto"          # auto -> cuda if available else cpu
    sample_rate: int = 16000      # Parakeet expects 16 kHz mono

    # VAD (Phase 1)
    vad_threshold: float = 0.5
    vad_min_silence_ms: int = 600
    vad_min_speech_ms: int = 250
    vad_max_segment_s: float = 20.0

    # Server (Phase 2)
    host: str = "127.0.0.1"
    port: int = 8765

    # Summary (Phase 5) — verify exact Ollama tag before use
    ollama_url: str = "http://127.0.0.1:11434"
    summary_model: str = "gemma4:e2b"

    # Storage
    transcript_dir: str = "transcripts"


@lru_cache
def get_settings() -> Settings:
    return Settings()
