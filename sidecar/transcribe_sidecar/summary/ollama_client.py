"""Post-meeting summary via a local Ollama model (Gemma 4 E2B by default).

Runs after the meeting, so the GPU is free. Requires the model to be pulled:
    ollama pull gemma4:e2b
"""
from __future__ import annotations

import httpx

SYSTEM_PROMPT = (
    "You are a meeting assistant. The transcript labels speakers as 'Me' (the user) "
    "and 'Them' (other participants). Write a concise summary in the SAME language as "
    "the transcript, using exactly these sections:\n"
    "## Summary\n## Key decisions\n## Action items (with owner if clear)\n"
    "Be faithful to the transcript; do not invent details. If a section has nothing, "
    "write '-'."
)


def available(base_url: str, timeout: float = 2.0) -> bool:
    """True if an Ollama server is reachable."""
    try:
        return httpx.get(f"{base_url}/api/version", timeout=timeout).status_code == 200
    except Exception:
        return False


def summarize(transcript_markdown: str, *, base_url: str, model: str) -> str:
    payload = {
        "model": model,
        "messages": [
            {"role": "system", "content": SYSTEM_PROMPT},
            {"role": "user", "content": transcript_markdown},
        ],
        "stream": False,
        "options": {"temperature": 0.2},
    }
    resp = httpx.post(f"{base_url}/api/chat", json=payload, timeout=600)
    resp.raise_for_status()
    return (resp.json().get("message", {}).get("content") or "").strip()


def embed(texts: list[str], *, base_url: str, model: str) -> list[list[float]]:
    """Embed a batch of texts (for semantic search). Requires `ollama pull <model>`."""
    resp = httpx.post(f"{base_url}/api/embed", json={"model": model, "input": texts}, timeout=300)
    resp.raise_for_status()
    return resp.json().get("embeddings", [])
