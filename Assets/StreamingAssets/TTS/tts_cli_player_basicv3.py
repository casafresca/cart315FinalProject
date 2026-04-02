import sys
import os
import json
import time
import traceback
import subprocess

import numpy as np
import torch
import soundfile as sf
from TTS.api import TTS

from rag_memory import PDFMemoryRAG
from tts_cli_config import (
    INFERENCE_KWARGS,
    LANGUAGE,
    LLM_MODEL,
    MODEL_NAME,
    NARRATOR_FILES,
    OLLAMA_EXE,
    OLLAMA_PREP_TIMEOUT_SECONDS,
    OLLAMA_TIMEOUT_SECONDS,
    RAG_CACHE_DIR,
    RAG_CHUNK_CHARS,
    RAG_CHUNK_OVERLAP,
    RAG_CONTRADICTION_PROBABILITY,
    RAG_ENABLED,
    RAG_GLITCH_PROBABILITY,
    RAG_MIN_CHUNK_CHARS,
    RAG_RANDOM_SEED,
    RAG_RESEARCH_DIR,
    RAG_TOP_K,
    get_output_sample_rate,
    resolve_existing_files,
)


def log(message: str):
    timestamp = time.strftime("%H:%M:%S")
    print(f"[{timestamp}] {message}", flush=True)


SYSTEM_PROMPT = (
    "You are a war-torn PTSD survivor from the Korean War era. Speak in brief, emotionally grounded lines. "
    "You carry fragmented memories, fear responses, and moral conflict, but avoid graphic sensationalism. "
    "Do not provide medical advice. Keep replies under 400 characters and avoid stage directions."
)


rag_engine = None


def run_ollama(prompt: str) -> str:
    cmd = [OLLAMA_EXE, "run", LLM_MODEL, prompt]
    try:
        result = subprocess.run(
            cmd,
            stdin=subprocess.DEVNULL,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            check=True,
            timeout=OLLAMA_TIMEOUT_SECONDS,
        )
    except FileNotFoundError:
        result = subprocess.run(
            ["ollama", "run", LLM_MODEL, prompt],
            stdin=subprocess.DEVNULL,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            check=True,
            timeout=OLLAMA_TIMEOUT_SECONDS,
        )
    return result.stdout.strip()


def ensure_ollama_model() -> None:
    log(f"Preparing Ollama model: {LLM_MODEL}")
    cmd = [OLLAMA_EXE, "pull", LLM_MODEL]
    try:
        result = subprocess.run(
            cmd,
            stdin=subprocess.DEVNULL,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            check=True,
            timeout=OLLAMA_PREP_TIMEOUT_SECONDS,
        )
    except FileNotFoundError:
        result = subprocess.run(
            ["ollama", "pull", LLM_MODEL],
            stdin=subprocess.DEVNULL,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            check=True,
            timeout=OLLAMA_PREP_TIMEOUT_SECONDS,
        )

    for line in (result.stdout or "").splitlines():
        if line.strip():
            log(f"Ollama: {line.strip()}")


def generate_reply(user_text: str) -> str:
    memory_context = ""
    if rag_engine is not None:
        memory_context = rag_engine.build_prompt_context(user_text)

    prompt = f"{SYSTEM_PROMPT}\n{memory_context}\nPlayer: {user_text}\nNPC:"
    try:
        reply = run_ollama(prompt)
    except subprocess.TimeoutExpired as exc:
        raise RuntimeError(
            f"Ollama timed out after {OLLAMA_TIMEOUT_SECONDS:g}s. Is the Ollama app/server running?"
        ) from exc
    except FileNotFoundError as exc:
        raise RuntimeError(
            f"Ollama executable was not found at '{OLLAMA_EXE}' and was not available on PATH."
        ) from exc
    except subprocess.CalledProcessError as exc:
        details = (exc.stderr or exc.stdout or str(exc)).strip()
        raise RuntimeError(f"Ollama failed: {details}") from exc

    if not reply:
        raise RuntimeError("Ollama returned an empty reply.")

    return reply.split("\n")[0][:400]


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
    out_dir = os.path.join(script_dir, "wavs")
    for i, arg in enumerate(sys.argv):
        if arg == "--out-dir" and i + 1 < len(sys.argv):
            requested = sys.argv[i + 1]
            out_dir = requested if os.path.isabs(requested) else os.path.join(script_dir, requested)

    os.makedirs(out_dir, exist_ok=True)
    out_dir = os.path.abspath(out_dir)
    log(f"Output directory: {out_dir}")

    ensure_ollama_model()

    global rag_engine
    rag_engine = PDFMemoryRAG(
        research_dir=RAG_RESEARCH_DIR,
        cache_dir=RAG_CACHE_DIR,
        logger=log,
        enabled=RAG_ENABLED,
        top_k=RAG_TOP_K,
        chunk_chars=RAG_CHUNK_CHARS,
        chunk_overlap=RAG_CHUNK_OVERLAP,
        min_chunk_chars=RAG_MIN_CHUNK_CHARS,
        glitch_probability=RAG_GLITCH_PROBABILITY,
        contradiction_probability=RAG_CONTRADICTION_PROBABILITY,
        seed=RAG_RANDOM_SEED,
    )
    rag_engine.initialize()

    tts, sample_rate, speaker_wavs = initialize()

    ready_msg = {"type": "ready", "sampleRate": sample_rate}
    print(json.dumps(ready_msg), flush=True)
    log("Ready message sent to Unity")

    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue

        log(f"Received line: {line}")

        try:
            msg = json.loads(line)
        except Exception:
            log(f"Invalid JSON: {line}")
            continue

        if msg.get("cmd") == "quit":
            log("Shutting down.")
            break

        request_id = msg.get("id")
        user_text = msg.get("text", "")
        if not user_text:
            continue

        log(f"Request {request_id}: {user_text}")
        try:
            reply_text = generate_reply(user_text)
            log(f"Generated reply: {reply_text}")
            start_time = time.perf_counter()

            wav = tts.tts(
                text=reply_text,
                speaker_wav=speaker_wavs,
                language=LANGUAGE,
                **INFERENCE_KWARGS,
            )
            wav_np = np.asarray(wav, dtype=np.float32)

            out_path = os.path.abspath(os.path.join(out_dir, f"tts_{request_id}_{int(time.time() * 1000)}.wav"))
            sf.write(out_path, wav_np, sample_rate)
            log(f"Wrote WAV file: {out_path} ({os.path.getsize(out_path)} bytes)")
            elapsed_ms = int((time.perf_counter() - start_time) * 1000)

            response = {
                "type": "result",
                "id": request_id,
                "wavPath": out_path,
                "sampleRate": sample_rate,
                "replyText": reply_text,
                "cached": False,
                "elapsedMs": elapsed_ms,
            }
            print(json.dumps(response), flush=True)
            log(f"Result sent for request {request_id}")

        except Exception as e:
            log(f"Error: {e}")
            traceback.print_exc()
            error_response = {"type": "error", "id": request_id, "error": str(e)}
            print(json.dumps(error_response), flush=True)


if __name__ == "__main__":
    main()
