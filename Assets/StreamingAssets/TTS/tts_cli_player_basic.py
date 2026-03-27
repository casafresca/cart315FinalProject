# XTTS basic live reader
#
# conda activate tortoise
# cd "C:\Users\gauth\OneDrive\Desktop\GitHub\CART498-GenAI\TTS"
# pip install TTS sounddevice
# python tts_cli_player_basic.py

import time
import argparse

import numpy as np
import sounddevice as sd
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
    print(f"[{timestamp}] {message}", flush=True)


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


def main():
    parser = argparse.ArgumentParser(description="XTTS CLI player (interactive or one-shot).")
    parser.add_argument(
        "text",
        nargs="*",
        help="If provided, speaks this text once and exits. If omitted, runs interactive mode.",
    )
    args = parser.parse_args()
    one_shot_text = " ".join(args.text).strip() if args.text else None

    device = "cuda" if torch.cuda.is_available() else "cpu"
    log(f"Python Torch: {torch.__version__}")
    log(f"CUDA available: {torch.cuda.is_available()}")
    log(f"Device: {device}")
    if torch.cuda.is_available():
        try:
            log(f"CUDA device count: {torch.cuda.device_count()}")
            log(f"CUDA device 0: {torch.cuda.get_device_name(0)}")
        except Exception as exc:
            log(f"CUDA device query failed: {exc}")

    log(f"Loading model: {MODEL_NAME}")
    load_start = time.perf_counter()
    tts = TTS(MODEL_NAME, progress_bar=True).to(device)
    load_s = time.perf_counter() - load_start
    log(f"Model loaded in {load_s:.1f}s (model device: {try_get_model_device(tts)})")
    sample_rate = get_output_sample_rate(tts, default=24000)
    log(f"Output sample rate: {sample_rate}")
    speaker_wavs = resolve_files(NARRATOR_FILES)
    log(f"Inference settings: {INFERENCE_KWARGS}")

    if one_shot_text:
        log(f"Speaking once: {one_shot_text}")
        gen_start = time.perf_counter()
        wav = tts.tts(text=one_shot_text, speaker_wav=speaker_wavs, language=LANGUAGE, **INFERENCE_KWARGS)
        gen_s = time.perf_counter() - gen_start
        wav_np = np.asarray(wav, dtype=np.float32)
        log(f"Generated in {gen_s:.1f}s; audio {wav_np.size/sample_rate:.2f}s; playing...")
        sd.play(wav_np, samplerate=sample_rate)
        sd.wait()
        log("Done.")
        return

    log("Type text and press Enter. Type 'quit' to exit.")
    while True:
        try:
            text = input("> ").strip()
        except (EOFError, KeyboardInterrupt):
            log("Exiting.")
            break
        if not text:
            continue
        if text.lower() in {"quit", "exit"}:
            log("Exiting.")
            break

        log("Generating...")
        gen_start = time.perf_counter()
        wav = tts.tts(text=text, speaker_wav=speaker_wavs, language=LANGUAGE, **INFERENCE_KWARGS)
        gen_s = time.perf_counter() - gen_start
        wav_np = np.asarray(wav, dtype=np.float32)
        log(f"Generated in {gen_s:.1f}s; audio {wav_np.size/sample_rate:.2f}s; playing...")
        sd.play(wav_np, samplerate=sample_rate)
        sd.wait()
        log("Done.")


if __name__ == "__main__":
    main()
