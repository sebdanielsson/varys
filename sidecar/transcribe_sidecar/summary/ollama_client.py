"""Post-meeting summary via a local Ollama model (Phase 5).

Default model: Gemma 4 E2B (very light). Verify the exact Ollama tag before use.
Runs after the meeting, so it has the GPU to itself.
"""
from __future__ import annotations

import httpx


def summarize(transcript_markdown: str, *, base_url: str, model: str) -> str:
    prompt = (
        "Summarize this meeting transcript. Provide a short summary, key "
        "decisions, and action items (with owners if mentioned). Answer in the "
        "same language as the transcript.\n\n" + transcript_markdown
    )
    resp = httpx.post(
        f"{base_url}/api/generate",
        json={"model": model, "prompt": prompt, "stream": False},
        timeout=600,
    )
    resp.raise_for_status()
    return resp.json().get("response", "")
