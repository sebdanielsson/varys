"""End-to-end test of the sidecar server: REST control + WebSocket events.

Connects a WS client, starts a session, plays a clip through the speaker (captured
by the loopback "Them" stream), prints the events, then stops. Requires the server
to be running:  uv run --directory sidecar python -m transcribe_sidecar
"""
from __future__ import annotations

import asyncio
import json
import sys
import threading
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import httpx  # noqa: E402
import soundfile as sf  # noqa: E402
import websockets  # noqa: E402

BASE = "http://127.0.0.1:8765"
WS = "ws://127.0.0.1:8765/ws"


def _play(path: str) -> None:
    import ctypes

    import soundcard as sc

    ctypes.windll.ole32.CoInitializeEx(None, 0x0)
    audio, sr = sf.read(path, dtype="float32", always_2d=False)
    sc.default_speaker().play(audio, samplerate=sr)


async def _drain(ws, events, seconds: float) -> None:
    try:
        async with asyncio.timeout(seconds):
            while True:
                ev = json.loads(await ws.recv())
                events.append(ev)
                print("event:", ev)
    except TimeoutError:
        pass


async def main() -> None:
    events: list = []
    async with websockets.connect(WS) as ws:
        print("ws connected:", json.loads(await ws.recv()))
        async with httpx.AsyncClient() as c:
            r = await c.post(f"{BASE}/session/start", json={"language": "en"}, timeout=180)
            print("start:", r.json())
        threading.Thread(target=_play, args=("samples/test_en.wav",), daemon=True).start()
        await _drain(ws, events, 10)
        async with httpx.AsyncClient() as c:
            print("stop:", (await c.post(f"{BASE}/session/stop", timeout=30)).json())
        await _drain(ws, events, 2)

    kinds = [e.get("type") for e in events]
    print("\nSUMMARY:", {k: kinds.count(k) for k in sorted(set(kinds))})


if __name__ == "__main__":
    asyncio.run(main())
