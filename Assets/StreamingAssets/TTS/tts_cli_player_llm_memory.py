# XTTS + Ollama with role memory + quest state + reset commands
#
# conda activate xtts
# cd "C:\Users\gauth\OneDrive\Desktop\GitHub\CART498-GenAI\TTS"
# pip install TTS sounddevice
# python tts_cli_player_llm_memory.py

import subprocess
from collections import defaultdict, deque

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
    VOICE_MAP,
    get_output_sample_rate,
    resolve_existing_files,
)

SYSTEM_PROMPT = (
    "You are an NPC in a fantasy RPG. Reply with one short sentence under 10 words. Stay in character."
)

MEMORY_TURNS = 3
history_by_role = defaultdict(lambda: deque(maxlen=MEMORY_TURNS * 2))
quest_state_by_role = defaultdict(lambda: "not started")


def resolve_files(paths):
    return resolve_existing_files(paths, fallback=NARRATOR_FILES[:1])


def parse_role_and_text(raw):
    if ":" in raw:
        role, text = raw.split(":", 1)
        return role.strip().lower(), text.strip()
    return "narrator", raw.strip()


def update_quest_state(role, user_text):
    text = user_text.lower()
    if any(k in text for k in ["quest", "help me", "job", "task"]):
        quest_state_by_role[role] = "started"
    if any(k in text for k in ["completed", "done", "finished"]):
        quest_state_by_role[role] = "completed"


def build_prompt(role, user_text):
    state = quest_state_by_role[role]
    lines = [
        SYSTEM_PROMPT,
        f"NPC role: {role}. Quest state: {state}.",
        "Conversation:",
    ]
    for turn in history_by_role[role]:
        lines.append(turn)
    lines.append(f"Player: {user_text}")
    lines.append("NPC:")
    return "\n".join(lines).strip()


def run_ollama(prompt):
    cmd = [OLLAMA_EXE, "run", LLM_MODEL, prompt]
    try:
        result = subprocess.run(cmd, capture_output=True, text=True, encoding='utf-8', errors='replace', check=True)
    except FileNotFoundError:
        result = subprocess.run(["ollama", "run", LLM_MODEL, prompt], capture_output=True, text=True, encoding='utf-8', errors='replace', check=True)
    return result.stdout.strip()


def generate_reply(role, user_text):
    prompt = build_prompt(role, user_text)
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

    print("Memory mode ON (last 3 turns per role). Type 'quit' to exit.")
    print("Use 'role: message' (e.g., 'merchant: hello').")
    print("Commands: /reset (all), /reset <role>")
    print(f"Roles: {', '.join(VOICE_MAP.keys())}")

    while True:
        try:
            raw = input("> ").strip()
        except (EOFError, KeyboardInterrupt):
            print("\nExiting.")
            break
        if not raw:
            continue
        if raw.lower() in {"quit", "exit"}:
            print("Exiting.")
            break

        if raw.startswith("/reset"):
            parts = raw.split()
            if len(parts) == 1:
                history_by_role.clear()
                quest_state_by_role.clear()
                print("All memory cleared.")
            else:
                role = parts[1].lower()
                history_by_role.pop(role, None)
                quest_state_by_role.pop(role, None)
                print(f"Memory cleared for role: {role}")
            continue

        role, user_text = parse_role_and_text(raw)
        if role not in VOICE_MAP:
            role = "narrator"
        update_quest_state(role, user_text)
        speaker_wavs = resolve_files(VOICE_MAP[role])

        print(f"Role: {role} | Quest: {quest_state_by_role[role]}")
        print("Thinking...")
        reply = generate_reply(role, user_text)
        print(f"NPC: {reply}")

        history_by_role[role].append(f"Player: {user_text}")
        history_by_role[role].append(f"NPC: {reply}")

        print("Speaking...")
        wav = tts.tts(text=reply, speaker_wav=speaker_wavs, language=LANGUAGE)
        sd.play(np.asarray(wav, dtype=np.float32), samplerate=sample_rate)
        sd.wait()
        print("Done.")


if __name__ == "__main__":
    main()
