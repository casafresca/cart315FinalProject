# XTTS + Ollama (single narrator voice)
#
# conda activate tortoise
# cd "C:\Users\gauth\OneDrive\Desktop\GitHub\CART498-GenAI\TTS"
# pip install TTS sounddevice
# python tts_cli_player_llm.py

import subprocess
from pathlib import Path

import numpy as np
import sounddevice as sd
import torch
from TTS.api import TTS

from tts_cli_config import (
    LANGUAGE,
    LLM_MODEL,
    MODEL_NAME,
    NARRATOR_FILES,
    OLLAMA_EXE,
    get_output_sample_rate,
    resolve_existing_files,
)

SYSTEM_PROMPT = (
    "You are an NPC in a fantasy RPG. Reply with one short sentence under 10 words."
)


def resolve_files(paths):
    return resolve_existing_files(paths)


def run_ollama(prompt):
    cmd = [OLLAMA_EXE, "run", LLM_MODEL, prompt]
    try:
        result = subprocess.run(cmd, capture_output=True, text=True, encoding='utf-8', errors='replace', check=True)
    except FileNotFoundError:
        result = subprocess.run(["ollama", "run", LLM_MODEL, prompt], capture_output=True, text=True, encoding='utf-8', errors='replace', check=True)
    return result.stdout.strip()


def generate_reply(user_text):
    prompt = f"{SYSTEM_PROMPT}\nPlayer: {user_text}\nNPC:".strip()
    try:
        reply = run_ollama(prompt)
    except subprocess.CalledProcessError as e:
        return f"[Ollama error: {e.stderr.strip()}]"
    if not reply:
        return "[No reply from LLM]"
    return reply.split("\n")[0][:140]


def main():
    device = "cuda" if torch.cuda.is_available() else "cpu"
    print(f"CUDA available: {torch.cuda.is_available()}")
    print(f"Device: {device}")

    tts = TTS(MODEL_NAME, progress_bar=False).to(device)
    sample_rate = get_output_sample_rate(tts, default=24000)
    speaker_wavs = resolve_files(NARRATOR_FILES)

    print("Type text and press Enter. Type 'quit' to exit.")
    while True:
        try:
            text = input("> ").strip()
        except (EOFError, KeyboardInterrupt):
            print("\nExiting.")
            break
        if not text:
            continue
        if text.lower() in {"quit", "exit"}:
            print("Exiting.")
            break

        print("Thinking...")
        reply = generate_reply(text)
        print(f"NPC: {reply}")

        print("Speaking...")
        wav = tts.tts(text=reply, speaker_wav=speaker_wavs, language=LANGUAGE)
        sd.play(np.asarray(wav, dtype=np.float32), samplerate=sample_rate)
        sd.wait()
        print("Done.")


if __name__ == "__main__":
    main()
