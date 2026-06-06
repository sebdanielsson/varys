"""Test the Ollama summary path on a transcript (Phase 5).

Usage: python scripts/summarize_test.py [path-to-transcript.md]
Falls back to a built-in Swedish sample if no path is given (checks that the
summary comes back in the transcript's language).
"""
from __future__ import annotations

import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from transcribe_sidecar.config import get_settings  # noqa: E402
from transcribe_sidecar.summary.ollama_client import available, summarize  # noqa: E402

SAMPLE = """# meeting
**Them** [00:00]: Hej, ska vi borja? Vi behover bestamma releasedatumet.
**Me** [00:05]: Ja. Jag foreslar nasta fredag, den trettonde.
**Them** [00:10]: Funkar for mig. Kan du fixa changelogen innan dess?
**Me** [00:14]: Absolut, jag tar changelogen. Vi kor en demo pa torsdag.
**Them** [00:20]: Bra. Da bokar jag in demon och meddelar teamet.
"""


def main(argv: list[str]) -> int:
    s = get_settings()
    if not available(s.ollama_url):
        print("Ollama not reachable at", s.ollama_url)
        return 1
    md = Path(argv[1]).read_text(encoding="utf-8") if len(argv) > 1 else SAMPLE
    print(f"summarizing with {s.summary_model} ...\n")
    t = time.perf_counter()
    out = summarize(md, base_url=s.ollama_url, model=s.summary_model)
    print(out)
    print(f"\n({time.perf_counter() - t:.1f}s)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
