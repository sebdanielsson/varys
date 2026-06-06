"""Meeting library + search over the meetings directory.

Layout — one directory per meeting:
    <data_dir>/meetings/<id>/
        meta.json         id, title, created, language, engine, duration_s, utterances, has_summary, indexed
        transcript.json   structured utterances
        transcript.md     markdown
        transcript.srt    subtitles
        summary.md        generated notes (optional)
        index.json        semantic-search chunk embeddings (optional)
"""
from __future__ import annotations

import json
import logging
import shutil
from datetime import datetime
from pathlib import Path

import numpy as np

from .config import Settings
from .summary.ollama_client import embed
from .transcript.store import Transcript

logger = logging.getLogger(__name__)


def meetings_root(s: Settings) -> Path:
    return Path(s.data_dir) / "meetings"


def _read_json(p: Path) -> dict:
    return json.loads(p.read_text(encoding="utf-8"))


def _write_text(p: Path, text: str) -> None:
    # Always UTF-8 (no BOM) with LF line endings, so files are portable.
    p.write_text(text.replace("\r\n", "\n").replace("\r", "\n"), encoding="utf-8", newline="\n")


def _write_json(p: Path, data: dict) -> None:
    _write_text(p, json.dumps(data, ensure_ascii=False, indent=2))


def _derive_title(transcript: Transcript) -> str:
    for u in transcript.utterances:
        t = u.text.strip()
        if len(t) >= 12:
            return (t[:48] + "…") if len(t) > 48 else t
    return "Untitled meeting"


# --- CRUD -------------------------------------------------------------------

def save_meeting(transcript: Transcript, *, settings: Settings, language: str, engine: str,
                 created: str | None = None) -> dict:
    d = meetings_root(settings) / transcript.title
    d.mkdir(parents=True, exist_ok=True)
    _write_text(d / "transcript.json", transcript.to_json())
    _write_text(d / "transcript.md", transcript.to_markdown())
    _write_text(d / "transcript.srt", transcript.to_srt())
    dur = transcript.utterances[-1].end if transcript.utterances else 0.0
    meta = {
        "id": transcript.title,
        "title": _derive_title(transcript),
        "created": created or datetime.now().isoformat(timespec="seconds"),
        "language": language,
        "engine": engine,
        "duration_s": round(dur, 1),
        "utterances": len(transcript.utterances),
        "has_summary": False,
        "indexed": False,
    }
    _write_json(d / "meta.json", meta)
    return meta


def list_meetings(settings: Settings) -> list[dict]:
    root = meetings_root(settings)
    if not root.exists():
        return []
    metas = []
    for d in root.iterdir():
        if d.name.startswith(".") or not (d / "meta.json").exists():
            continue
        try:
            metas.append(_read_json(d / "meta.json"))
        except Exception:
            logger.warning("skipping unreadable meeting %s", d.name)
    metas.sort(key=lambda m: m.get("created", ""), reverse=True)
    return metas


def load_meeting(settings: Settings, mid: str) -> dict | None:
    d = meetings_root(settings) / mid
    if not (d / "meta.json").exists():
        return None

    def _opt(name: str) -> str:
        p = d / name
        return p.read_text(encoding="utf-8") if p.exists() else ""

    return {
        "meta": _read_json(d / "meta.json"),
        "transcript": _read_json(d / "transcript.json") if (d / "transcript.json").exists() else {},
        "transcript_md": _opt("transcript.md"),
        "summary": _opt("summary.md"),
        "dir": str(d),
    }


def set_summary(settings: Settings, mid: str, summary: str) -> bool:
    d = meetings_root(settings) / mid
    if not (d / "meta.json").exists():
        return False
    _write_text(d / "summary.md", summary)
    meta = _read_json(d / "meta.json")
    meta["has_summary"] = True
    _write_json(d / "meta.json", meta)
    return True


