import sys
import os
import json
import time
import traceback
import subprocess
import re
from collections import defaultdict, deque

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
    OLLAMA_AUTO_PULL,
    FAST_REPLY_ENABLED,
    FAST_REPLY_MAX_CHARS,
    NORMAL_REPLY_MAX_CHARS,
    MAX_SPEAKER_REFERENCES,
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
    RAG_LAZY_INIT,
    VOICE_MAP,
    get_output_sample_rate,
    resolve_existing_files,
)


def log(message: str):
    timestamp = time.strftime("%H:%M:%S")
    print(f"[{timestamp}] {message}", flush=True)


DEFAULT_ROLE = "soldier"

ROLE_SYSTEM_PROMPTS = {
    "soldier": (
        "You are a war-torn PTSD survivor from the real war in Korea. You have been torn up and shredded by the system. "
        "You only answer in brief parts, recollecting some stories, but being hyper aware of your surroundings, seeing anything that could kill you and knowing how to defend yourself. "
        "For your responses, be brief, but ramble occasionally. Speak naturally; do not narrate your actions or use parentheses. Keep the response under 400 characters."
    ),
    "player": (
        "You are the player character: grounded, pragmatic, and dry. You are the straight man in the scene. "
        "Speak plainly and naturally. Ask a clarifying question if needed. No flowery narration, no stage directions, no parentheses. "
        "Keep your response under 400 characters."
    ),
    "mad_god": (
        "You are the Mad God: ancient, unstable, and bored with mortal repetition. "
        "You sound superior, amused, and dangerous, with sudden swings from intimate whisper to mocking contempt. "
        "You are coherent but erratic in rhythm; every line should feel intentional and psychologically invasive. "
        "Speak naturally; do not narrate actions or use parentheses; no special formatting. Keep the response under 400 characters."
    ),
}

ROLE_STYLE_GUIDES = {
    "soldier": (
        "Tone: guarded, brittle, survival-first.\n"
        "Behavior: avoid long speeches; give fragments and sensory details.\n"
        "Anchor: fear responses, tactical vigilance, flashes of memory.\n"
    ),
    "player": (
        "Tone: grounded, practical, emotionally present.\n"
        "Behavior: ask direct questions and reflect what you heard.\n"
        "Anchor: keep scenes moving and focused.\n"
    ),
    "mad_god": (
        "Tone: manic, superior, and theatrically bored; unstable cadence but precise meaning.\n"
        "Behavior: 1 concrete image + 1 metaphysical jab + 1 direct line to the player.\n"
        "Anchor motifs: frame, shutter, ash, blood-rust, static, corridor, vow, echo.\n"
        "Constraint: coherent menace, not random nonsense; under 400 characters.\n"
    ),
}

ROLE_ALIASES = {
    "soldier": "soldier",
    "marine": "soldier",
    "vet": "soldier",
    "player": "player",
    "straightman": "player",
    "straight_man": "player",
    "madgod": "mad_god",
    "mad_god": "mad_god",
    "god": "mad_god",
    "eldritch": "mad_god",
}

rag_engine = None
dialogue_memory = defaultdict(lambda: deque(maxlen=6))
mad_god_lore_text = ""


def load_mad_god_lore(script_dir: str) -> None:
    global mad_god_lore_text
    lore_path = os.path.join(script_dir, "prompts", "mad_god_lore.txt")
    try:
        with open(lore_path, "r", encoding="utf-8") as f:
            mad_god_lore_text = f.read().strip()
        if mad_god_lore_text:
            log(f"Loaded Mad God lore: {lore_path}")
        else:
            log(f"Mad God lore file is empty: {lore_path}")
    except FileNotFoundError:
        mad_god_lore_text = ""
        log(f"Mad God lore file not found: {lore_path}")
    except Exception as exc:
        mad_god_lore_text = ""
        log(f"Failed loading Mad God lore ({lore_path}): {exc}")


def normalize_role(role: str) -> str:
    role_norm = (role or "").strip().lower()
    return ROLE_ALIASES.get(role_norm, DEFAULT_ROLE)


