"""Pluggable ASR backends: Parakeet-TDT (auto language) and KB-Whisper (forced)."""
from __future__ import annotations

from .base import AsrSegment


def make_backend(settings):
    """Construct (but don't load) the ASR backend for the configured language.

    sv -> KB-Whisper with the language forced; en/auto -> Parakeet (auto-detect).
    """
    lang = (settings.language or "auto").lower()
    if lang == "sv":
        from .whisper import WhisperAsr

        return WhisperAsr(
            settings.whisper_model_sv, device=settings.device, language="sv",
            compute_type=settings.whisper_compute_type, sample_rate=settings.sample_rate,
        )
    from .parakeet import ParakeetAsr

    return ParakeetAsr(settings.model_id, device=settings.device, sample_rate=settings.sample_rate)


__all__ = ["AsrSegment", "make_backend"]
