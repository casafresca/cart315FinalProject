import sys
import os
import json
import time
import traceback
import subprocess
import re
from collections import defaultdict, deque
from urllib import error as urlerror
from urllib import request as urlrequest

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
        "You are a war-torn survivor of a brutal campaign. You have been torn up and shredded by the system. "
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
soldier_profiles_by_id = {}
soldier_profiles_by_name = {}
soldier_fragments_by_profile = defaultdict(list)


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


def normalize_lookup_key(value: str) -> str:
    return re.sub(r"[^a-z0-9]+", "", (value or "").strip().lower())


def load_soldier_prompt_data(script_dir: str) -> None:
    global soldier_profiles_by_id
    global soldier_profiles_by_name
    global soldier_fragments_by_profile

    profiles_path = os.path.join(script_dir, "prompts", "soldier_profiles.json")
    fragments_path = os.path.join(script_dir, "prompts", "soldier_testimony_fragments.json")

    soldier_profiles_by_id = {}
    soldier_profiles_by_name = {}
    soldier_fragments_by_profile = defaultdict(list)

    try:
        with open(profiles_path, "r", encoding="utf-8") as f:
            profile_data = json.load(f)

        for profile in profile_data.get("profiles", []):
            if not isinstance(profile, dict):
                continue
            profile_id = str(profile.get("id") or "").strip()
            soldier_name = str(profile.get("soldierName") or "").strip()
            if profile_id:
                soldier_profiles_by_id[profile_id] = profile
            if soldier_name:
                soldier_profiles_by_name[normalize_lookup_key(soldier_name)] = profile

        #log(f"Loaded soldier profiles: {len(soldier_profiles_by_id)}")
    except FileNotFoundError:
        log(f"Soldier profile file not found: {profiles_path}")
    except Exception as exc:
        log(f"Failed loading soldier profiles ({profiles_path}): {exc}")

    try:
        with open(fragments_path, "r", encoding="utf-8") as f:
            fragment_data = json.load(f)

        for fragment in fragment_data.get("fragments", []):
            if not isinstance(fragment, dict):
                continue
            profile_id = str(fragment.get("profileId") or "").strip()
            if not profile_id:
                continue
            soldier_fragments_by_profile[profile_id].append(fragment)

        # log(f"Loaded soldier testimony fragments: {sum(len(v) for v in soldier_fragments_by_profile.values())}")
    except FileNotFoundError:
        log(f"Soldier testimony fragment file not found: {fragments_path}")
    except Exception as exc:
        log(f"Failed loading soldier testimony fragments ({fragments_path}): {exc}")


def resolve_soldier_profile(payload: dict) -> dict | None:
    profile_id = str(payload.get("profileId") or payload.get("id") or "").strip()
    if profile_id and profile_id in soldier_profiles_by_id:
        return soldier_profiles_by_id[profile_id]

    for candidate in (payload.get("soldierName"), payload.get("speakerName")):
        lookup = normalize_lookup_key(str(candidate or ""))
        if lookup and lookup in soldier_profiles_by_name:
            return soldier_profiles_by_name[lookup]

    return None


def coalesce_profile_field(payload: dict, profile: dict | None, field_name: str, fallback: str = "") -> str:
    payload_value = str(payload.get(field_name) or "").strip()
    if payload_value:
        return payload_value
    if profile is not None:
        profile_value = str(profile.get(field_name) or "").strip()
        if profile_value:
            return profile_value
    return fallback


def choose_profile_fragments(profile: dict | None, stage: str, seed_text: str, limit: int = 3) -> list[dict]:
    if not profile:
        return []

    profile_id = str(profile.get("id") or "").strip()
    fragments = soldier_fragments_by_profile.get(profile_id) or []
    if not fragments:
        return []

    lowered_seed = (seed_text or "").lower()
    scored = []

    for fragment in fragments:
        score = 0
        trigger = str(fragment.get("trigger") or "").replace("_", " ").lower()
        memory_type = str(fragment.get("memoryType") or "").strip().lower()
        identity_theme = str(fragment.get("identityTheme") or "").replace("_", " ").lower()
        text = str(fragment.get("text") or "").lower()

        if trigger and trigger in lowered_seed:
            score += 4
        if identity_theme and identity_theme in lowered_seed:
            score += 2
        if any(word and word in lowered_seed for word in text.split()[:8]):
            score += 1

        if stage in ("breaking", "fractured", "delusional") and memory_type in ("late_confession", "identity_fragment", "sensory_flash"):
            score += 3
        elif stage in ("unstable", "volatile", "frayed") and memory_type in ("trigger_reaction", "body_memory", "triage_memory", "urban_memory"):
            score += 2
        elif stage in ("guarded",) and memory_type in ("identity_fragment",):
            score += 1

        scored.append((score, fragment))

    scored.sort(key=lambda item: item[0], reverse=True)
    selected = []
    seen_ids = set()
    for _, fragment in scored:
        fragment_id = str(fragment.get("id") or "").strip()
        if fragment_id in seen_ids:
            continue
        selected.append(fragment)
        seen_ids.add(fragment_id)
        if len(selected) >= limit:
            break

    return selected


