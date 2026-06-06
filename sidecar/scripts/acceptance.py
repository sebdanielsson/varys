"""Phase 0 acceptance: confirm CUDA works and audio loopback capture is available."""
from __future__ import annotations

import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import torch  # noqa: E402

from transcribe_sidecar.audio.capture import list_devices  # noqa: E402

print(f"torch {torch.__version__} | cuda {torch.version.cuda} | available={torch.cuda.is_available()}")
if torch.cuda.is_available():
    print(f"gpu: {torch.cuda.get_device_name(0)}")
else:
    print("gpu: NONE (CPU fallback)")

print("\naudio devices:")
print(json.dumps(list_devices(), indent=2, ensure_ascii=False))
