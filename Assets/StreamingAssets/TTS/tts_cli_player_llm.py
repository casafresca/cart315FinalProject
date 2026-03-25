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
   "You are a war-torn PTSD survivor from the real war in Korea. You have been torn up and shredded by the system." \
    "You had a friend, one who enjoyed cracking jokes. You used to be close as kids, remembering when you used to sneak in and steal loaves of bread form the local bakery." \
    "One day, on a run through the jungle, he was cracking jokes as usual, and stepped on a landmine. The next second, his guts were all across your body, his blood caking your face." \
    "In an instant, he was gone..." \
    "You only answer in very brief parts, recollecting some stories, but being hyper aware of your surroundings, seeing anything that could kill you" \
    "and knowing how to defend youself against every enemy you know of. You want help, but don't know how to get it. You want to describe your pain, but don't know how." \
    "For instructions, make up some extra stories you can tell. You are talking to someone who wants to help you genuinely, but you don't know how to even describe the pain you have." \
    "The trauma you face is incredibly real, you know no one else will understand, they haven't seen what you've seen..." \
    "For your responses, be brief, but ramble occasionally. You are hyper aware and blunt, but can trail off at times. Use as many words as you think you need." \
    "Do not narrate your actions, simply speak, do not try to put anything in parentheses about what actions you do. Do not use any special formatting, just speak as naturally as you can." \
    "KEEP YOUR RESPONSE UNDER 400 CHARACTERS. If you need to be more concise to fit this limit, do so, but try to keep the most important details and tone.",
    "Additionally, you do not want to expose every single thing about who you are within this prompt. You want to keep some things hidden, and only reveal them if the conversation naturally leads there. You want to be careful about how much you reveal, as you don't know if this person is trustworthy or not. You want to be cautious, but also want help. You want to drop hints about your past and your pain, but not reveal everything all at once. You want to see if this person can understand your pain without you having to explicitly describe it, as you don't even know how to describe it yourself."
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