def format_fragment_context(fragments: list[dict]) -> str:
    if not fragments:
        return ""

    lines = []
    for fragment in fragments:
        memory_type = str(fragment.get("memoryType") or "fragment").strip()
        emotion = str(fragment.get("emotion") or "").strip()
        identity_theme = str(fragment.get("identityTheme") or "").replace("_", " ").strip()
        text = str(fragment.get("text") or "").strip()
        parts = [memory_type]
        if emotion:
            parts.append(emotion)
        if identity_theme:
            parts.append(identity_theme)
        lines.append(f"- {' / '.join(parts)}: {text}")

    return "\n".join(lines)


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
    return run_ollama_with_options(prompt)


def run_ollama_with_options(prompt: str, *, temperature: float | None = None, force_json: bool = False) -> str:
    payload = {
        "model": LLM_MODEL,
        "prompt": prompt,
        "stream": False,
    }
    if force_json:
        payload["format"] = "json"
    if temperature is not None:
        payload["options"] = {"temperature": max(0.0, min(2.0, float(temperature)))}

    try:
        req = urlrequest.Request(
            "http://127.0.0.1:11434/api/generate",
            data=json.dumps(payload).encode("utf-8"),
            headers={"Content-Type": "application/json"},
            method="POST",
        )
        with urlrequest.urlopen(req, timeout=OLLAMA_TIMEOUT_SECONDS) as response:
            body = response.read().decode("utf-8", errors="replace")
        parsed = json.loads(body)
        reply = (parsed.get("response") or "").strip()
        if reply:
            return reply
    except (urlerror.URLError, TimeoutError, json.JSONDecodeError, ValueError):
        pass

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
    #log(f"Preparing Ollama model: {LLM_MODEL}")
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
        #log("[RAG] Lazy init on first request...")
        rag_engine.initialize()

    memory_context = ""
    if rag_engine is not None:
        memory_context = rag_engine.build_prompt_context(user_text)
        #log(f"RAG memory context:\n{memory_context}")

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
    #log(f"Raw choice generation output: {raw}")

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

    fallback_choices = [
        "Easy. I mean no disrespect.",
        "Your service did not make you untouchable.",
        "Talk to me. I am listening.",
        "Stand down and lower the weapon.",
    ]

    cleaned_raw = clean_choice(raw)
    if cleaned_raw:
        fallback_choices[0] = cleaned_raw[:90]

    log(f"[AI DEBUG][choices] using fallback choices, raw_preview={raw[:240]!r}")
    return fallback_choices


def get_insanity_stage(insanity: int) -> str:
    if insanity >= 85:
        return "breaking"
    if insanity >= 65:
        return "delusional"
    if insanity >= 40:
        return "unstable"
    if insanity >= 20:
        return "frayed"
    return "guarded"


def get_debate_temperature(insanity: int) -> float:
    clamped = max(0, min(100, insanity))
    return round(0.4 + (clamped / 100.0) * 0.95, 2)


def get_stage_behavior(stage: str) -> str:
    behaviors = {
        "guarded": (
            "He is tense but still mostly coherent. He hides behind discipline, clipped language, and suspicion. "
            "He resists empathy and keeps testing whether the player understands anything real."
        ),
        "frayed": (
            "He starts slipping between present and memory. Certain words or sounds snag him. He is irritable, ashamed, and brittle. "
            "He can still be reached, but he is beginning to misread intent."
        ),
        "unstable": (
            "He is actively losing the thread of the room. He speaks in sensory fragments, bursts of tactical logic, guilt, and disgust. "
            "He should sound more alarming, more vivid, and less socially normal."
        ),
        "delusional": (
            "He is half inside the war. He misidentifies sounds, shadows, and motives. He may treat the player as a superior officer, an enemy, a witness, or a dead comrade. "
            "He should feel frightening, intimate, and unstable without becoming random nonsense."
        ),
        "breaking": (
            "He is on the edge of total identity collapse. The line between self, enemy, command, guilt, and memory is tearing. "
            "His voice should feel like a person disappearing in real time: violent imagery, shame, grief, and combat reflex all colliding."
        ),
    }
    return behaviors.get(stage, behaviors["guarded"])


def get_reveal_goal(round_number: int, stage: str, mode: str) -> str:
    if mode == "typed":
        if round_number <= 1:
            return "resist and test the player while letting one personal crack show"
        if round_number == 2:
            return "react to a trigger or a meaningful word and let one memory fragment leak through"
        if round_number == 3:
            return "reveal a contradiction, guilt point, or hidden fear tied to identity"
        if stage in ("fractured", "breaking"):
            return "confess, misrecognize, or expose a collapsing sense of self"
        return "reveal one deeper backstory shard and show whether the player is reaching you"

    if round_number <= 1:
        return "resist, challenge, or test the player"
    if round_number == 2:
        return "show a trigger reaction or an involuntary memory slip"
    if round_number == 3:
        return "reveal a guilt point, moral wound, or identity crack"
    if stage in ("delusional", "breaking"):
        return "confess, fracture, or collapse toward a dangerous truth"
    return "push the confrontation toward either confession or partial stabilization"