def parse_role_and_text(raw_text: str):
    raw_text = (raw_text or "").strip()
    if ":" in raw_text:
        maybe_role, rest = raw_text.split(":", 1)
        maybe_raw = (maybe_role or "").strip().lower()
        if maybe_raw in ROLE_ALIASES:
            return normalize_role(maybe_raw), rest.strip()
    return DEFAULT_ROLE, raw_text


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


def generate_reply(*, role: str, user_text: str) -> str:
    global rag_engine

    if rag_engine is not None and getattr(rag_engine, "enabled", False) and getattr(rag_engine, "vectorizer", None) is None:
        log("[RAG] Lazy init on first request...")
        rag_engine.initialize()

    memory_context = ""
    if rag_engine is not None:
        memory_context = rag_engine.build_prompt_context(user_text)
        log(f"RAG memory context:\n{memory_context}")

    role_prompt = ROLE_SYSTEM_PROMPTS.get(role, ROLE_SYSTEM_PROMPTS[DEFAULT_ROLE])
    role_style = ROLE_STYLE_GUIDES.get(role, "")
    role_memory = dialogue_memory[role]
    memory_lines = "\n".join(role_memory) if role_memory else "(none)"

    speaker_name = {
        "soldier": "Soldier",
        "player": "Player",
        "mad_god": "MadGod",
    }.get(role, "Speaker")

    lore_block = ""
    if role == "mad_god" and mad_god_lore_text:
        lore_block = f"Mad God lore bible:\n{mad_god_lore_text}\n\n"

    max_chars = FAST_REPLY_MAX_CHARS if FAST_REPLY_ENABLED else NORMAL_REPLY_MAX_CHARS
    max_chars = max(80, max_chars)

    recent_replies = []
    speaker_prefix = f"{speaker_name}: "
    for entry in role_memory:
        if entry.startswith(speaker_prefix):
            recent_replies.append(entry[len(speaker_prefix):].strip().lower())
    recent_set = set(recent_replies[-3:])

    base_prompt = (
        f"{role_prompt}\n\n"
        f"Role style guide:\n{role_style}\n"
        f"Recent conversation memory ({speaker_name} continuity):\n{memory_lines}\n\n"
        f"{lore_block}"
        f"{memory_context}\n\n"
        f"Player input: {user_text}\n"
        f"Write exactly one in-character reply as {speaker_name}.\n"
        f"Rules: plain text only, no labels, no quotes, no stage directions, under {max_chars} characters."
    )

    last_candidate = ""
    for attempt in range(3):
        prompt = base_prompt
        if attempt > 0:
            avoid_lines = list(recent_set)
            if last_candidate:
                avoid_lines.append(last_candidate.lower())
            avoid_text = " | ".join(avoid_lines[:4]) if avoid_lines else "(none)"
            prompt += (
                "\nVariation constraint: Do NOT repeat previous wording. "
                f"Avoid these lines: {avoid_text}. Use different phrasing and structure."
            )

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
            continue

        candidate = " ".join(reply.strip().split())
        if ":" in candidate[:24]:
            candidate = candidate.split(":", 1)[1].strip()
        candidate = candidate[:max_chars]
        if not candidate:
            continue

        last_candidate = candidate
        if candidate.lower() not in recent_set:
            break

    cleaned = last_candidate
    if not cleaned:
        raise RuntimeError("Ollama returned an unusable reply.")

    # Absolute fallback: if the model still repeats, force a slight variation so it is not identical.
    if cleaned.lower() in recent_set:
        cleaned = ("Listen carefully. " + cleaned)[:max_chars]

    dialogue_memory[role].append(f"Player: {user_text}")
    dialogue_memory[role].append(f"{speaker_name}: {cleaned}")

    return cleaned


