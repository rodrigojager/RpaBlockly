"""Classificação binária de grades hCaptcha com modelos ResNet ONNX fixados.

Os modelos vêm do model hub do hcaptcha-challenger (release ``model``), são
provisionados pelo usuário e validados por SHA-256/tamanho/contrato de tensores
antes da primeira inferência. Nenhum download acontece em runtime. O pipeline
de pré-processamento segue o contrato público dos modelos: imagem RGB,
resize 64x64, escala 1/255 e saída logit [1,2] cujo índice 0 é a classe
positiva.
"""

from __future__ import annotations

import hashlib
import io
import json
import math
import re
import threading
import unicodedata
from pathlib import Path
from typing import Any

from .config import Settings
from .errors import SolverError
from .schemas import InferenceResult, TileDecision

# Homóglifos minúsculos usados para ofuscar prompts (cirílico/grego → latim).
# A normalização aplica lowercase antes da substituição.
_HOMOGLYPHS = {
    "а": "a",
    "е": "e",
    "і": "i",
    "ο": "o",
    "с": "c",
    "ԁ": "d",
    "ѕ": "s",
    "һ": "h",
    "у": "y",
    "р": "p",
    "ϳ": "j",
    "х": "x",
    "κ": "k",
    "ι": "i",
    "β": "b",
    "ρ": "p",
}

_CJK_RE = re.compile(r"[぀-ヿ㐀-䶿一-鿿豈-﫿]")


def _normalize_text(value: str) -> str:
    """Normaliza prompt/alias para comparação determinística."""
    result = unicodedata.normalize("NFKC", value).lower()
    for bad, good in _HOMOGLYPHS.items():
        result = result.replace(bad, good)
    result = result.strip().rstrip(".")
    return " ".join(result.split())


def _alias_matches(prompt: str, alias: str) -> bool:
    if not alias:
        return False
    if _CJK_RE.search(alias):
        return alias in prompt
    return re.search(
        rf"(?<![\w]){re.escape(alias)}(?![\w])", prompt, flags=re.IGNORECASE
    ) is not None


