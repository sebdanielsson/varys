"""In-memory transcript with JSON / Markdown / SRT exporters."""
from __future__ import annotations

import json
from dataclasses import asdict, dataclass, field
from pathlib import Path


@dataclass
class Utterance:
    speaker: str           # "Me" | "Them"
    text: str
    start: float           # seconds since meeting start
    end: float
    language: str | None = None


@dataclass
class Transcript:
    title: str = "meeting"
    utterances: list[Utterance] = field(default_factory=list)

    def add(self, u: Utterance) -> None:
        self.utterances.append(u)

    def to_json(self) -> str:
        return json.dumps(
            {"title": self.title, "utterances": [asdict(u) for u in self.utterances]},
            ensure_ascii=False,
            indent=2,
        )

    def to_markdown(self) -> str:
        lines = [f"# {self.title}", ""]
        for u in self.utterances:
            lines.append(f"**{u.speaker}** [{_hms(u.start)}]: {u.text}")
        return "\n".join(lines) + "\n"

    def to_srt(self) -> str:
        blocks = []
        for i, u in enumerate(self.utterances, 1):
            blocks.append(
                f"{i}\n{_srt_ts(u.start)} --> {_srt_ts(u.end)}\n{u.speaker}: {u.text}\n"
            )
        return "\n".join(blocks)

    def save(self, directory: str) -> dict[str, str]:
        d = Path(directory)
        d.mkdir(parents=True, exist_ok=True)
        paths = {
            "json": d / f"{self.title}.json",
            "md": d / f"{self.title}.md",
            "srt": d / f"{self.title}.srt",
        }
        paths["json"].write_text(self.to_json(), encoding="utf-8", newline="\n")
        paths["md"].write_text(self.to_markdown(), encoding="utf-8", newline="\n")
        paths["srt"].write_text(self.to_srt(), encoding="utf-8", newline="\n")
        return {k: str(v) for k, v in paths.items()}


def _hms(s: float) -> str:
    s = int(s)
    return f"{s // 3600:02d}:{(s % 3600) // 60:02d}:{s % 60:02d}"


def _srt_ts(s: float) -> str:
    ms = int((s - int(s)) * 1000)
    s = int(s)
    return f"{s // 3600:02d}:{(s % 3600) // 60:02d}:{s % 60:02d},{ms:03d}"
