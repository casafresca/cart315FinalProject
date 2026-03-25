# tts_unity_preload_safe.py
import sys
import os
import traceback
import warnings

# Optional AI layer
try:
    import ollama
    USE_OLLAMA = True
except ImportError:
    USE_OLLAMA = False

from TTS.api import TTS

# Ignore harmless PyTorch warnings
warnings.filterwarnings("ignore", category=UserWarning)

# --- CONFIG ---
VOICE_MAP = {
    "narrator": "voices/narrator.wav",
    "merchant": "voices/merchant.wav",
    "guard": "voices/guard.wav",
    "healer": "voices/healer.wav"
}

OUTPUT_FILE = "output.wav"

# --- INIT ---
try:
    tts = TTS("tts_models/multilingual/multi-dataset/xtts_v2")
except Exception as e:
    print("error: Failed to load TTS model:", e)
    sys.exit(1)

print("ready")
sys.stdout.flush()

# --- MAIN LOOP ---
while True:
    try:
        line = input()
        if not line.strip():
            continue

        # Expect format: character:text
        if ":" not in line:
            print("error: Invalid format, expected character:text")
            sys.stdout.flush()
            continue

        character, text = line.split(":", 1)
        character = character.strip()
        text = text.strip()

        if character not in VOICE_MAP:
            print(f"error: Unknown character '{character}'")
            sys.stdout.flush()
            continue

        # --- Optional AI response ---
        if USE_OLLAMA:
            try:
                response = ollama.generate(model="llama2", prompt=text)
                text = response.get("response", text)
            except Exception as ai_e:
                print(f"error: Ollama failed: {ai_e}")
                sys.stdout.flush()
                # fallback: just use original text

        # --- Generate TTS ---
        try:
            tts.tts_to_file(
                text=text,
                speaker_wav=VOICE_MAP[character],
                file_path=OUTPUT_FILE
            )
        except Exception as tts_e:
            print(f"error: TTS generation failed: {tts_e}")
            sys.stdout.flush()
            continue

        # Make sure file exists before signaling Unity
        if not os.path.exists(OUTPUT_FILE):
            print("error: TTS output file missing")
            sys.stdout.flush()
            continue

        print("ready")
        sys.stdout.flush()

    except EOFError:
        break  # exit cleanly on Ctrl-D / Unity process killed
    except Exception as e:
        traceback.print_exc()
        print(f"error: {e}")
        sys.stdout.flush()