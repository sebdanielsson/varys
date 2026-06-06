"""Entry point: run the sidecar HTTP/WebSocket server (Phase 2+)."""
from __future__ import annotations

from .config import get_settings


def main() -> None:
    import uvicorn

    s = get_settings()
    uvicorn.run("transcribe_sidecar.server.app:app", host=s.host, port=s.port, reload=False)


if __name__ == "__main__":
    main()
