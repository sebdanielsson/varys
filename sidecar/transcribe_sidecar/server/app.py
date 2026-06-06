"""FastAPI app. Phase 0: health only. Phase 2 wires up control + the WS stream."""
from __future__ import annotations

import logging

from fastapi import FastAPI, WebSocket

from ..config import get_settings

logger = logging.getLogger(__name__)
app = FastAPI(title="Transcribe Sidecar", version="0.0.1")


@app.get("/health")
def health() -> dict:
    s = get_settings()
    return {"status": "ok", "model": s.model_id, "device": s.device}


@app.websocket("/ws")
async def ws(websocket: WebSocket) -> None:
    """TODO(Phase 2): stream {speaker, text, t0, t1, lang} events to the UI."""
    await websocket.accept()
    await websocket.send_json({"type": "hello", "version": app.version})
    await websocket.close()
