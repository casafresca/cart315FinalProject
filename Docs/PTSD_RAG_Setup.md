# PTSD War RAG Setup (Unity + Python)

This project now supports a local PDF-based RAG layer for NPC replies in:

- `Assets/StreamingAssets/TTS/tts_cli_player_basicv3.py`

## What it does

1. Reads PDFs from `Assets/Research`
2. Chunks text into memory fragments
3. Builds a TF-IDF retrieval index (cached locally)
4. Retrieves top fragments for each player line
5. Injects them into the Ollama prompt before generating reply

## Files added/updated

- Added: `Assets/StreamingAssets/TTS/rag_memory.py`
- Updated: `Assets/StreamingAssets/TTS/tts_cli_player_basicv3.py`
- Updated: `Assets/StreamingAssets/TTS/tts_cli_config.py`
- Updated: `Assets/StreamingAssets/TTS/requirements.txt`

## Cache location

- `Assets/StreamingAssets/TTS/.cache/rag`

The cache auto-rebuilds if PDF files change.

## Environment knobs (optional)

You can set these env vars before launching Unity or Python:

- `RAG_ENABLED=true|false`
- `RAG_TOP_K=4`
- `RAG_CHUNK_CHARS=500`
- `RAG_CHUNK_OVERLAP=80`
- `RAG_MIN_CHUNK_CHARS=120`
- `RAG_GLITCH_PROBABILITY=0.20`
- `RAG_CONTRADICTION_PROBABILITY=0.12`
- `RAG_RANDOM_SEED=42`
- `RAG_RESEARCH_DIR=<custom path>`
- `RAG_CACHE_DIR=<custom path>`

## Trauma-like memory behavior

The retriever intentionally supports "memory noise":

- occasional irrelevant insertion (`RAG_GLITCH_PROBABILITY`)
- occasional contradictory repeat (`RAG_CONTRADICTION_PROBABILITY`)

This makes dialogue feel less deterministic and more fragmented.
