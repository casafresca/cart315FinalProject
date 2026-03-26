# tts_unity_live.py
import sys
import warnings
import subprocess
import numpy as np
import torch
from TTS.api import TTS
import sounddevice as sd

# ------------------------------
# CONFIG
# ------------------------------
VOICE_MAP = {
    "narrator": "voices/narrator.wav",
    "merchant": "voices/merchant.wav",
    "guard": "voices/guard.wav",
    "healer": "voices/healer.wav"
}

SYSTEM_PROMPT = (
    "You are a war-torn PTSD survivor from the Korean war. "
    "Keep responses brief and hyper-aware. Answer naturally in-character."
)

LLM_MODEL = "llama2"
OLLAMA_EXE = "ollama"  # path to Ollama CLI if not in PATH

warnings.filterwarnings("ignore", category=UserWarning)

# ------------------------------
# INIT
# ------------------------------
device = "cuda" if torch.cuda.is_available() else "cpu"
print(f"Device: {device}", flush=True)

try:
    tts_model = TTS("tts_models/multilingual/multi-dataset/xtts_v2", progress_bar=False).to(device)
except Exception as e:
    print(f"error: Failed to load TTS: {e}", flush=True)
    sys.exit(1)

print("ready", flush=True)

# ------------------------------
# HELPERS
# ------------------------------
def run_ollama(prompt):
    cmd = [OLLAMA_EXE, "run", LLM_MODEL, prompt]
    try:
        result = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", check=True)
    except FileNotFoundError:
        result = subprocess.run(["ollama", "run", LLM_MODEL, prompt], capture_output=True, text=True, encoding="utf-8", check=True)
    return result.stdout.strip()

def generate_reply(context, player_text):
    prompt = f"{SYSTEM_PROMPT}\n{context}\nPlayer: {player_text}\nNPC:"
    try:
        reply = run_ollama(prompt)
    except subprocess.CalledProcessError as e:
        return f"[Ollama error: {e.stderr.strip()}]"
    if not reply:
        return "[No reply from LLM]"
    return reply.split("\n")[0][:400]

def parse_role_text(raw):
    if ":" in raw:
        role, text = raw.split(":", 1)
        return role.strip().lower(), text.strip()
    return "narrator", raw.strip()

# ------------------------------
# MAIN LOOP
# ------------------------------
conversation_context = ""
while True:
    try:
        raw = input().strip()
    except (EOFError, KeyboardInterrupt):
        break

    if not raw:
        continue
    if raw.lower() in {"quit", "exit"}:
        break

    role, player_text = parse_role_text(raw)
    if role not in VOICE_MAP:
        role = "narrator"

    # generate LLM reply
    reply = generate_reply(conversation_context, player_text)
    conversation_context += f"\nPlayer: {player_text}\nNPC: {reply}"

    # generate TTS audio in-memory
    try:
        speaker_wav = VOICE_MAP[role]
        wav_array = tts_model.tts(text=reply, speaker_wav=speaker_wav, language="en")
    except Exception as e:
        print(f"error: TTS failed: {e}", flush=True)
        continue

    # play audio immediately
    try:
        sd.play(np.asarray(wav_array, dtype=np.float32), samplerate=24000)
        sd.wait()
    except Exception as e:
        print(f"error: audio playback failed: {e}", flush=True)
        continue

    # signal Unity that speaking is done
    print("ready", flush=True)