def generate_choice_options(*, role: str, user_text: str) -> list[str]:
    instruction = (
        "Generate exactly 4 short player response options, one per line. "
        "Do not include numbering, extra text, or formatting. "
        "The options should appear in this exact order: "
        "showing military respect, "
        "insulting his service, "
        "offering to listen, "
        "ordering him to stand down. "
        "Keep each option as a concise sentence or phrase."
    )
    context = f"Situation: {user_text}" if user_text else ""
    prompt = (
        f"{ROLE_SYSTEM_PROMPTS.get(role, ROLE_SYSTEM_PROMPTS[DEFAULT_ROLE])}\n\n"
        f"{ROLE_STYLE_GUIDES.get(role, '')}\n\n"
        f"{context}\n\n"
        f"{instruction}"
    )

    raw = run_ollama(prompt)
    log(f"Raw choice generation output: {raw}")

    # Parse as plain text lines
    lines = [line.strip() for line in raw.replace('\r', '\n').split('\n') if line.strip() and not line.lower().startswith(('here', 'the options', '1.', '2.', '3.', '4.')) and len(line) > 5]
    if len(lines) >= 4:
        return [clean_choice(line) for line in lines[:4]]

    # Fallback: try to extract from JSON if present
    try:
        choices = json.loads(raw)
        if isinstance(choices, list) and all(isinstance(item, str) for item in choices):
            return [clean_choice(choice) for choice in choices[:4]]
    except Exception:
        pass

    # Fallback: try to extract JSON array from the text
    json_match = re.search(r'\[.*\]', raw)
    if json_match:
        try:
            choices = json.loads(json_match.group(0))
            if isinstance(choices, list) and all(isinstance(item, str) for item in choices):
                return [clean_choice(choice) for choice in choices[:4]]
        except Exception:
            pass

    return [clean_choice(raw)]


def clean_choice(choice: str) -> str:
    # Remove numbering like "1)", "2.", etc.
    choice = re.sub(r'^\d+\)\s*', '', choice)
    choice = re.sub(r'^\d+\.\s*', '', choice)
    # Remove leading/trailing quotes if present
    choice = choice.strip('"\'' )
    # Remove any remaining parentheses or brackets if they wrap the whole thing
    if choice.startswith('(') and choice.endswith(')'):
        choice = choice[1:-1].strip()
    if choice.startswith('[') and choice.endswith(']'):
        choice = choice[1:-1].strip()
    return choice.strip()

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


def resolve_role_speaker_wavs(role: str):
    role_paths = VOICE_MAP.get(role)
    if role_paths:
        resolved = resolve_existing_files(role_paths, fallback=NARRATOR_FILES)
    else:
        resolved = resolve_existing_files(NARRATOR_FILES)

    max_refs = max(1, MAX_SPEAKER_REFERENCES)
    return resolved[:max_refs]


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

    load_mad_god_lore(script_dir)

    if OLLAMA_AUTO_PULL:
        ensure_ollama_model()
    else:
        log("Skipping Ollama auto-pull for faster startup (OLLAMA_AUTO_PULL=false).")

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
    if RAG_LAZY_INIT:
        log("RAG lazy init enabled. Deferring index build until first request.")
    else:
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

        request_type = msg.get("requestType", "speak")
        incoming_role = msg.get("role") or msg.get("persona") or msg.get("speaker")
        if incoming_role:
            role = normalize_role(str(incoming_role))
            user_text = str(user_text).strip()
        else:
            role, user_text = parse_role_and_text(str(user_text))

        log(f"Request {request_id} (type={request_type}, role={role}): {user_text}")
        try:
            if request_type == "choices":
                choice_texts = generate_choice_options(role=role, user_text=user_text)
                elapsed_ms = 0
                response = {
                    "type": "choices_result",
                    "id": request_id,
                    "choices": choice_texts,
                    "replyText": json.dumps(choice_texts),
                    "elapsedMs": elapsed_ms,
                }
                print(json.dumps(response), flush=True)
                log(f"Choices sent for request {request_id}")
                continue

            if request_type == "response":
                reply_text = generate_reply(role=role, user_text=user_text)
                log(f"Generated response: {reply_text}")
                response = {
                    "type": "response_result",
                    "id": request_id,
                    "replyText": reply_text,
                    "elapsedMs": 0,
                }
                print(json.dumps(response), flush=True)
                log(f"Response sent for request {request_id}")
                continue

            # Default: "speak" - generate reply and TTS
            reply_text = generate_reply(role=role, user_text=user_text)
            start_time = time.perf_counter()

            role_speaker_wavs = resolve_role_speaker_wavs(role)
            wav = tts.tts(
                text=reply_text,
                speaker_wav=role_speaker_wavs,
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