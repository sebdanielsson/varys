"""End-to-end test of the meeting library + search (no server, no ASR).

Creates two sample meetings, saves them, then exercises list / load / index /
keyword search / semantic search. Leaves the meetings in the library as demo data.
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from transcribe_sidecar import library  # noqa: E402
from transcribe_sidecar.config import get_settings  # noqa: E402
from transcribe_sidecar.transcript.store import Transcript, Utterance  # noqa: E402


def make(title: str, lang: str, lines: list[tuple[str, str]]) -> Transcript:
    t = Transcript(title=title)
    clock = 0.0
    for speaker, text in lines:
        t.add(Utterance(speaker, text, clock, clock + 3.0, lang))
        clock += 3.5
    return t


def main() -> None:
    s = get_settings()
    print("meetings root:", library.meetings_root(s))

    m_en = make("meeting-test-en-001", "en", [
        ("Them", "Let's decide the release date for version two of the product."),
        ("Me", "I propose next Friday, and I'll handle the changelog."),
        ("Them", "Great, I'll book the demo and tell the team about the database migration."),
    ])
    m_sv = make("meeting-test-sv-001", "sv", [
        ("Them", "Vi maste prata om budgeten for marknadsforing nasta kvartal."),
        ("Me", "Jag tycker vi okar budgeten med tio procent for kampanjen."),
        ("Them", "Okej, jag bokar ett mote med ekonomiavdelningen om det."),
    ])
    for t, lang in [(m_en, "en"), (m_sv, "sv")]:
        meta = library.save_meeting(t, settings=s, language=lang, engine="test")
        print("saved:", meta["id"], "| title:", meta["title"])

    print("\n--- list_meetings ---")
    for m in library.list_meetings(s):
        print(f"  {m['id']}  [{m['language']}]  {m['utterances']} utt  '{m['title']}'")

    print("\n--- index (embeddings) ---")
    for m in library.list_meetings(s):
        print("  indexed", m["id"], library.index_meeting(s, m["id"]))

    print("\n--- keyword search: 'changelog' ---")
    for h in library.search_keyword(s, "changelog"):
        print(f"  [{h['meeting']['id']}] {h['speaker']}: {h['text']}")

    print("\n--- semantic: 'when are we shipping the product?' ---")
    for h in library.search_semantic(s, "when are we shipping the product?", limit=3):
        print(f"  {h['score']:.3f} [{h['meeting']['id']}] {h['text'][:72]}")

    print("\n--- semantic (Swedish): 'kostnader och pengar' ---")
    for h in library.search_semantic(s, "kostnader och pengar", limit=3):
        print(f"  {h['score']:.3f} [{h['meeting']['id']}] {h['text'][:72]}")


if __name__ == "__main__":
    main()
