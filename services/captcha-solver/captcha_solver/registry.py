"""Registry de modelos CPU, limites de mídia e inferência serializada."""

from __future__ import annotations

import hashlib
import io
import json
import math
import tempfile
import threading
from pathlib import Path
from typing import Any

from .config import Settings
from .errors import SolverError
from .hcaptcha import HCaptchaBinarySolver
from .schemas import InferenceResult, VisualTask
from .vlm import LiteLlmVisionSolver


class SolverRegistry:
    def __init__(
        self,
        settings: Settings,
        vlm_solver: LiteLlmVisionSolver | None = None,
        hcaptcha_solver: HCaptchaBinarySolver | None = None,
    ) -> None:
        self.settings = settings
        self._image_gate = threading.BoundedSemaphore(1)
        self._audio_gate = threading.BoundedSemaphore(1)
        self._vlm_gate = threading.BoundedSemaphore(1)
        self._hcaptcha_gate = threading.BoundedSemaphore(1)
        self._model_lock = threading.Lock()
        self._ocr_session: Any | None = None
        self._ocr_descriptor: dict[str, Any] | None = None
        self._charset: list[str] | None = None
        self._whisper: Any | None = None
        self._vlm = vlm_solver or (
            LiteLlmVisionSolver(settings) if settings.enable_vlm else None
        )
        self._hcaptcha = hcaptcha_solver or (
            HCaptchaBinarySolver(settings) if settings.enable_hcaptcha else None
        )

    def capabilities(self) -> list[dict[str, Any]]:
        result: list[dict[str, Any]] = []
        if self.settings.enable_image:
            result.append(
                {
                    "type": "image",
                    "solver": "onnx-ctc",
                    "modelVersion": self._manifest_version(),
                    "maxBytes": self.settings.max_image_bytes,
                    "maxPixels": self.settings.max_image_pixels,
                    "platform": "cpu",
                }
            )
        if self.settings.enable_audio:
            result.append(
                {
                    "type": "recaptcha_v2_audio",
                    "solver": "faster-whisper",
                    "modelVersion": (
                        f"{self.settings.whisper_model}@"
                        f"{self.settings.whisper_revision}"
                    ),
                    "maxBytes": self.settings.max_audio_bytes,
                    "maxDurationSeconds": self.settings.max_audio_seconds,
                    "platform": "cpu-int8",
                }
            )
        if self.settings.enable_vlm:
            result.append(
                {
                    "type": "visual",
                    "solver": "litellm-vlm",
                    "modelVersion": self.settings.litellm_model,
                    "tasks": [
                        "text",
                        "grid",
                        "point",
                        "bounding_box",
                        "slider",
                        "rotation",
                        "drag",
                    ],
                    "platform": (
                        "local-router"
                        if self.settings.litellm_is_local
                        else "external-router"
                    ),
                    "maxActions": self.settings.vlm_max_actions,
                }
            )
        if self.settings.enable_hcaptcha and self._hcaptcha is not None:
            try:
                result.append(self._hcaptcha.capability())
            except SolverError:
                result.append(
                    {
                        "type": "hcaptcha_image_label",
                        "solver": "hcaptcha-resnet-onnx",
                        "modelVersion": None,
                        "labels": [],
                        "maxTiles": self.settings.max_hcaptcha_tiles,
                        "platform": "cpu",
                    }
                )
        return result

    def readiness(self) -> dict[str, Any]:
        checks: dict[str, Any] = {}
        if self.settings.enable_image:
            try:
                self._load_ocr()
                checks["image"] = {"ready": True}
            except Exception as exception:  # readiness precisa explicar a causa
                checks["image"] = {
                    "ready": False,
                    "error": _safe_exception_name(exception),
                }
        if self.settings.enable_audio:
            try:
                self._load_whisper()
                checks["recaptcha_v2_audio"] = {"ready": True}
            except Exception as exception:
                checks["recaptcha_v2_audio"] = {
                    "ready": False,
                    "error": _safe_exception_name(exception),
                }
        if self.settings.enable_vlm:
            try:
                assert self._vlm is not None
                self._vlm.check_readiness()
                checks["visual"] = {"ready": True}
            except Exception as exception:
                checks["visual"] = {
                    "ready": False,
                    "error": _safe_exception_name(exception),
                }
        if self.settings.enable_hcaptcha:
            try:
                assert self._hcaptcha is not None
                self._hcaptcha.check_readiness()
                checks["hcaptcha_image_label"] = {"ready": True}
            except Exception as exception:
                checks["hcaptcha_image_label"] = {
                    "ready": False,
                    "error": _safe_exception_name(exception),
                }
        return {
            "ready": bool(checks) and all(item["ready"] for item in checks.values()),
            "checks": checks,
        }

    def solve_image(self, raw: bytes) -> InferenceResult:
        if not self.settings.enable_image:
            raise SolverError("unsupported_type", "OCR de imagem desabilitado.", 422)
        if not self._image_gate.acquire(blocking=False):
            raise SolverError("busy", "OCR ocupado.", 429, retryable=True)
        try:
            return self._solve_image(raw)
        finally:
            self._image_gate.release()

    def solve_audio(self, raw: bytes, language: str) -> InferenceResult:
        if not self.settings.enable_audio:
            raise SolverError("unsupported_type", "Áudio desabilitado.", 422)
        if not self._audio_gate.acquire(blocking=False):
            raise SolverError("busy", "Transcrição ocupada.", 429, retryable=True)
        try:
            return self._solve_audio(raw, language)
        finally:
            self._audio_gate.release()

    def solve_hcaptcha(self, tiles: list[bytes], hint: str) -> InferenceResult:
        if not self.settings.enable_hcaptcha or self._hcaptcha is None:
            raise SolverError(
                "unsupported_type", "Classificação hCaptcha desabilitada.", 422
            )
        if not self._hcaptcha_gate.acquire(blocking=False):
            raise SolverError(
                "busy", "Inferência hCaptcha ocupada.", 429, retryable=True
            )
        try:
            return self._hcaptcha.solve(tiles, hint)
        finally:
            self._hcaptcha_gate.release()

    def solve_visual(
        self,
        raw: bytes,
        *,
        task: VisualTask,
        provider: str,
        variant: str | None,
        hint: str | None,
        geometry: dict[str, Any],
        deadline: Any,
        local_only: bool,
    ) -> InferenceResult:
        if not self.settings.enable_vlm or self._vlm is None:
            raise SolverError("unsupported_type", "Fallback VLM desabilitado.", 422)
        if local_only and not self.settings.litellm_is_local:
            raise SolverError(
                "needs_configuration",
                "A política localOnly bloqueou o router VLM externo.",
                422,
            )
        if not self._vlm_gate.acquire(blocking=False):
            raise SolverError("busy", "Inferência VLM ocupada.", 429, retryable=True)
        try:
            return self._vlm.solve(
                raw,
                task,
                provider,
                variant,
                hint,
                geometry,
                deadline,
            )
        finally:
            self._vlm_gate.release()

    def _solve_image(self, raw: bytes) -> InferenceResult:
        try:
            import numpy as np
            from PIL import Image, ImageFile

            Image.MAX_IMAGE_PIXELS = self.settings.max_image_pixels
            ImageFile.LOAD_TRUNCATED_IMAGES = False
            with Image.open(io.BytesIO(raw)) as source:
                width, height = source.size
                if width <= 0 or height <= 0 or width * height > self.settings.max_image_pixels:
                    raise SolverError(
                        "invalid_payload",
                        "A resolução da imagem excede o limite configurado.",
                        413,
                    )
                source.load()
                if "A" in source.getbands():
                    rgba = source.convert("RGBA")
                    white = Image.new("RGBA", rgba.size, "white")
                    white.alpha_composite(rgba)
                    image = white.convert("L")
                else:
                    image = source.convert("L")

            descriptor = self._descriptor()
            target_height = int(descriptor["preprocessing"]["targetHeight"])
            target_width = max(1, int(width * target_height / height))
            if target_width > 4096:
                raise SolverError(
                    "invalid_payload", "A largura normalizada excede o limite.", 413
                )
            image = image.resize((target_width, target_height), Image.Resampling.LANCZOS)
            samples = np.asarray(image, dtype=np.float32) / 255.0
            samples = (samples - 0.5) / 0.5
            tensor = samples[np.newaxis, np.newaxis, :, :]

            session, charset = self._load_ocr()
            output = session.run(
                [descriptor["tensors"]["outputName"]],
                {descriptor["tensors"]["inputName"]: tensor},
            )[0]
            if output.dtype != np.int64 or output.ndim != 2 or output.shape[0] != 1:
                raise SolverError(
                    "model_mismatch", "A saída ONNX não respeita int64 [1,sequence].", 503
                )
            answer = _decode_ctc(output[0].tolist(), charset)
        except SolverError:
            raise
        except Exception as exception:
            raise SolverError(
                "decode_failed",
                f"A imagem não pôde ser inferida ({_safe_exception_name(exception)}).",
                422,
            ) from exception

        if not answer.strip():
            raise SolverError("low_confidence", "O OCR não produziu texto.", 422)
        return InferenceResult(
            answer=answer.strip(),
            solver="onnx-ctc",
            model_version=descriptor["version"],
        )

    def _solve_audio(self, raw: bytes, language: str) -> InferenceResult:
        path = ""
        try:
            with tempfile.NamedTemporaryFile(suffix=".mp3", delete=False) as handle:
                handle.write(raw)
                path = handle.name
            duration = _audio_duration_seconds(Path(path))
            if duration is None:
                raise SolverError(
                    "decode_failed", "Não foi possível determinar a duração do áudio.", 422
                )
            if duration > self.settings.max_audio_seconds:
                raise SolverError(
                    "invalid_payload",
                    "A duração do áudio excede o limite configurado.",
                    413,
                )

            model = self._load_whisper()
            segments, _ = model.transcribe(path, language=language, beam_size=5)
            answer = " ".join(segment.text for segment in segments).strip()
        except SolverError:
            raise
        except Exception as exception:
            raise SolverError(
                "upstream_unavailable",
                f"A transcrição falhou ({_safe_exception_name(exception)}).",
                503,
                retryable=True,
            ) from exception
        finally:
            if path:
                Path(path).unlink(missing_ok=True)

        if not answer:
            raise SolverError("low_confidence", "A transcrição não produziu texto.", 422)
        return InferenceResult(
            answer=answer,
            solver="faster-whisper",
            model_version=(
                f"{self.settings.whisper_model}@{self.settings.whisper_revision}"
            ),
        )

    def _load_ocr(self) -> tuple[Any, list[str]]:
        if self._ocr_session is not None and self._charset is not None:
            return self._ocr_session, self._charset
        with self._model_lock:
            if self._ocr_session is not None and self._charset is not None:
                return self._ocr_session, self._charset
            descriptor = self._descriptor()
            _validate_file(
                self.settings.ocr_model_path,
                descriptor["modelSizeBytes"],
                descriptor["modelSha256"],
            )
            _validate_file(
                self.settings.ocr_charset_path,
                None,
                descriptor["charset"]["sha256"],
            )
            charset = json.loads(self.settings.ocr_charset_path.read_text("utf-8"))
            if not isinstance(charset, list) or len(charset) != descriptor["charset"]["classes"]:
                raise SolverError(
                    "model_mismatch", "O charset não corresponde ao manifesto.", 503
                )
            import onnxruntime as ort

            options = ort.SessionOptions()
            options.inter_op_num_threads = 1
            options.intra_op_num_threads = max(1, min(2, _cpu_count()))
            session = ort.InferenceSession(
                str(self.settings.ocr_model_path),
                sess_options=options,
                providers=["CPUExecutionProvider"],
            )
            inputs = session.get_inputs()
            outputs = session.get_outputs()
            tensors = descriptor["tensors"]
            if (
                len(inputs) != 1
                or inputs[0].name != tensors["inputName"]
                or inputs[0].type != "tensor(float)"
                or len(inputs[0].shape) != 4
                or inputs[0].shape[:3] != [1, 1, 64]
                or len(outputs) != 1
                or outputs[0].name != tensors["outputName"]
                or outputs[0].type != "tensor(int64)"
                or len(outputs[0].shape) != 2
                or outputs[0].shape[0] != 1
            ):
                raise SolverError(
                    "model_mismatch", "Os tensores ONNX divergem do manifesto.", 503
                )
            self._ocr_session = session
            self._charset = [str(item) for item in charset]
            return session, self._charset

    def _load_whisper(self) -> Any:
        if self._whisper is not None:
            return self._whisper
        with self._model_lock:
            if self._whisper is not None:
                return self._whisper
            from faster_whisper import WhisperModel

            self._whisper = WhisperModel(
                self.settings.whisper_model,
                device="cpu",
                compute_type="int8",
                revision=self.settings.whisper_revision,
                local_files_only=True,
            )
            return self._whisper

    def _descriptor(self) -> dict[str, Any]:
        if self._ocr_descriptor is not None:
            return self._ocr_descriptor
        try:
            manifest = json.loads(self.settings.ocr_manifest_path.read_text("utf-8"))
            models = manifest["models"]
            if manifest["schemaVersion"] != 1 or len(models) != 1:
                raise ValueError("manifest shape")
            self._ocr_descriptor = models[0]
            return self._ocr_descriptor
        except Exception as exception:
            raise SolverError(
                "model_missing", "O manifesto OCR não está disponível ou é inválido.", 503
            ) from exception

    def _manifest_version(self) -> str | None:
        try:
            return str(self._descriptor()["version"])
        except SolverError:
            return None