def rename_meeting(settings: Settings, mid: str, title: str) -> bool:
    d = meetings_root(settings) / mid
    if not (d / "meta.json").exists():
        return False
    meta = _read_json(d / "meta.json")
    meta["title"] = title.strip() or meta["title"]
    _write_json(d / "meta.json", meta)
    return True


def delete_meeting(settings: Settings, mid: str) -> bool:
    """Move a meeting to <root>/.trash (reversible)."""
    root = meetings_root(settings)
    d = root / mid
    if not d.exists():
        return False
    trash = root / ".trash"
    trash.mkdir(parents=True, exist_ok=True)
    dest = trash / mid
    if dest.exists():
        shutil.rmtree(dest, ignore_errors=True)
    shutil.move(str(d), str(dest))
    return True


# --- search -----------------------------------------------------------------

def search_keyword(settings: Settings, query: str, limit: int = 25) -> list[dict]:
    q = query.lower().strip()
    if not q:
        return []
    hits = []
    for meta in list_meetings(settings):
        tj = meetings_root(settings) / meta["id"] / "transcript.json"
        if not tj.exists():
            continue
        for u in _read_json(tj).get("utterances", []):
            if q in u.get("text", "").lower():
                hits.append({"meeting": meta, "speaker": u.get("speaker"), "text": u.get("text"),
                             "start": u.get("start"), "score": 1.0})
                break  # best single snippet per meeting
    return hits[:limit]


def _chunks(utterances: list[dict], max_chars: int = 400) -> list[dict]:
    """Group consecutive utterances into ~max_chars chunks for embedding."""
    chunks: list[dict] = []
    cur, start, speaker = "", None, None
    for u in utterances:
        if start is None:
            start, speaker = u.get("start"), u.get("speaker")
        cur = (cur + " " + u.get("text", "")).strip()
        if len(cur) >= max_chars:
            chunks.append({"text": cur, "start": start, "speaker": speaker})
            cur, start, speaker = "", None, None
    if cur:
        chunks.append({"text": cur, "start": start, "speaker": speaker})
    return chunks


def _doc_text(s: Settings, text: str) -> str:
    # embeddinggemma uses asymmetric retrieval prompts; harmless for other models.
    return f"title: none | text: {text}" if "embeddinggemma" in s.embed_model else text


def _query_text(s: Settings, text: str) -> str:
    return f"task: search result | query: {text}" if "embeddinggemma" in s.embed_model else text


def index_meeting(settings: Settings, mid: str) -> bool:
    """Build and store semantic-search embeddings for one meeting."""
    d = meetings_root(settings) / mid
    if not (d / "transcript.json").exists():
        return False
    chunks = _chunks(_read_json(d / "transcript.json").get("utterances", []))
    if not chunks:
        return False
    vecs = embed([_doc_text(settings, c["text"]) for c in chunks],
                 base_url=settings.ollama_url, model=settings.embed_model)
    for c, v in zip(chunks, vecs):
        c["vec"] = v
    _write_json(d / "index.json", {"model": settings.embed_model, "chunks": chunks})
    meta = _read_json(d / "meta.json")
    meta["indexed"] = True
    _write_json(d / "meta.json", meta)
    return True


def search_semantic(settings: Settings, query: str, limit: int = 15) -> list[dict]:
    if not query.strip():
        return []
    qv = np.asarray(embed([_query_text(settings, query)], base_url=settings.ollama_url,
                          model=settings.embed_model)[0], dtype=np.float32)
    qv /= np.linalg.norm(qv) + 1e-8
    hits = []
    for meta in list_meetings(settings):
        ip = meetings_root(settings) / meta["id"] / "index.json"
        if not ip.exists():
            continue
        for c in _read_json(ip).get("chunks", []):
            v = np.asarray(c["vec"], dtype=np.float32)
            score = float(np.dot(qv, v / (np.linalg.norm(v) + 1e-8)))
            hits.append({"meeting": meta, "speaker": c.get("speaker"), "text": c["text"],
                         "start": c.get("start"), "score": round(score, 4)})
    hits.sort(key=lambda h: h["score"], reverse=True)
    return hits[:limit]
