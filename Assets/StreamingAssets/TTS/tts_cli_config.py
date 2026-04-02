from __future__ import annotations

import os
from pathlib import Path
from typing import Dict, Iterable, List, Optional, Sequence


REPO_ROOT = Path(__file__).resolve().parent


def _env(name: str, default: str) -> str:
    value = os.getenv(name)
    return default if value is None or value == "" else value


# Core TTS settings
MODEL_NAME: str = _env("TTS_MODEL_NAME", "tts_models/multilingual/multi-dataset/xtts_v2")
LANGUAGE: str = _env("TTS_LANGUAGE", "en")

# Ollama / LLM settings (used by the *_llm*.py scripts)
LLM_MODEL: str = _env("LLM_MODEL", "llama3.2:1b")
OLLAMA_EXE: str = _env("OLLAMA_EXE", str(REPO_ROOT / "ollama" / "ollama.exe"))
OLLAMA_TIMEOUT_SECONDS: float = float(_env("OLLAMA_TIMEOUT_SECONDS", "60"))
OLLAMA_PREP_TIMEOUT_SECONDS: float = float(_env("OLLAMA_PREP_TIMEOUT_SECONDS", "900"))

# Voice reference files (XTTS voice cloning)
VOICE_DIR: Path = Path(_env("VOICE_DIR", str(REPO_ROOT / "tortoise")))


def voice(*parts: str) -> Path:
    return VOICE_DIR.joinpath(*parts)


NARRATOR_FILES: List[Path] = [
    voice("train_dotrice", "1.wav"),
    voice("train_dotrice", "2.wav"),
]

VOICE_MAP: Dict[str, List[Path]] = {
    "narrator": NARRATOR_FILES,
    "soldier": NARRATOR_FILES,
    "player": NARRATOR_FILES,
    "mad_god": [
        voice("pat2", "00100.mp3"),
        voice("pat2", "00112.mp3"),
        voice("pat2", "00130.mp3"),
        voice("pat2", "00159.mp3"),
    ],
    "merchant": [
        voice("train_lescault", "lescault_new1.wav"),
        voice("train_lescault", "lescault_new2.wav"),
    ],
    "guard": [
        voice("train_kennard", "1.wav"),
        voice("train_kennard", "2.wav"),
    ],
    "healer": [
        voice("train_grace", "1.wav"),
        voice("train_grace", "2.wav"),
    ],
}


INFERENCE_KWARGS = {
    # Conservative defaults (override per-script if desired).
    "temperature": float(_env("XTTS_TEMPERATURE", "0.25")),
    "top_p": float(_env("XTTS_TOP_P", "0.85")),
    "top_k": int(_env("XTTS_TOP_K", "50")),
    "do_sample": _env("XTTS_DO_SAMPLE", "false").lower() in {"1", "true", "yes", "y"},
    "repetition_penalty": float(_env("XTTS_REPETITION_PENALTY", "10.0")),
    "length_penalty": float(_env("XTTS_LENGTH_PENALTY", "1.0")),
}


def resolve_existing_files(
    paths: Sequence[Path],
    *,
    fallback: Optional[Sequence[Path]] = None,
    raise_if_missing: bool = True,
) -> List[str]:
    existing = [str(p) for p in paths if p.exists()]
    if existing:
        return existing
    if fallback is not None:
        fallback_existing = [str(p) for p in fallback if p.exists()]
        if fallback_existing:
            return fallback_existing
    if raise_if_missing:
        raise FileNotFoundError("No speaker wav files found. Check VOICE_DIR / VOICE_MAP.")
    return []


def get_output_sample_rate(tts, default: int = 24000) -> int:
    return int(getattr(getattr(tts, "synthesizer", None), "output_sample_rate", default))

