"""
XTTS CLI WAV generator.

Unity-friendly mode:
  - Start once, keep the model in memory.
  - Accept JSON lines over stdin and respond with JSON lines over stdout.
  - Save .wav files into an output folder (cache).

Example (manual):
  python -u tts_cli_player_basicv2.py --stdio --out-dir wavs --warmup
  {"id":1,"text":"Hello!"}
"""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
import time
from pathlib import Path
from typing import Any, Dict, Optional, Tuple

import numpy as np
import soundfile as sf
import torch
from TTS.api import TTS

from tts_cli_config import (
    INFERENCE_KWARGS,
    LANGUAGE,
    MODEL_NAME,
    NARRATOR_FILES,
    get_output_sample_rate,
    resolve_existing_files,
)


def log(message: str) -> None:
    timestamp = time.strftime("%H:%M:%S")
    print(f"[{timestamp}] {message}", file=sys.stderr, flush=True)


def resolve_files(paths):
    return resolve_existing_files(paths)


def try_get_model_device(tts: TTS) -> str:
    try:
        model = getattr(getattr(tts, "synthesizer", None), "tts_model", None)
        if model is None:
            return "unknown"
        params = list(model.parameters())
        if not params:
            return "unknown"
        return str(params[0].device)
    except Exception:
        return "unknown"


def _normalize_text(text: str) -> str:
    return " ".join(text.strip().split())


def _stable_cache_key(
    *,
    text: str,
    language: str,
    model_name: str,
    inference_kwargs: Dict[str, Any],
    speaker_wavs: Tuple[str, ...],
) -> str:
    payload = {
        "model": model_name,
        "language": language,
        "inference": inference_kwargs,
        "speaker_wavs": list(speaker_wavs),
        "text": _normalize_text(text),
    }
    return json.dumps(payload, sort_keys=True, ensure_ascii=False)


def build_fast_synthesizer(tts: TTS, speaker_wavs, language: str):
    xtts_model = getattr(getattr(tts, "synthesizer", None), "tts_model", None)
    if xtts_model is None or not hasattr(xtts_model, "get_conditioning_latents") or not hasattr(xtts_model, "inference"):
        return None

    log("Caching voice conditioning latents...")
    cache_start = time.perf_counter()
    gpt_cond_latent, speaker_embedding = xtts_model.get_conditioning_latents(
        audio_path=speaker_wavs,
        gpt_cond_len=12,
        gpt_cond_chunk_len=4,
        max_ref_length=10,
        sound_norm_refs=False,
    )
    cache_s = time.perf_counter() - cache_start
    log(f"Cached conditioning in {cache_s:.1f}s")

    def synth(text: str) -> np.ndarray:
        outputs = xtts_model.inference(
            text=text,
            language=language,
            gpt_cond_latent=gpt_cond_latent,
            speaker_embedding=speaker_embedding,
            enable_text_splitting=False,
            **INFERENCE_KWARGS,
        )
        return np.asarray(outputs["wav"], dtype=np.float32)

    return synth


def generate_wav_to_cache(
    *,
    text: str,
    out_dir: Path,
    sample_rate: int,
    speaker_wavs: Tuple[str, ...],
    fast_synth,
    tts: Optional[TTS],
) -> Tuple[Path, bool]:
    out_dir.mkdir(parents=True, exist_ok=True)

    key = _stable_cache_key(
        text=text,
        language=LANGUAGE,
        model_name=MODEL_NAME,
        inference_kwargs=INFERENCE_KWARGS,
        speaker_wavs=speaker_wavs,
    )
    stem = hashlib.sha1(key.encode("utf-8")).hexdigest()[:16]
    out_path = (out_dir / stem).with_suffix(".wav")
    if out_path.exists() and out_path.stat().st_size > 0:
        return out_path, True

    if fast_synth is not None:
        wav_np = fast_synth(text)
    else:
        if tts is None:
            raise RuntimeError("TTS model is not initialized.")
        wav = tts.tts(text=text, speaker_wav=list(speaker_wavs), language=LANGUAGE, **INFERENCE_KWARGS)
        wav_np = np.asarray(wav, dtype=np.float32)

    sf.write(str(out_path), wav_np, sample_rate)
    return out_path, False


