import sys
import os
import json
import time
import traceback

import numpy as np
import torch
import soundfile as sf
from TTS.api import TTS

from tts_cli_config import (
    INFERENCE_KWARGS,
    LANGUAGE,
    MODEL_NAME,
    NARRATOR_FILES,
    get_output_sample_rate,
    resolve_existing_files,
)

def log(message: str):
    timestamp = time.strftime("%H:%M:%S")
    print(f"[{timestamp}] {message}", flush=True)

# -------------------------
# Initialize TTS
# -------------------------
def initialize():
    device = "cuda" if torch.cuda.is_available() else "cpu"
    log(f"Torch: {torch.__version__}, CUDA: {torch.cuda.is_available()}, Device: {device}")
    log(f"Loading model: {MODEL_NAME}")
    start = time.perf_counter()
    tts = TTS(MODEL_NAME).to(device)
    load_time = time.perf_counter() - start
    log(f"Loaded in {load_time:.2f}s")
    sample_rate = get_output_sample_rate(tts, default=24000)
    speaker_wavs = resolve_existing_files(NARRATOR_FILES)
    return tts, sample_rate, speaker_wavs

# -------------------------
# Main server loop
# -------------------------
def main():
    script_dir = os.path.dirname(os.path.abspath(__file__))
    out_dir = os.path.join(script_dir, "wavs")  # default output inside this script folder
    for i, arg in enumerate(sys.argv):
        if arg == "--out-dir" and i + 1 < len(sys.argv):
            requested = sys.argv[i + 1]
            out_dir = requested if os.path.isabs(requested) else os.path.join(script_dir, requested)
    os.makedirs(out_dir, exist_ok=True)
    out_dir = os.path.abspath(out_dir)
    log(f"Output directory: {out_dir}")

    tts, sample_rate, speaker_wavs = initialize()

    # ✅ Send READY to Unity
    ready_msg = {"type": "ready", "sampleRate": sample_rate}
    print(json.dumps(ready_msg), flush=True)

    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue

        try:
            msg = json.loads(line)
        except Exception:
            log(f"Invalid JSON: {line}")
            continue

        # Shutdown
        if msg.get("cmd") == "quit":
            log("Shutting down.")
            break

        request_id = msg.get("id")
        text = msg.get("text", "")
        if not text:
            continue

        log(f"Request {request_id}: {text}")
        start_time = time.perf_counter()

        try:
            # Generate audio
            wav = tts.tts(
                text=text,
                speaker_wav=speaker_wavs,
                language=LANGUAGE,
                **INFERENCE_KWARGS
            )
            wav_np = np.asarray(wav, dtype=np.float32)

            # Save permanently
            out_path = os.path.abspath(os.path.join(out_dir, f"tts_{request_id}_{int(time.time()*1000)}.wav"))
            sf.write(out_path, wav_np, sample_rate)
            log(f"Wrote WAV file: {out_path} ({os.path.getsize(out_path)} bytes)")
            elapsed_ms = int((time.perf_counter() - start_time) * 1000)

            # ✅ Send result to Unity
            response = {
                "type": "result",
                "id": request_id,
                "wavPath": out_path,  # absolute path
                "sampleRate": sample_rate,
                "cached": False,
                "elapsedMs": elapsed_ms
            }
            print(json.dumps(response), flush=True)

        except Exception as e:
            log(f"Error: {e}")
            traceback.print_exc()
            error_response = {"type": "error", "id": request_id, "error": str(e)}
            print(json.dumps(error_response), flush=True)

if __name__ == "__main__":
    main()