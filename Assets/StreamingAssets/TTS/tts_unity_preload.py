# XTTS pre-loaded for Unity integration
#
# This script loads the TTS model once and waits for input in the format "character:text"
# It generates speech for the specified character and saves to output.wav
# Prints "ready" when done, then waits for next input.

import sys
import time
import os

import numpy as np
import torch
from TTS.api import TTS
from scipy.io.wavfile import write

from tts_cli_config import (
    INFERENCE_KWARGS,
    LANGUAGE,
    MODEL_NAME,
    VOICE_MAP,
    get_output_sample_rate,
    resolve_existing_files,
)


def log(message: str) -> None:
    timestamp = time.strftime("%H:%M:%S")
    print(f"[{timestamp}] {message}", flush=True)


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
    log(f"Inference settings: {INFERENCE_KWARGS}")
    log("TTS pre-loaded. Waiting for input in format 'character:text'")

    while True:
        try:
            line = input().strip()
        except (EOFError, KeyboardInterrupt):
            log("Exiting.")
            break
        if not line:
            continue
        if line.lower() in {"quit", "exit"}:
            log("Exiting.")
            break

        try:
            character, text = line.split(":", 1)
            character = character.strip().lower()
            text = text.strip()
        except ValueError:
            log("Invalid input format. Use 'character:text'")
            continue

        if character not in VOICE_MAP:
            log(f"Unknown character: {character}. Available: {list(VOICE_MAP.keys())}")
            continue

        speaker_wavs = resolve_existing_files(VOICE_MAP[character])
        if not speaker_wavs:
            log(f"No voice files found for {character}")
            continue

        log(f"Generating for {character}: {text}")
        gen_start = time.perf_counter()
        wav = tts.tts(text=text, speaker_wav=speaker_wavs, language=LANGUAGE, **INFERENCE_KWARGS)
        gen_s = time.perf_counter() - gen_start
        wav_np = np.asarray(wav, dtype=np.float32)
        log(f"Generated in {gen_s:.1f}s; audio {wav_np.size/sample_rate:.2f}s")

        # Save to output.wav
        write("output.wav", sample_rate, (wav_np * 32767).astype(np.int16))
        print("ready", flush=True)


if __name__ == "__main__":
    main()</content>
<parameter name="filePath">c:\Users\chief\OneDrive\Documents\GitHub\cart315FinalProject\Assets\StreamingAssets\TTS\tts_unity_preload.py