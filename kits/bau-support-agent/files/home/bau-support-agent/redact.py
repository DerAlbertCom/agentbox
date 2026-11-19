"""
redact.py — PII redaction for BAU ticket text before it reaches Bedrock/Anthropic.

Why this exists: BAU tickets are frequently forwarded emails, and forwarded
emails carry signature blocks (name, direct phone number, sometimes a personal
e-mail address). See CS #39082 and the 2026-09-15 sample review (#38664 is a
clean example: name + mobile number in a signature). Automatically generated
tickets (DQC reports etc.) carry deal/counterparty identifiers, which are
business-confidential but not personal data — this module does not touch
those, only the categories below.

Design, and its deliberate limit:
- EMAIL_ADDRESS, PHONE_NUMBER, IBAN_CODE have a fixed shape: Presidio's
  built-in pattern/checksum recognizers catch these deterministically. No
  fuzziness here.
- PERSON does not have a fixed shape ("Joey" is just a capitalized word) —
  the only way to find it is NLP-based NER (Presidio's spaCy-backed
  recognizer). This is best-effort by construction: an unusual name or a
  non-Western name can be missed. There is no regex or dictionary that closes
  this gap; NER is the industry-standard tool for it (used because it's the
  best available method, not because it's perfect). Blind truncation (e.g.
  cutting everything after a "Regards," line) was deliberately rejected
  2026-09-15 — it risks silently dropping ticket content that happens to sit
  below a signature line, which is worse than an occasional missed name.
- Tickets are bilingual (English and German, e.g. #39288 is German). A single
  spaCy model would either miss non-English names entirely or (worse, if the
  wrong-language model is applied) misfire constantly on German's
  capitalize-every-noun orthography. So the analyzer runs BOTH the English and
  German NLP engines and merges the results.

Logging: only entity type + count go to the logger, never the matched text —
this module's own logs must not become a new PII leak.
"""
from __future__ import annotations

import logging
from functools import lru_cache

logger = logging.getLogger("bau_support_agent.redact")

# PERSON is the only ML/NER-based entity here; EMAIL_ADDRESS, PHONE_NUMBER and
# IBAN_CODE are deterministic pattern/checksum recognizers built into Presidio
# and fire identically regardless of which language's NLP engine is active.
_ENTITIES = ["PERSON", "EMAIL_ADDRESS", "PHONE_NUMBER", "IBAN_CODE"]

# Languages the NLP engine is configured for. Order matters only for which
# recognizer "wins" a fully-overlapping span (arbitrary, since anonymization
# only needs entity type + span for those).
_LANGUAGES = ["en", "de"]


@lru_cache(maxsize=1)
def _get_engines():
    """Lazily build the Presidio analyzer/anonymizer (spaCy model load is slow).

    Configures one AnalyzerEngine whose NlpEngine knows both en_core_web_sm and
    de_core_news_sm, so analyze() can be called once per language and the
    results merged, instead of building a whole new engine per language.
    """
    from presidio_analyzer import AnalyzerEngine
    from presidio_analyzer.nlp_engine import NlpEngineProvider
    from presidio_anonymizer import AnonymizerEngine

    nlp_configuration = {
        "nlp_engine_name": "spacy",
        "models": [
            {"lang_code": "en", "model_name": "en_core_web_sm"},
            {"lang_code": "de", "model_name": "de_core_news_sm"},
        ],
    }
    nlp_engine = NlpEngineProvider(nlp_configuration=nlp_configuration).create_engine()
    analyzer = AnalyzerEngine(nlp_engine=nlp_engine, supported_languages=_LANGUAGES)
    return analyzer, AnonymizerEngine()


def _merge_results(results_by_language: list[list]) -> list:
    """Merge per-language analyzer results, dropping fully-duplicate spans.

    Two languages' recognizers can both fire on the same span (e.g. the
    regex-based EMAIL_ADDRESS/PHONE_NUMBER recognizers run for every
    language). Keep one result per (start, end, entity_type) triple.
    """
    seen: set[tuple[int, int, str]] = set()
    merged = []
    for results in results_by_language:
        for r in results:
            key = (r.start, r.end, r.entity_type)
            if key in seen:
                continue
            seen.add(key)
            merged.append(r)
    return merged


def redact_text(text: str) -> str:
    """Return *text* with detected PII replaced by `<ENTITY_TYPE>` placeholders.

    Empty/falsy input is returned unchanged. Runs the analyzer once per
    configured language (see module docstring for why) and merges the results
    before anonymizing once.

    Any analyzer failure is logged and the ORIGINAL text is returned — a
    visible failure (unredacted text reaching the caller, which the caller can
    choose to block on) is preferable to this function silently swallowing
    ticket content on an unexpected error.
    """
    if not text:
        return text

    try:
        analyzer, anonymizer = _get_engines()
        results_by_language = [
            analyzer.analyze(text=text, language=lang, entities=_ENTITIES)
            for lang in _LANGUAGES
        ]
        results = _merge_results(results_by_language)
    except Exception:
        logger.exception("redact_text: analyzer failed, returning original text unredacted")
        return text

    if results:
        counts: dict[str, int] = {}
        for r in results:
            counts[r.entity_type] = counts.get(r.entity_type, 0) + 1
        logger.info(
            "redact_text: redacted %s",
            ", ".join(f"{k}={v}" for k, v in sorted(counts.items())),
        )

    anonymized = anonymizer.anonymize(text=text, analyzer_results=results)
    return anonymized.text
