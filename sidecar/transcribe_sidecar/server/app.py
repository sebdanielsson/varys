"""FastAPI service: REST control + a WebSocket stream of transcription events.

The Session runs the pipeline on background threads and emits events through a
thread-safe bridge onto the asyncio loop, which fans them out to WS clients.
"""
from __future__ import annotations

import asyncio
import logging
from contextlib import asynccontextmanager
from pathlib import Path

from fastapi import FastAPI, WebSocket, WebSocketDisconnect
from pydantic import BaseModel

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
    paths = await asyncio.get_running_loop().run_in_executor(None, sess.stop)
    app.state.last_transcript = sess.transcript
    app.state.session = None
    return {"status": "stopped", "files": paths}


@app.post("/session/summarize")
async def session_summarize() -> dict:
    sess = app.state.session
    transcript = (sess.transcript if sess and sess.transcript.utterances
                  else app.state.last_transcript)
    if transcript is None or not transcript.utterances:
        return {"status": "no_transcript"}

    s = get_settings()
    if not ollama_available(s.ollama_url):
        return {"status": "error", "message": "Ollama is not reachable; start it and pull the model."}

    md = transcript.to_markdown()
    loop = asyncio.get_running_loop()
    try:
        summary = await loop.run_in_executor(
            None, lambda: summarize(md, base_url=s.ollama_url, model=s.summary_model))
    except Exception as ex:  # e.g. model not pulled
        return {"status": "error", "message": str(ex)}

    _broadcast_threadsafe({"type": "summary", "text": summary})
    try:
        sp = Path(s.transcript_dir) / f"{transcript.title}.summary.md"
        sp.parent.mkdir(parents=True, exist_ok=True)
        sp.write_text(summary, encoding="utf-8")
    except Exception:
        logger.exception("failed to save summary")
    return {"status": "ok", "summary": summary, "model": s.summary_model}


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