def run_stdio_loop(*, out_dir: Path, warmup: bool, no_voice_cache: bool) -> int:
    if torch.cuda.is_available():
        try:
            torch.backends.cuda.matmul.allow_tf32 = True
            torch.backends.cudnn.allow_tf32 = True
        except Exception:
            pass

    device = "cuda" if torch.cuda.is_available() else "cpu"
    log(f"Python Torch: {torch.__version__}")
    log(f"CUDA available: {torch.cuda.is_available()}")
    log(f"Device: {device}")

    log(f"Loading model: {MODEL_NAME}")
    load_start = time.perf_counter()
    tts = TTS(MODEL_NAME, progress_bar=True).to(device)
    load_s = time.perf_counter() - load_start
    sample_rate = get_output_sample_rate(tts, default=24000)
    log(f"Model loaded in {load_s:.1f}s (model device: {try_get_model_device(tts)}); sample_rate={sample_rate}")

    speaker_wavs_list = resolve_files(NARRATOR_FILES)
    speaker_wavs = tuple(speaker_wavs_list)

    fast_synth = None
    if not no_voice_cache:
        fast_synth = build_fast_synthesizer(tts, speaker_wavs=speaker_wavs_list, language=LANGUAGE)

    if warmup:
        try:
            log("Warmup generation...")
            if fast_synth is not None:
                _ = fast_synth("warmup")
            else:
                _ = tts.tts(text="warmup", speaker_wav=speaker_wavs_list, language=LANGUAGE, **INFERENCE_KWARGS)
            log("Warmup done.")
        except Exception as exc:
            log(f"Warmup failed (continuing): {exc}")

    print(json.dumps({"type": "ready", "sampleRate": sample_rate}), flush=True)

    for line in sys.stdin:
        raw = line.strip()
        if not raw:
            continue

        request_id: Any = None
        try:
            msg = json.loads(raw)
            request_id = msg.get("id")
            cmd = (msg.get("cmd") or "").strip().lower()
            if cmd in {"quit", "exit", "shutdown"}:
                print(json.dumps({"id": request_id, "type": "shutdown"}), flush=True)
                return 0

            text = (msg.get("text") or "").strip()
            if not text:
                raise ValueError("Missing 'text'.")

            t0 = time.perf_counter()
            wav_path, cached = generate_wav_to_cache(
                text=text,
                out_dir=out_dir,
                sample_rate=sample_rate,
                speaker_wavs=speaker_wavs,
                fast_synth=fast_synth,
                tts=tts,
            )
            elapsed_ms = int((time.perf_counter() - t0) * 1000)

            print(
                json.dumps(
                    {
                        "id": request_id,
                        "type": "result",
                        "wavPath": str(wav_path.resolve()),
                        "cached": cached,
                        "sampleRate": sample_rate,
                        "elapsedMs": elapsed_ms,
                    }
                ),
                flush=True,
            )
        except Exception as exc:
            print(json.dumps({"id": request_id, "type": "error", "error": str(exc)}), flush=True)

    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description="XTTS WAV generator (Unity-friendly).")
    parser.add_argument("--stdio", action="store_true", help="Read JSON lines from stdin and write JSON lines to stdout.")
    parser.add_argument("--out-dir", default="wavs", help="Directory to save cached .wav files (created if missing).")
    parser.add_argument("--no-voice-cache", action="store_true", help="Disable caching voice conditioning latents.")
    parser.add_argument("--warmup", action="store_true", help="Run one warmup generation after load/caching.")
    args = parser.parse_args()

    out_dir = Path(args.out_dir)

    if args.stdio:
        return run_stdio_loop(out_dir=out_dir, warmup=args.warmup, no_voice_cache=args.no_voice_cache)

    log("Starting interactive mode. Use --stdio for Unity.")
    return run_stdio_loop(out_dir=out_dir, warmup=args.warmup, no_voice_cache=args.no_voice_cache)


if __name__ == "__main__":
    raise SystemExit(main())