def _validate_file(path: Path, expected_size: int | None, expected_hash: str) -> None:
    if not path.is_file():
        raise SolverError("model_missing", f"Arquivo obrigatório ausente: {path.name}.", 503)
    if expected_size is not None and path.stat().st_size != int(expected_size):
        raise SolverError("model_mismatch", f"Tamanho inválido: {path.name}.", 503)
    digest = hashlib.sha256(path.read_bytes()).hexdigest()
    if digest.lower() != str(expected_hash).lower():
        raise SolverError("model_mismatch", f"SHA-256 inválido: {path.name}.", 503)


def _decode_ctc(indices: list[int], charset: list[str]) -> str:
    result: list[str] = []
    previous = 0
    for raw_index in indices:
        index = int(raw_index)
        if index < 0 or index >= len(charset):
            raise SolverError("model_mismatch", "Índice fora do charset OCR.", 503)
        if index != previous and index != 0:
            result.append(charset[index])
        previous = index
    return "".join(result)


def _audio_duration_seconds(path: Path) -> float | None:
    import av

    with av.open(str(path)) as container:
        if container.duration is not None:
            duration = float(container.duration / av.time_base)
        else:
            durations = [
                float(stream.duration * stream.time_base)
                for stream in container.streams.audio
                if stream.duration is not None and stream.time_base is not None
            ]
            duration = max(durations, default=math.nan)
    return duration if math.isfinite(duration) and duration >= 0 else None


def _safe_exception_name(exception: Exception) -> str:
    if isinstance(exception, SolverError):
        return exception.code
    return type(exception).__name__


def _cpu_count() -> int:
    import os

    return os.cpu_count() or 1
