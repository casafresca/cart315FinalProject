import soundfile as sf
import time
import argparse

import numpy as np
import torch
from TTS.api import TTS

from tts_cli_config import (
    INFERENCE_KWARGS,
    LANGUAGE,
    MODEL_NAME,
    NARRATOR_FILES,
    get_output_sample_rate,
    resolve_existing_files,
)


def log(message: str) -> None:
    timestamp = time.strftime("%H:%M:%S")
    print(f"[{timestamp}] {message}", flush=True)


def resolve_files(paths):
    return resolve_existing_files(paths)


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


def build_fast_synthesizer(tts: TTS, speaker_wavs, language: str):
    xtts_model = getattr(getattr(tts, "synthesizer", None), "tts_model", None)
    if xtts_model is None or not hasattr(xtts_model, "get_conditioning_latents") or not hasattr(xtts_model, "inference"):
        return None

    log("Caching voice conditioning latents...")
    cache_start = time.perf_counter()
    gpt_cond_latent, speaker_embedding = xtts_model.get_conditioning_latents(
        audio_path=speaker_wavs,
        gpt_cond_len=12,
        gpt_cond_chunk_len=4,
        max_ref_length=10,
        sound_norm_refs=False,
    )
    cache_s = time.perf_counter() - cache_start
    log(f"Cached conditioning in {cache_s:.1f}s")

    def synth(text: str) -> np.ndarray:
        outputs = xtts_model.inference(
            text=text,
            language=language,
            gpt_cond_latent=gpt_cond_latent,
            speaker_embedding=speaker_embedding,
            enable_text_splitting=False,
            **INFERENCE_KWARGS,
        )
        return np.asarray(outputs["wav"], dtype=np.float32)

    return synth


def main():
    parser = argparse.ArgumentParser(description="XTTS CLI player (interactive or one-shot).")
    parser.add_argument(
        "text",
        nargs="*",
        help="If provided, speaks this text once and exits. If omitted, runs interactive mode.",
    )
    parser.add_argument(
        "--no-voice-cache",
        action="store_true",
        help="Disable caching voice conditioning latents (slower per-utterance).",
    )
    parser.add_argument(
        "--warmup",
        action="store_true",
        help="Run one silent warmup generation after loading/caching (reduces first-utterance latency).",
    )
    args = parser.parse_args()
    one_shot_text = " ".join(args.text).strip() if args.text else None

    if torch.cuda.is_available():
        try:
            torch.backends.cuda.matmul.allow_tf32 = True
            torch.backends.cudnn.allow_tf32 = True
        except Exception:
            pass

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
    speaker_wavs = resolve_files(NARRATOR_FILES)
    log(f"Inference settings: {INFERENCE_KWARGS}")

    fast_synth = None
    if not args.no_voice_cache:
        fast_synth = build_fast_synthesizer(tts, speaker_wavs=speaker_wavs, language=LANGUAGE)

    if args.warmup:
        try:
            log("Warmup generation...")
            if fast_synth is not None:
                _ = fast_synth("warmup")
            else:
                _ = tts.tts(text="warmup", speaker_wav=speaker_wavs, language=LANGUAGE, **INFERENCE_KWARGS)
            log("Warmup done.")
        except Exception as exc:
            log(f"Warmup failed (continuing): {exc}")

    if one_shot_text:
        log(f"Generating once: {one_shot_text}")
        gen_start = time.perf_counter()
        if fast_synth is not None:
            wav_np = fast_synth(one_shot_text)
        else:
            wav = tts.tts(text=one_shot_text, speaker_wav=speaker_wavs, language=LANGUAGE, **INFERENCE_KWARGS)
            wav_np = np.asarray(wav, dtype=np.float32)
        gen_s = time.perf_counter() - gen_start
        log(f"Generated in {gen_s:.1f}s; audio {wav_np.size/sample_rate:.2f}s; saving...")

        output_file = f"./outputs/npc_voice_{time.strftime('%Y%m%d_%H%M%S')}.wav"
        sf.write(output_file, wav_np, sample_rate)

        log(f"Saved to {output_file}")
        log("Done.")
        return

    log("Type text and press Enter. Type 'quit' to exit.")
    while True:
        try:
            text = input("> ").strip()
        except (EOFError, KeyboardInterrupt):
            log("Exiting.")
            break
        if not text:
            continue
        if text.lower() in {"quit", "exit"}:
            log("Exiting.")
            break

        log("Generating...")
        gen_start = time.perf_counter()
        if fast_synth is not None:
            wav_np = fast_synth(text)
        else:
            wav = tts.tts(text=text, speaker_wav=speaker_wavs, language=LANGUAGE, **INFERENCE_KWARGS)
            wav_np = np.asarray(wav, dtype=np.float32)
        gen_s = time.perf_counter() - gen_start
        log(f"Generated in {gen_s:.1f}s; audio {wav_np.size/sample_rate:.2f}s; saving...")

        output_file = f"npc_voice_{time.strftime('%Y%m%d_%H%M%S')}.wav"
        sf.write(output_file, wav_np, sample_rate)
        log(f"Saved to {output_file}")


if __name__ == "__main__":
    main()