def shape_emotional_speech(text: str, stage: str) -> str:
    cleaned = " ".join(str(text or "").split()).strip()
    if not cleaned:
        return cleaned

    stage_key = (stage or "").strip().lower()

    if stage_key in {"frayed", "volatile"}:
        cleaned = re.sub(r"\bI (can|could|did|do|don't|didn't|won't)\b", r"I... \1", cleaned, count=1)
        cleaned = re.sub(r"\b(no|stop|wait|listen)\b", lambda m: f"{m.group(1).capitalize()}... {m.group(1).lower()}", cleaned, count=1, flags=re.IGNORECASE)
        return cleaned[:280].strip()

    if stage_key in {"unstable", "fractured"}:
        cleaned = re.sub(r"\b(don't|stop|wait|listen|no)\b", lambda m: f"{m.group(1).capitalize()}. {m.group(1).capitalize()}.", cleaned, count=1, flags=re.IGNORECASE)
        cleaned = re.sub(r"\bI remember\b", "I remember... I remember", cleaned, count=1, flags=re.IGNORECASE)
        cleaned = re.sub(r"\bI can still\b", "I can still... I can still", cleaned, count=1, flags=re.IGNORECASE)
        cleaned = re.sub(r"\b(he|she|they) said\b", lambda m: f"{m.group(1).capitalize()} said it. {m.group(1).capitalize()} said it.", cleaned, count=1, flags=re.IGNORECASE)
        return cleaned[:280].strip()

    if stage_key in {"delusional", "breaking"}:
        cleaned = re.sub(r"\b(don't|stop|no|get down|move)\b", lambda m: f"{m.group(1).upper()}!", cleaned, count=1, flags=re.IGNORECASE)
        cleaned = re.sub(r"\bI can't\b", "I can't. I can't-", cleaned, count=1, flags=re.IGNORECASE)
        cleaned = re.sub(r"\b(not him|not her|not them)\b", lambda m: f"{m.group(1).capitalize()}. {m.group(1).capitalize()}.", cleaned, count=1, flags=re.IGNORECASE)
        return cleaned[:280].strip()

    return cleaned[:280].strip()


def clean_generated_reply_from_raw(raw: str) -> str:
    cleaned = " ".join(str(raw or "").split()).strip()
    if not cleaned:
        return ""

    cleaned = re.sub(r"^\{.*?\}$", "", cleaned).strip()
    cleaned = re.sub(r'^[\"\']|[\"\']$', "", cleaned).strip()
    cleaned = re.sub(r"^(soldier_reply|speaker_reply)\s*:\s*", "", cleaned, flags=re.IGNORECASE)
    cleaned = cleaned.strip(" ,")
    return cleaned


def pick_stage_fallback(stage: str, options: dict[str, list[str]], default_key: str = "guarded") -> str:
    stage_key = (stage or "").strip().lower()
    pool = options.get(stage_key) or options.get(default_key) or [""]
    return random.choice(pool).strip()


def get_debate_fallback_choices(insanity: int) -> list[dict]:
    stage = get_insanity_stage(insanity)
    if stage in {"delusional", "breaking"}:
        return [
            {"title": "Ground him", "text": "Look at me. You are here right now, not back there.", "tone": "ground", "insanityDelta": -16, "calmDelta": 1},
            {"title": "Validate", "text": "What happened to you was real, and you survived it.", "tone": "validate", "insanityDelta": -10, "calmDelta": 1},
            {"title": "Question", "text": "Who do you think is in danger right now?", "tone": "question", "insanityDelta": 6, "calmDelta": 0},
            {"title": "Confront", "text": "If you pull that trigger now, you prove the war still owns you.", "tone": "confront", "insanityDelta": 18, "calmDelta": -1},
        ]

    return [
        {"title": "Ground him", "text": "Breathe. This room is real. Stay with me.", "tone": "ground", "insanityDelta": -14, "calmDelta": 1},
        {"title": "Validate", "text": "You don't have to carry all of that alone.", "tone": "validate", "insanityDelta": -8, "calmDelta": 1},
        {"title": "Question", "text": "What are you seeing right now that I am not?", "tone": "question", "insanityDelta": 4, "calmDelta": 0},
        {"title": "Confront", "text": "Then say what you did instead of hiding behind the gun.", "tone": "confront", "insanityDelta": 16, "calmDelta": -1},
    ]


def normalize_debate_choices(raw_choices, insanity: int) -> list[dict]:
    tone_effects = {
        "ground": (-16, 1),
        "validate": (-10, 1),
        "question": (5, 0),
        "confront": (18, -1),
        "provoke": (20, -1),
    }

    normalized: list[dict] = []
    seen_tones = set()
    for item in raw_choices or []:
        if not isinstance(item, dict):
            continue

        tone = str(item.get("tone", "")).strip().lower()
        if tone not in tone_effects or tone in seen_tones:
            continue

        title = clean_choice(str(item.get("title", "")).strip()) or tone.title()
        text = clean_choice(str(item.get("text", "")).strip())
        if len(text) < 8:
            continue

        insanity_delta, calm_delta = tone_effects[tone]
        normalized.append(
            {
                "title": title,
                "text": text,
                "tone": tone,
                "insanityDelta": insanity_delta,
                "calmDelta": calm_delta,
            }
        )
        seen_tones.add(tone)

    for item in get_debate_fallback_choices(insanity):
        if len(normalized) >= 4:
            break
        if item["tone"] in seen_tones:
            continue
        normalized.append(item)
        seen_tones.add(item["tone"])

    return normalized[:4]


