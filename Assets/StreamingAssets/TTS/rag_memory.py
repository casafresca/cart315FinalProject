from __future__ import annotations

import pickle
import random
import re
from dataclasses import dataclass
from pathlib import Path
from typing import Callable, List, Sequence

from sklearn.feature_extraction.text import TfidfVectorizer
from sklearn.metrics.pairwise import cosine_similarity

try:
    from pypdf import PdfReader
except Exception:  # pragma: no cover
    PdfReader = None


@dataclass
class MemoryChunk:
    text: str
    source: str


class PDFMemoryRAG:
    """
    Lightweight local RAG over PDF files.
    - Extracts text from PDFs
    - Splits into paragraph-size chunks
    - Uses TF-IDF + cosine similarity for retrieval
    - Adds optional memory glitches (irrelevant insertions / contradictions)
    """

    def __init__(
        self,
        research_dir: Path,
        cache_dir: Path,
        logger: Callable[[str], None],
        enabled: bool = True,
        top_k: int = 4,
        chunk_chars: int = 500,
        chunk_overlap: int = 80,
        min_chunk_chars: int = 120,
        glitch_probability: float = 0.20,
        contradiction_probability: float = 0.12,
        seed: int | None = None,
    ) -> None:
        self.enabled = enabled
        self.research_dir = research_dir
        self.cache_dir = cache_dir
        self.log = logger
        self.top_k = max(1, top_k)
        self.chunk_chars = max(150, chunk_chars)
        self.chunk_overlap = max(0, min(chunk_overlap, self.chunk_chars // 2))
        self.min_chunk_chars = max(40, min_chunk_chars)
        self.glitch_probability = max(0.0, min(1.0, glitch_probability))
        self.contradiction_probability = max(0.0, min(1.0, contradiction_probability))
        self.rng = random.Random(seed)

        self.chunks: List[MemoryChunk] = []
        self.vectorizer: TfidfVectorizer | None = None
        self.matrix = None

    def initialize(self) -> None:
        if not self.enabled:
            self.log("[RAG] Disabled by config.")
            return

        if PdfReader is None:
            self.log("[RAG] pypdf is unavailable. RAG disabled.")
            self.enabled = False
            return

        try:
            self.cache_dir.mkdir(parents=True, exist_ok=True)
            loaded = self._try_load_cache()
            if loaded:
                #self.log(f"[RAG] Loaded cache with {len(self.chunks)} chunks.")
                return

            self._build_index()
            self._save_cache()
            self.log(f"[RAG] Built index with {len(self.chunks)} chunks.")
        except Exception as exc:
            self.log(f"[RAG] Failed to initialize: {exc}")
            self.enabled = False

    def build_prompt_context(self, user_text: str) -> str:
        if not self.enabled or not user_text.strip() or self.vectorizer is None or self.matrix is None:
            return ""

        selected = self._retrieve(user_text, self.top_k)
        if not selected:
            return ""

        lines = ["Relevant memories from war/PTSD research:"]
        for item in selected:
            cleaned = self._compact(item.text)
            lines.append(f'- "{cleaned}" (source: {item.source})')
        return "\n".join(lines)

    def _pdf_files(self) -> Sequence[Path]:
        if not self.research_dir.exists():
            return []
        return sorted(self.research_dir.glob("*.pdf"))

    def _extract_pdf_text(self, pdf_path: Path) -> str:
        reader = PdfReader(str(pdf_path))
        parts: List[str] = []
        for page in reader.pages:
            text = page.extract_text() or ""
            if text.strip():
                parts.append(text)
        return "\n".join(parts)

    def _to_chunks(self, text: str, source: str) -> List[MemoryChunk]:
        normalized = self._normalize(text)
        if not normalized:
            return []

        paragraphs = [p.strip() for p in re.split(r"\n{2,}", normalized) if p.strip()]
        chunks: List[MemoryChunk] = []

        for para in paragraphs:
            if len(para) <= self.chunk_chars:
                if len(para) >= self.min_chunk_chars:
                    chunks.append(MemoryChunk(text=para, source=source))
                continue

            start = 0
            step = self.chunk_chars - self.chunk_overlap
            while start < len(para):
                piece = para[start : start + self.chunk_chars].strip()
                if len(piece) >= self.min_chunk_chars:
                    chunks.append(MemoryChunk(text=piece, source=source))
                start += max(1, step)

        return chunks

    def _build_index(self) -> None:
        files = self._pdf_files()
        if not files:
            raise FileNotFoundError(f"No PDF files found in {self.research_dir}")

        all_chunks: List[MemoryChunk] = []
        for pdf in files:
            try:
                text = self._extract_pdf_text(pdf)
                chunks = self._to_chunks(text, pdf.name)
                all_chunks.extend(chunks)
                self.log(f"[RAG] {pdf.name}: {len(chunks)} chunks")
            except Exception as exc:
                self.log(f"[RAG] Skipped {pdf.name}: {exc}")

        if not all_chunks:
            raise RuntimeError("No usable text chunks were extracted from PDFs.")

        self.chunks = all_chunks
        corpus = [c.text for c in self.chunks]
        self.vectorizer = TfidfVectorizer(max_features=40000, ngram_range=(1, 2), stop_words="english")
        self.matrix = self.vectorizer.fit_transform(corpus)

    def _retrieve(self, query: str, top_k: int) -> List[MemoryChunk]:
        q = self._normalize(query)
        if not q:
            return []

        query_vec = self.vectorizer.transform([q])
        sims = cosine_similarity(query_vec, self.matrix).ravel()
        ranked = sims.argsort()[::-1]

        picks: List[MemoryChunk] = []
        seen_idx = set()
        seen_snippets = set()

        for idx in ranked:
            if len(picks) >= top_k:
                break
            score = float(sims[idx])
            if score <= 0:
                continue
            candidate = self.chunks[idx]
            key = candidate.text[:180]
            if key in seen_snippets:
                continue
            picks.append(candidate)
            seen_idx.add(idx)
            seen_snippets.add(key)

        if not picks:
            sample_size = min(top_k, len(self.chunks))
            return self.rng.sample(self.chunks, sample_size)

        if self.rng.random() < self.glitch_probability and len(self.chunks) > len(picks):
            idx = self.rng.randrange(0, len(self.chunks))
            if idx not in seen_idx:
                insert_at = self.rng.randrange(0, len(picks) + 1)
                candidate = self.chunks[idx]
                key = candidate.text[:180]
                if key not in seen_snippets:
                    picks.insert(insert_at, candidate)
                    seen_snippets.add(key)

        if self.rng.random() < self.contradiction_probability and picks:
            picks.append(self.rng.choice(picks))

        return picks[:top_k]

    def _cache_path(self) -> Path:
        return self.cache_dir / "rag_pdf_index.pkl"

    def _fingerprint_path(self) -> Path:
        return self.cache_dir / "rag_pdf_fingerprint.txt"

    def _make_fingerprint(self) -> str:
        files = self._pdf_files()
        rows = []
        for p in files:
            stat = p.stat()
            rows.append(f"{p.name}|{stat.st_size}|{int(stat.st_mtime)}")
        return "\n".join(rows)

    def _try_load_cache(self) -> bool:
        cache_path = self._cache_path()
        fp_path = self._fingerprint_path()
        if not cache_path.exists() or not fp_path.exists():
            return False

        current_fp = self._make_fingerprint()
        saved_fp = fp_path.read_text(encoding="utf-8", errors="ignore")
        if current_fp != saved_fp:
            return False

        with cache_path.open("rb") as f:
            payload = pickle.load(f)

        self.chunks = payload["chunks"]
        self.vectorizer = payload["vectorizer"]
        self.matrix = payload["matrix"]
        return True

    def _save_cache(self) -> None:
        cache_path = self._cache_path()
        fp_path = self._fingerprint_path()

        payload = {
            "chunks": self.chunks,
            "vectorizer": self.vectorizer,
            "matrix": self.matrix,
        }

        with cache_path.open("wb") as f:
            pickle.dump(payload, f)

        fp_path.write_text(self._make_fingerprint(), encoding="utf-8")

    @staticmethod
    def _normalize(text: str) -> str:
        text = text.replace("\x00", " ")
        text = re.sub(r"[ \t]+", " ", text)
        text = re.sub(r"\r\n?", "\n", text)
        text = re.sub(r"\n{3,}", "\n\n", text)
        return text.strip()

    @staticmethod
    def _compact(text: str, max_len: int = 210) -> str:
        text = re.sub(r"\s+", " ", text).strip()
        text = text.encode("ascii", "ignore").decode("ascii")
        if len(text) <= max_len:
            return text
        return text[: max_len - 3].rstrip() + "..."