class HCaptchaBinarySolver:
    """Resolve o rótulo do prompt e classifica os tiles individualmente."""

    def __init__(self, settings: Settings) -> None:
        self.settings = settings
        self._lock = threading.Lock()
        self._descriptors: list[dict[str, Any]] | None = None
        self._defaults_cache: dict[str, Any] | None = None
        self._suite_cache: str | None = None
        self._sessions: dict[str, Any] = {}

    def capability(self) -> dict[str, Any]:
        return {
            "type": "hcaptcha_image_label",
            "solver": "hcaptcha-resnet-onnx",
            "modelVersion": self._suite_version(),
            "labels": [item["id"] for item in self._load_manifest()],
            "maxTiles": self.settings.max_hcaptcha_tiles,
            "platform": "cpu",
        }

    def check_readiness(self) -> None:
        descriptors = self._load_manifest()
        if not descriptors:
            raise SolverError(
                "model_missing", "O manifesto hCaptcha não possui modelos.", 503
            )
        missing = [
            item["id"]
            for item in descriptors
            if not self._model_path(item).is_file()
        ]
        if missing:
            raise SolverError(
                "model_missing",
                f"Modelos hCaptcha não provisionados: {', '.join(missing)}.",
                503,
            )

    def solve(self, tiles: list[bytes], prompt: str) -> InferenceResult:
        if not tiles or len(tiles) > self.settings.max_hcaptcha_tiles:
            raise SolverError(
                "invalid_payload",
                "A quantidade de tiles do desafio é inválida.",
                422,
            )
        descriptor = self._resolve_label(prompt)
        session = self._load_session(descriptor)
        input_name = session.get_inputs()[0].name
        output_name = session.get_outputs()[0].name
        positive_index = int(self._defaults()["preprocessing"]["positiveIndex"])
        decisions: list[TileDecision] = []
        for index, raw in enumerate(tiles):
            tensor = self._preprocess_tile(raw)
            try:
                logits = session.run([output_name], {input_name: tensor})[0]
            except Exception as exception:
                raise SolverError(
                    "decode_failed",
                    "Os tiles não puderam ser inferidos "
                    f"({_safe_exception_name(exception)}).",
                    422,
                ) from exception
            row = logits[0].tolist()
            if len(row) != 2 or any(not math.isfinite(value) for value in row):
                raise SolverError(
                    "model_mismatch",
                    "A saída do classificador hCaptcha não respeita [1,2].",
                    503,
                )
            winner = 0 if row[0] >= row[1] else 1
            decisions.append(
                TileDecision(
                    index=index,
                    match=winner == positive_index,
                    confidence=round(1.0 / (1.0 + math.exp(-row[winner])), 6),
                )
            )
        return InferenceResult(
            solver="hcaptcha-resnet-onnx",
            model_version=(
                f"{descriptor['id']}@{descriptor['modelSha256'][:12].lower()}"
            ),
            tiles=decisions,
        )

    def _resolve_label(self, prompt: str) -> dict[str, Any]:
        normalized = _normalize_text(prompt)
        if not normalized:
            raise SolverError(
                "invalid_payload", "O prompt do desafio está vazio.", 422
            )
        best: tuple[int, dict[str, Any]] | None = None
        for descriptor in self._load_manifest():
            for language_aliases in descriptor["aliases"].values():
                for alias in language_aliases:
                    cleaned = _normalize_text(alias)
                    if _alias_matches(normalized, cleaned) and (
                        best is None or len(cleaned) > best[0]
                    ):
                        best = (len(cleaned), descriptor)
        if best is None:
            raise SolverError(
                "model_missing",
                "Nenhum modelo hCaptcha provisionado cobre o rótulo do desafio.",
                503,
                details={"suggestion": "human_handoff"},
            )
        return best[1]

    def _load_manifest(self) -> list[dict[str, Any]]:
        if self._descriptors is not None:
            return self._descriptors
        with self._lock:
            if self._descriptors is not None:
                return self._descriptors
            try:
                manifest = json.loads(
                    self.settings.hcaptcha_manifest_path.read_text("utf-8")
                )
                models = manifest["models"]
                if (
                    manifest["schemaVersion"] != 1
                    or manifest["suite"] != "hcaptcha-resnet-binary"
                    or not isinstance(models, list)
                ):
                    raise ValueError("manifest shape")
            except Exception as exception:
                raise SolverError(
                    "model_missing",
                    "O manifesto de modelos hCaptcha não está disponível ou é inválido.",
                    503,
                ) from exception
            self._defaults_cache = manifest["defaults"]
            self._suite_cache = str(manifest["suite"])
            self._descriptors = models
            return self._descriptors

    def _suite_version(self) -> str | None:
        try:
            self._load_manifest()
        except SolverError:
            return None
        return self._suite_cache

    def _defaults(self) -> dict[str, Any]:
        if self._defaults_cache is None:
            self._load_manifest()
        return self._defaults_cache

    def _model_path(self, descriptor: dict[str, Any]) -> Path:
        return self.settings.hcaptcha_model_dir / descriptor["publishedFile"]

    def _load_session(self, descriptor: dict[str, Any]) -> Any:
        cached = self._sessions.get(descriptor["id"])
        if cached is not None:
            return cached
        with self._lock:
            cached = self._sessions.get(descriptor["id"])
            if cached is not None:
                return cached
            path = self._model_path(descriptor)
            if not path.is_file():
                raise SolverError(
                    "model_missing",
                    f"Modelo hCaptcha ausente: {path.name}.",
                    503,
                )
            if path.stat().st_size != int(descriptor["modelSizeBytes"]):
                raise SolverError(
                    "model_mismatch", f"Tamanho inválido: {path.name}.", 503
                )
            digest = hashlib.sha256(path.read_bytes()).hexdigest()
            if digest.lower() != str(descriptor["modelSha256"]).lower():
                raise SolverError(
                    "model_mismatch", f"SHA-256 inválido: {path.name}.", 503
                )

            import onnxruntime as ort

            options = ort.SessionOptions()
            options.inter_op_num_threads = 1
            options.intra_op_num_threads = max(1, min(2, _cpu_count()))
            session = ort.InferenceSession(
                str(path),
                sess_options=options,
                providers=["CPUExecutionProvider"],
            )
            tensors = self._defaults()["tensors"]
            inputs = session.get_inputs()
            outputs = session.get_outputs()
            if (
                len(inputs) != 1
                or inputs[0].type != "tensor(float)"
                or inputs[0].shape != tensors["inputShape"]
                or len(outputs) != 1
                or outputs[0].type != "tensor(float)"
                or outputs[0].shape != tensors["outputShape"]
            ):
                raise SolverError(
                    "model_mismatch",
                    f"Os tensores de {path.name} divergem do manifesto.",
                    503,
                )
            self._sessions[descriptor["id"]] = session
            return session

    def _preprocess_tile(self, raw: bytes) -> Any:
        import numpy as np
        from PIL import Image, ImageFile

        preprocessing = self._defaults()["preprocessing"]
        target_width, target_height = (
            int(preprocessing["resize"][0]),
            int(preprocessing["resize"][1]),
        )
        Image.MAX_IMAGE_PIXELS = self.settings.max_image_pixels
        ImageFile.LOAD_TRUNCATED_IMAGES = False
        try:
            with Image.open(io.BytesIO(raw)) as source:
                width, height = source.size
                if (
                    width <= 0
                    or height <= 0
                    or width * height > self.settings.max_image_pixels
                ):
                    raise SolverError(
                        "invalid_payload",
                        "A resolução de um tile excede o limite configurado.",
                        413,
                    )
                source.load()
                if "A" in source.getbands():
                    rgba = source.convert("RGBA")
                    white = Image.new("RGBA", rgba.size, "white")
                    white.alpha_composite(rgba)
                    image = white.convert("RGB")
                else:
                    image = source.convert("RGB")
        except SolverError:
            raise
        except Exception as exception:
            raise SolverError(
                "decode_failed",
                "Um tile do desafio não é uma imagem válida.",
                422,
            ) from exception
        image = image.resize(
            (target_width, target_height), Image.Resampling.LANCZOS
        )
        samples = np.asarray(image, dtype=np.float32) / 255.0
        tensor = samples.transpose(2, 0, 1)[np.newaxis, :, :, :]
        return np.ascontiguousarray(tensor, dtype=np.float32)


def _safe_exception_name(exception: Exception) -> str:
    if isinstance(exception, SolverError):
        return exception.code
    return type(exception).__name__


def _cpu_count() -> int:
    import os

    return os.cpu_count() or 1