def generate_debate_turn(*, role: str, payload: dict) -> dict:
    global rag_engine

    if rag_engine is not None and getattr(rag_engine, "enabled", False) and getattr(rag_engine, "vectorizer", None) is None:
        #log("[RAG] Lazy init on first request...")
        rag_engine.initialize()

    profile = resolve_soldier_profile(payload)

    soldier_name = coalesce_profile_field(payload, profile, "soldierName", "Soldier")
    former_identity = coalesce_profile_field(payload, profile, "formerIdentity")
    military_role = coalesce_profile_field(payload, profile, "militaryRole")
    war_theater = coalesce_profile_field(payload, profile, "warTheater")
    defining_trauma = coalesce_profile_field(payload, profile, "definingTrauma")
    trigger_stimulus = coalesce_profile_field(payload, profile, "triggerStimulus")
    identity_fracture = coalesce_profile_field(payload, profile, "identityFracture")
    physical_tell = coalesce_profile_field(payload, profile, "physicalTell")
    taboo_topic = coalesce_profile_field(payload, profile, "tabooTopic")
    round_number = int(payload.get("round") or 1)
    insanity = max(0, min(100, int(payload.get("insanity") or 0)))
    stage = str(payload.get("insanityStage") or get_insanity_stage(insanity)).strip() or get_insanity_stage(insanity)
    last_player_line = str(payload.get("lastPlayerLine") or "").strip()
    last_soldier_line = str(payload.get("lastSoldierLine") or "").strip()
    scene_summary = str(payload.get("sceneSummary") or "").strip()
    recent_transcript = payload.get("recentTranscript") or []

    transcript_block = "\n".join([str(line).strip() for line in recent_transcript if str(line).strip()]) or "(none)"
    context_seed = " ".join(part for part in [last_player_line, last_soldier_line, scene_summary] if part).strip()
    memory_context = rag_engine.build_prompt_context(context_seed) if rag_engine is not None else ""
    temperature = get_debate_temperature(insanity)
    stage_behavior = get_stage_behavior(stage)
    reveal_goal = get_reveal_goal(round_number, stage, "debate")
    profile_fragment_context = format_fragment_context(choose_profile_fragments(profile, stage, context_seed))
    profile_extra_context = ""
    if profile:
        recurring_images = ", ".join(profile.get("recurringImages") or [])
        profile_extra_context = (
            f"Public persona: {str(profile.get('publicPersona') or '').strip()}\n"
            f"Hidden fear: {str(profile.get('hiddenFear') or '').strip()}\n"
            f"Moral wound: {str(profile.get('moralWound') or '').strip()}\n"
            f"Speech pattern: {str(profile.get('speechPattern') or '').strip()}\n"
            f"Recurring images: {recurring_images}\n"
            f"False belief: {str(profile.get('falseBelief') or '').strip()}\n"
            f"Refuses to admit early: {str(profile.get('refuseToAdmitEarly') or '').strip()}\n"
            f"Reveal midway: {str(profile.get('revealMidway') or '').strip()}\n"
            f"Reveal late: {str(profile.get('revealLate') or '').strip()}\n"
            f"Collapse mode: {str(profile.get('collapseMode') or '').strip()}\n"
            f"Recovery path: {str(profile.get('recoveryPath') or '').strip()}\n"
        )

    prompt = (
        f"{ROLE_SYSTEM_PROMPTS.get(role, ROLE_SYSTEM_PROMPTS[DEFAULT_ROLE])}\n\n"
        f"{ROLE_STYLE_GUIDES.get(role, '')}\n"
        "You are in a psychological debate battle with the player.\n"
        f"Current round: {round_number}\n"
        f"Current insanity: {insanity}/100\n"
        f"Current stage: {stage}\n"
        f"Character name: {soldier_name}\n"
        f"Scene summary: {scene_summary}\n"
        f"Military role: {military_role}\n"
        f"Former identity before war: {former_identity}\n"
        f"War context: {war_theater}\n"
        f"Defining trauma: {defining_trauma}\n"
        f"Trigger stimulus: {trigger_stimulus}\n"
        f"Identity fracture: {identity_fracture}\n"
        f"Physical tell: {physical_tell}\n"
        f"Taboo topic: {taboo_topic}\n"
        f"{profile_extra_context}"
        f"Stage behavior guidance: {stage_behavior}\n"
        f"Turn reveal goal: {reveal_goal}\n"
        f"Recent transcript:\n{transcript_block}\n\n"
        f"Last player line: {last_player_line or '(none)'}\n"
        f"Last soldier line: {last_soldier_line or '(none)'}\n\n"
        f"Profile-specific testimony fragments:\n{profile_fragment_context or '(none)'}\n\n"
        f"{memory_context}\n\n"
        "Return valid JSON only with this schema:\n"
        "{"
        "\"soldier_reply\":\"short line\","
        "\"peak_insanity\":false,"
        "\"break_reason\":\"optional\","
        "\"choices\":["
        "{\"title\":\"short label\",\"text\":\"player line\",\"tone\":\"ground\"},"
        "{\"title\":\"short label\",\"text\":\"player line\",\"tone\":\"validate\"},"
        "{\"title\":\"short label\",\"text\":\"player line\",\"tone\":\"question\"},"
        "{\"title\":\"short label\",\"text\":\"player line\",\"tone\":\"confront\"}"
        "]"
        "}\n"
        "Rules: soldier_reply under 260 characters; plain language only; "
        "the reply must directly react to the player's last line and add one new concrete detail or escalation; "
        "the reply should fulfill the turn reveal goal instead of staying at the same emotional depth; "
        "do not repeat the same phrase or emotional beat from the recent transcript; "
        "make the soldier feel like one specific person, not a generic PTSD archetype; "
        "use sensory detail, guilt, warped identity, or trigger reaction when appropriate; "
        "choices must be concise, distinct, and feel like real conversation replies to what he just said; "
        "at least one choice should calm, one should validate, one should probe, and one should risk escalation; "
        "tones must be exactly one of ground, validate, question, confront; "
        "peak_insanity should only be true if the soldier is mentally breaking right now."
    )

    raw = run_ollama_with_options(prompt, temperature=temperature, force_json=True)
    #log(f"Raw debate output: {raw}")

    payload_json = None
    parse_status = "ok"
    try:
        payload_json = json.loads(raw)
    except Exception:
        parse_status = "json_parse_failed"
        json_match = re.search(r"\{.*\}", raw, re.DOTALL)
        if json_match:
            try:
                payload_json = json.loads(json_match.group(0))
                parse_status = "json_recovered_from_block"
            except Exception:
                payload_json = None
                parse_status = "json_block_parse_failed"

    if not isinstance(payload_json, dict):
        payload_json = {}
        if parse_status == "ok":
            parse_status = "payload_not_dict"

    soldier_reply = " ".join(str(payload_json.get("soldier_reply") or "").split()).strip()
    fallback_reason = ""
    if not soldier_reply:
        fallback_reason = "missing_soldier_reply"
        soldier_reply = clean_generated_reply_from_raw(raw)
        if soldier_reply:
            fallback_reason = "salvaged_raw_text"
    if not soldier_reply:
        fallback_reason = "stage_fallback"
        soldier_reply = pick_stage_fallback(stage, {
            "guarded": [
                "You keep talking like I can step back into a life that stayed untouched.",
                "You say it like the part of me that mattered is still standing at attention.",
            ],
            "frayed": [
                "You keep reaching for the version of me that existed before the noise got inside.",
                "You talk like I only misplaced myself, like I could still walk back into that old skin.",
            ],
            "unstable": [
                "Stop saying it like there is some whole version of me waiting under all this.",
                "Don't talk like the man before the war is just buried under the mud. He isn't.",
            ],
            "delusional": [
                "DON'T say it like there's a clean one left. There isn't. There isn't.",
                "You keep calling for a version of me that never came back out of it.",
            ],
            "breaking": [
                "DON'T talk like there's anything clean left of me to pull out of this.",
                "There is no untouched version left. There is only what kept moving.",
            ],
        })
    soldier_reply = shape_emotional_speech(soldier_reply, stage)[:260]

    if parse_status != "ok" or fallback_reason:
        log(
            f"[AI DEBUG][debate] parse_status={parse_status}, "
            f"fallback_reason={fallback_reason or 'none'}, "
            f"stage={stage}, raw_preview={raw[:240]!r}"
        )

    return {
        "soldier_reply": soldier_reply,
        "choices": normalize_debate_choices(payload_json.get("choices"), insanity),
        "peak_insanity": bool(payload_json.get("peak_insanity")) or insanity >= 95,
        "break_reason": " ".join(str(payload_json.get("break_reason") or "").split()).strip(),
        "temperature_used": temperature,
        "insanity_stage": stage,
    }


