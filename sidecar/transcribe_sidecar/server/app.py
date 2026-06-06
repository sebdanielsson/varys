"""FastAPI service: REST control + a WebSocket stream of transcription events.

The Session runs the pipeline on background threads and emits events through a
thread-safe bridge onto the asyncio loop, which fans them out to WS clients.
"""
from __future__ import annotations

import asyncio
import logging
from contextlib import asynccontextmanager

from fastapi import FastAPI, WebSocket, WebSocketDisconnect
from pydantic import BaseModel

from .. import library
from ..config import get_settings
from ..session import Session
from ..summary.ollama_client import available as ollama_available, summarize

logger = logging.getLogger(__name__)


@asynccontextmanager
async def lifespan(app: FastAPI):
    app.state.loop = asyncio.get_running_loop()
    yield
    if app.state.session and app.state.session.running:
        app.state.session.stop()


app = FastAPI(title="Transcribe Sidecar", version="0.1.0", lifespan=lifespan)
app.state.session = None
app.state.clients = set()
app.state.loop = None
app.state.last_transcript = None


class StartRequest(BaseModel):
    language: str | None = None  # auto | en | sv


def _broadcast_threadsafe(event: dict) -> None:
    """Called from Session threads; hop onto the event loop to fan out to clients."""
    loop = app.state.loop
    if loop is not None:
        loop.call_soon_threadsafe(lambda: asyncio.ensure_future(_broadcast(event)))


async def _broadcast(event: dict) -> None:
    for ws in list(app.state.clients):
        try:
            await ws.send_json(event)
        except Exception:
            app.state.clients.discard(ws)


@app.get("/health")
def health() -> dict:
    sess = app.state.session
    return {"status": "ok", "running": bool(sess and sess.running),
            "engine": sess.engine_label if sess else None}


@app.post("/session/start")
async def session_start(req: StartRequest | None = None) -> dict:
    sess = app.state.session
    if sess and sess.running:
        return {"status": "already_running", "engine": sess.engine_label}
    s = get_settings()
    if req and req.language:
        s = s.model_copy(update={"language": req.language})
    session = Session(s, on_event=_broadcast_threadsafe)
    app.state.session = session
    # model load + thread start is blocking -> run off the event loop
    status = await asyncio.get_running_loop().run_in_executor(None, session.start)
    return {"status": "started", **status}


@app.post("/session/stop")
async def session_stop() -> dict:
    sess = app.state.session
    if not sess or not sess.running:
        return {"status": "idle"}
    meta = await asyncio.get_running_loop().run_in_executor(None, sess.stop)
    app.state.session = None
    if meta.get("id"):
        asyncio.create_task(_index_async(meta["id"]))   # background semantic indexing
    return {"status": "stopped", "meeting": meta}


async def _index_async(mid: str) -> None:
    """Best-effort background semantic indexing (no-op if Ollama/model unavailable)."""
    s = get_settings()
    try:
        await asyncio.get_running_loop().run_in_executor(None, lambda: library.index_meeting(s, mid))
    except Exception:
        logger.info("semantic indexing skipped for %s", mid, exc_info=True)


class RenameRequest(BaseModel):
    title: str


@app.get("/meetings")
def meetings_list() -> dict:
    return {"meetings": library.list_meetings(get_settings())}


@app.get("/meetings/{mid}")
def meeting_get(mid: str) -> dict:
    return library.load_meeting(get_settings(), mid) or {"status": "not_found"}


@app.patch("/meetings/{mid}")
def meeting_rename(mid: str, req: RenameRequest) -> dict:
    return {"status": "ok" if library.rename_meeting(get_settings(), mid, req.title) else "not_found"}


class TextBody(BaseModel):
    text: str


@app.put("/meetings/{mid}/transcript")
async def meeting_set_transcript(mid: str, body: TextBody) -> dict:
    s = get_settings()
    ok = await asyncio.get_running_loop().run_in_executor(
        None, lambda: library.save_transcript(s, mid, body.text))
    return {"status": "ok" if ok else "not_found"}


@app.put("/meetings/{mid}/notes")
def meeting_set_notes(mid: str, body: TextBody) -> dict:
    return {"status": "ok" if library.set_notes(get_settings(), mid, body.text) else "not_found"}


@app.delete("/meetings/{mid}")
def meeting_delete(mid: str) -> dict:
    return {"status": "deleted" if library.delete_meeting(get_settings(), mid) else "not_found"}


@app.post("/meetings/{mid}/summarize")
async def meeting_summarize(mid: str) -> dict:
    s = get_settings()
    m = library.load_meeting(s, mid)
    if not m:
        return {"status": "not_found"}
    if not ollama_available(s.ollama_url):
        return {"status": "error", "message": "Ollama is not reachable; start it and pull the model."}
    loop = asyncio.get_running_loop()
    try:
        summary = await loop.run_in_executor(
            None, lambda: summarize(m["transcript_md"], base_url=s.ollama_url, model=s.summary_model))
    except Exception as ex:
        return {"status": "error", "message": str(ex)}
    library.set_summary(s, mid, summary)
    _broadcast_threadsafe({"type": "summary", "meeting_id": mid, "text": summary})
    return {"status": "ok", "summary": summary, "model": s.summary_model}


@app.post("/meetings/{mid}/index")
async def meeting_index(mid: str) -> dict:
    s = get_settings()
    try:
        ok = await asyncio.get_running_loop().run_in_executor(None, lambda: library.index_meeting(s, mid))
    except Exception as ex:
        return {"status": "error", "message": str(ex)}
    return {"status": "ok" if ok else "skipped"}


@app.get("/search")
async def search(q: str = "", mode: str = "keyword", limit: int = 20) -> dict:
    s = get_settings()
    loop = asyncio.get_running_loop()
    if mode == "semantic":
        if not ollama_available(s.ollama_url):
            return {"status": "error", "message": "Ollama is not reachable for semantic search."}
        try:
            hits = await loop.run_in_executor(None, lambda: library.search_semantic(s, q, limit))
        except Exception as ex:
            return {"status": "error", "message": str(ex)}
    else:
        hits = await loop.run_in_executor(None, lambda: library.search_keyword(s, q, limit))
    return {"status": "ok", "mode": mode, "hits": hits}


@app.post("/session/summarize")
async def session_summarize() -> dict:
    """Compat shim: summarize the most recent meeting."""
    metas = library.list_meetings(get_settings())
    if not metas:
        return {"status": "no_transcript"}
    return await meeting_summarize(metas[0]["id"])


@app.websocket("/ws")
async def ws(websocket: WebSocket) -> None:
    await websocket.accept()
    app.state.clients.add(websocket)
    sess = app.state.session
    await websocket.send_json({"type": "status",
                               "state": "listening" if (sess and sess.running) else "idle"})
    try:
        while True:
            await websocket.receive_text()  # ignore inbound; keeps the socket open
    except WebSocketDisconnect:
        pass
    finally:
        app.state.clients.discard(websocket)