def get_typed_stage(instability: int) -> str:
    if instability >= 80:
        return "breaking"
    if instability >= 60:
        return "fractured"
    if instability >= 35:
        return "volatile"
    return "guarded"


def get_typed_temperature(instability: int) -> float:
    clamped = max(0, min(100, instability))
    return round(0.45 + (clamped / 100.0) * 0.8, 2)


def normalize_suggested_words(raw_words, fallback_words) -> list[str]:
    normalized = []
    seen = set()

    for item in raw_words or []:
        word = clean_choice(str(item or "").strip())
        if not word:
            continue
        lower = word.lower()
        if lower in seen:
            continue
        if len(word) > 20:
            word = word[:20].strip()
        normalized.append(word)
        seen.add(lower)
        if len(normalized) >= 6:
            return normalized

    for item in fallback_words or []:
        word = clean_choice(str(item or "").strip())
        if not word:
            continue
        lower = word.lower()
        if lower in seen:
            continue
        normalized.append(word)
        seen.add(lower)
        if len(normalized) >= 6:
            break

    return normalized[:6]


def generate_typed_turn(*, role: str, payload: dict) -> dict:
    global rag_engine

    if rag_engine is not None and getattr(rag_engine, "enabled", False) and getattr(rag_engine, "vectorizer", None) is None:
        log("[RAG] Lazy init on first request...")
        rag_engine.initialize()

    profile = resolve_soldier_profile(payload)

    speaker_name = coalesce_profile_field(payload, profile, "speakerName") or coalesce_profile_field(payload, profile, "soldierName", "Speaker")
    former_identity = coalesce_profile_field(payload, profile, "formerIdentity")
    military_role = coalesce_profile_field(payload, profile, "militaryRole")
    war_theater = coalesce_profile_field(payload, profile, "warTheater")
    defining_trauma = coalesce_profile_field(payload, profile, "definingTrauma")
    trigger_stimulus = coalesce_profile_field(payload, profile, "triggerStimulus")
    identity_fracture = coalesce_profile_field(payload, profile, "identityFracture")
    physical_tell = coalesce_profile_field(payload, profile, "physicalTell")
    taboo_topic = coalesce_profile_field(payload, profile, "tabooTopic")
    round_number = int(payload.get("round") or 1)
    instability = max(0, min(100, int(payload.get("instability") or 0)))
    stage = str(payload.get("stage") or get_typed_stage(instability)).strip() or get_typed_stage(instability)
    required_word = str(payload.get("requiredWord") or "").strip()
    player_typed_line = " ".join(str(payload.get("playerTypedLine") or "").split()).strip()
    offered_words = [str(item).strip() for item in (payload.get("offeredWords") or []) if str(item).strip()]
    detected_tags = [str(item).strip() for item in (payload.get("detectedTags") or []) if str(item).strip()]
    recent_transcript = [str(item).strip() for item in (payload.get("recentTranscript") or []) if str(item).strip()]
    scene_summary = str(payload.get("sceneSummary") or "").strip()

    transcript_block = "\n".join(recent_transcript) or "(none)"
    seed_text = " ".join(part for part in [player_typed_line, scene_summary, former_identity, defining_trauma] if part).strip()
    memory_context = rag_engine.build_prompt_context(seed_text) if rag_engine is not None else ""
    temperature = get_typed_temperature(instability)
    tag_text = ", ".join(detected_tags) if detected_tags else "none"
    offered_text = ", ".join(offered_words) if offered_words else "none"
    reveal_goal = get_reveal_goal(round_number, stage, "typed")
    profile_fragment_context = format_fragment_context(choose_profile_fragments(profile, stage, seed_text))
    profile_extra_context = ""
    recommended_words = []
    if profile:
        recurring_images = ", ".join(profile.get("recurringImages") or [])
        recommended_words = [str(word).strip() for word in (profile.get("recommendedWords") or []) if str(word).strip()]
        profile_extra_context = (
            f"Public persona: {str(profile.get('publicPersona') or '').strip()}\n"
            f"Hidden fear: {str(profile.get('hiddenFear') or '').strip()}\n"
            f"Moral wound: {str(profile.get('moralWound') or '').strip()}\n"
            f"Speech pattern: {str(profile.get('speechPattern') or '').strip()}\n"
            f"Recurring images: {recurring_images}\n"
            f"False belief: {str(profile.get('falseBelief') or '').strip()}\n"
            f"Refuses to admit early: {str(profile.get('refuseToAdmitEarly') or '').strip()}\n"
            f"Reveal midway: {str(profile.get('revealMidway') or '').strip()}\n"
            f"Reveal late: {str(profile.get('revealLate') or '').strip()}\n"
            f"Collapse mode: {str(profile.get('collapseMode') or '').strip()}\n"
            f"Recovery path: {str(profile.get('recoveryPath') or '').strip()}\n"
        )

    prompt = (
        f"{ROLE_SYSTEM_PROMPTS.get(role, ROLE_SYSTEM_PROMPTS[DEFAULT_ROLE])}\n\n"
        f"{ROLE_STYLE_GUIDES.get(role, '')}\n"
        "You are in a live typed conversation with the player.\n"
        f"Character name: {speaker_name}\n"
        f"Round: {round_number}\n"
        f"Instability: {instability}/100\n"
        f"Current stage: {stage}\n"
        f"Scene summary: {scene_summary}\n"
        f"Military role: {military_role}\n"
        f"Former identity before war: {former_identity}\n"
        f"War context: {war_theater}\n"
        f"Defining trauma: {defining_trauma}\n"
        f"Trigger stimulus: {trigger_stimulus}\n"
        f"Identity fracture: {identity_fracture}\n"
        f"Physical tell: {physical_tell}\n"
        f"Taboo topic: {taboo_topic}\n"
        f"{profile_extra_context}"
        f"Optional trigger word this turn: {required_word or '(none)'}\n"
        f"Pressure words shown to the player: {offered_text}\n"
        f"Detected input tags: {tag_text}\n"
        f"Turn reveal goal: {reveal_goal}\n"
        f"Recent transcript:\n{transcript_block}\n\n"
        f"Profile-specific testimony fragments:\n{profile_fragment_context or '(none)'}\n\n"
        f"{memory_context}\n\n"
        f"Player typed line: {player_typed_line or '(none)'}\n\n"
        "Return valid JSON only with this schema:\n"
        "{"
        "\"speaker_reply\":\"short reply\","
        "\"state_hint\":\"short mood summary\","
        "\"backstory_reveal\":\"very short new detail or identity shard\","
        "\"suggested_words\":[\"word\",\"word\",\"word\",\"word\"]"
        "}\n"
        "Rules: speaker_reply under 280 characters; plain text only; "
        "directly answer the player's typed line as the main point of the conversation; "
        "the first clause must clearly react to the newest player line, not an older topic from the transcript; "
        "if the player asks a question about your name or identity, answer that specific question first, even if the answer is a refusal; "
        "if the player insults, mocks, or provokes you, react to the insult first instead of drifting into unrelated imagery; "
        "do not mention 'name' unless the latest player line is about name or identity; "
        "fulfill the turn reveal goal and move the character to a deeper or clearer emotional beat; "
        "make the character feel specific, personal, and reactive; "
        "include one concrete memory, sensory detail, or identity fracture when possible; "
        "avoid repeating exact wording from the recent transcript; "
        "if the player used the optional trigger word, let it increase pressure, anger, paranoia, or memory bleed; "
        "if the player did not use the trigger word, do not force the reply to revolve around it; "
        "state_hint should be 2 to 6 words; "
        "backstory_reveal should be one short fragment of new personal information; "
        "suggested_words should be short, emotionally loaded words or fragments that could pressure, provoke, or rattle the soldier on the next turn."
    )

    raw = run_ollama_with_options(prompt, temperature=temperature, force_json=True)
    log(f"Raw typed turn output: {raw}")

    payload_json = None
    parse_status = "ok"
    try:
        payload_json = json.loads(raw)
    except Exception:
        parse_status = "json_parse_failed"
        json_match = re.search(r"\{.*\}", raw, re.DOTALL)
        if json_match:
            try:
                payload_json = json.loads(json_match.group(0))
                parse_status = "json_recovered_from_block"
            except Exception:
                payload_json = None
                parse_status = "json_block_parse_failed"

    if not isinstance(payload_json, dict):
        payload_json = {}
        if parse_status == "ok":
            parse_status = "payload_not_dict"

    speaker_reply = " ".join(str(payload_json.get("speaker_reply") or "").split()).strip()
    fallback_reason = ""
    if not speaker_reply:
        fallback_reason = "missing_speaker_reply"
        speaker_reply = clean_generated_reply_from_raw(raw)
        if speaker_reply:
            fallback_reason = "salvaged_raw_text"
    if not speaker_reply:
        fallback_reason = "stage_fallback"
        speaker_reply = pick_stage_fallback(stage, {
            "guarded": [
                "That word catches on something old. I don't know why it still does.",
                "You picked the wrong word to say calmly. It lands harder than you think.",
            ],
            "volatile": [
                "That word drags the room sideways. I can hear the old static in it.",
                "You say that and suddenly I'm back in the smell of dust and hot wiring.",
            ],
            "fractured": [
                "That word takes me backward. I can taste smoke, metal, and old fear all at once.",
                "You say it and the room tears open. I get the grit back in my mouth.",
            ],
            "breaking": [
                "DON'T use that word like it's harmless. It isn't. It puts me right back there.",
                "That word rips the room apart. I can smell the heat and hear the screaming again.",
            ],
        }, default_key="guarded")
    speaker_reply = shape_emotional_speech(speaker_reply, stage)[:280]

    if parse_status != "ok" or fallback_reason:
        log(
            f"[AI DEBUG][typed] parse_status={parse_status}, "
            f"fallback_reason={fallback_reason or 'none'}, "
            f"stage={stage}, raw_preview={raw[:240]!r}"
        )

    state_hint = " ".join(str(payload_json.get("state_hint") or "").split()).strip()[:64]
    backstory_reveal = " ".join(str(payload_json.get("backstory_reveal") or "").split()).strip()[:120]

    fallback_words = [required_word] + offered_words + recommended_words + ["name", "home", "blood", "order", "safe", "brother"]

    return {
        "speaker_reply": speaker_reply,
        "state_hint": state_hint,
        "backstory_reveal": backstory_reveal,
        "suggested_words": normalize_suggested_words(payload_json.get("suggested_words"), fallback_words),
        "temperature_used": temperature,
        "stage": stage,
    }


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
    #log(f"Loading model: {MODEL_NAME}")
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
    # log(f"Output directory: {out_dir}")

    load_mad_god_lore(script_dir)
    load_soldier_prompt_data(script_dir)

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

        #log(f"Received line: {line}")

        try:
            msg = json.loads(line)
        except Exception:
            log(f"Invalid JSON: {line}")
            continue

        if msg.get("cmd") == "quit":
            log("Shutting down.")
            break

        request_id = msg.get("id")
        request_type = msg.get("requestType", "speak")
        user_text = msg.get("text", "")
        if request_type == "debate_turn":
            user_text = str(msg.get("lastPlayerLine", "") or msg.get("sceneSummary", "") or "debate").strip()
        if request_type == "typed_turn":
            user_text = str(msg.get("playerTypedLine", "") or msg.get("requiredWord", "") or msg.get("sceneSummary", "") or "typed").strip()

        if not user_text:
            continue

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
                #log(f"Choices sent for request {request_id}")
                continue

            if request_type == "response":
                reply_text = generate_reply(role=role, user_text=user_text)
                #log(f"Generated response: {reply_text}")
                response = {
                    "type": "response_result",
                    "id": request_id,
                    "replyText": reply_text,
                    "elapsedMs": 0,
                }
                print(json.dumps(response), flush=True)
                #log(f"Response sent for request {request_id}")
                continue

            if request_type == "debate_turn":
                debate_payload = {
                    "soldierName": msg.get("soldierName", "Soldier"),
                    "sceneSummary": msg.get("sceneSummary", ""),
                    "round": msg.get("round", 1),
                    "insanity": msg.get("insanity", 0),
                    "insanityStage": msg.get("insanityStage", ""),
                    "lastPlayerLine": msg.get("lastPlayerLine", ""),
                    "lastSoldierLine": msg.get("lastSoldierLine", ""),
                    "recentTranscript": msg.get("recentTranscript", []),
                }
                debate_turn = generate_debate_turn(role=role, payload=debate_payload)
                reply_text = debate_turn["soldier_reply"]

                start_time = time.perf_counter()
                role_speaker_wavs = resolve_role_speaker_wavs(role)
                wav = tts.tts(
                    text=reply_text,
                    speaker_wav=role_speaker_wavs,
                    language=LANGUAGE,
                    **INFERENCE_KWARGS,
                )
                wav_np = np.asarray(wav, dtype=np.float32)

                out_path = os.path.abspath(os.path.join(out_dir, f"debate_{request_id}_{int(time.time() * 1000)}.wav"))
                sf.write(out_path, wav_np, sample_rate)
                elapsed_ms = int((time.perf_counter() - start_time) * 1000)

                response = {
                    "type": "debate_turn_result",
                    "id": request_id,
                    "replyText": reply_text,
                    "soldierReply": reply_text,
                    "wavPath": out_path,
                    "debateChoices": debate_turn["choices"],
                    "peakInsanity": debate_turn["peak_insanity"],
                    "breakReason": debate_turn["break_reason"],
                    "temperatureUsed": debate_turn["temperature_used"],
                    "insanityStage": debate_turn["insanity_stage"],
                    "elapsedMs": elapsed_ms,
                }
                print(json.dumps(response), flush=True)
                #log(f"Debate turn sent for request {request_id}")
                continue

            if request_type == "typed_turn":
                typed_payload = {
                    "speakerName": msg.get("speakerName", "Speaker"),
                    "sceneSummary": msg.get("sceneSummary", ""),
                    "formerIdentity": msg.get("formerIdentity", ""),
                    "militaryRole": msg.get("militaryRole", ""),
                    "warTheater": msg.get("warTheater", ""),
                    "definingTrauma": msg.get("definingTrauma", ""),
                    "triggerStimulus": msg.get("triggerStimulus", ""),
                    "identityFracture": msg.get("identityFracture", ""),
                    "physicalTell": msg.get("physicalTell", ""),
                    "tabooTopic": msg.get("tabooTopic", ""),
                    "round": msg.get("round", 1),
                    "instability": msg.get("instability", 0),
                    "stage": msg.get("stage", ""),
                    "requiredWord": msg.get("requiredWord", ""),
                    "playerTypedLine": msg.get("playerTypedLine", ""),
                    "offeredWords": msg.get("offeredWords", []),
                    "detectedTags": msg.get("detectedTags", []),
                    "recentTranscript": msg.get("recentTranscript", []),
                }
                typed_turn = generate_typed_turn(role=role, payload=typed_payload)
                reply_text = typed_turn["speaker_reply"]

                start_time = time.perf_counter()
                role_speaker_wavs = resolve_role_speaker_wavs(role)
                wav = tts.tts(
                    text=reply_text,
                    speaker_wav=role_speaker_wavs,
                    language=LANGUAGE,
                    **INFERENCE_KWARGS,
                )
                wav_np = np.asarray(wav, dtype=np.float32)

                out_path = os.path.abspath(os.path.join(out_dir, f"typed_{request_id}_{int(time.time() * 1000)}.wav"))
                sf.write(out_path, wav_np, sample_rate)
                elapsed_ms = int((time.perf_counter() - start_time) * 1000)

                response = {
                    "type": "typed_turn_result",
                    "id": request_id,
                    "replyText": reply_text,
                    "speakerReply": reply_text,
                    "wavPath": out_path,
                    "stateHint": typed_turn["state_hint"],
                    "backstoryReveal": typed_turn["backstory_reveal"],
                    "suggestedWords": typed_turn["suggested_words"],
                    "temperatureUsed": typed_turn["temperature_used"],
                    "stage": typed_turn["stage"],
                    "elapsedMs": elapsed_ms,
                }
                print(json.dumps(response), flush=True)
                # log(f"Typed turn sent for request {request_id}")
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
            #log(f"Wrote WAV file: {out_path} ({os.path.getsize(out_path)} bytes)")
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
