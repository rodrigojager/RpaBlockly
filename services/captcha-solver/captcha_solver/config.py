"""Configuração validada do serviço, sem downloads implícitos."""

from __future__ import annotations

import os
from dataclasses import dataclass
from pathlib import Path
from urllib.parse import urlsplit


def _repository_root() -> Path:
    parents = Path(__file__).resolve().parents
    return parents[3] if len(parents) > 3 else Path("/app")


def _first_existing(*paths: Path) -> Path:
    return next((path for path in paths if path.exists()), paths[0])


def _positive_int(name: str, default: int) -> int:
    raw = os.environ.get(name, str(default))
    try:
        value = int(raw)
    except ValueError as exception:
        raise RuntimeError(f"{name} deve ser inteiro.") from exception
    if value <= 0:
        raise RuntimeError(f"{name} deve ser positivo.")
    return value


def _flag(name: str, default: bool) -> bool:
    raw = os.environ.get(name)
    if raw is None:
        return default
    if raw not in {"0", "1"}:
        raise RuntimeError(f"{name} deve ser 0 ou 1.")
    return raw == "1"


@dataclass(frozen=True)
class Settings:
    api_key: str
    allow_unauthenticated: bool
    enable_image: bool
    enable_audio: bool
    ocr_model_path: Path
    ocr_charset_path: Path
    ocr_manifest_path: Path
    whisper_model: str
    whisper_revision: str
    max_request_bytes: int
    max_image_bytes: int
    max_audio_bytes: int
    max_image_pixels: int
    max_audio_seconds: int
    enable_vlm: bool = False
    litellm_url: str = ""
    litellm_api_key: str = ""
    litellm_model: str = ""
    litellm_is_local: bool = False
    vlm_timeout_seconds: int = 45
    vlm_max_actions: int = 25
    enable_hcaptcha: bool = True
    hcaptcha_model_dir: Path = Path("/app/models/hcaptcha")
    hcaptcha_manifest_path: Path = Path("/app/models/hcaptcha/hcaptcha-models.manifest.json")
    max_hcaptcha_tiles: int = 16

    @classmethod
    def from_environment(cls) -> "Settings":
        root = _repository_root()
        model_root = Path("/app/models")
        return cls(
            api_key=os.environ.get("CAPTCHA_API_KEY", ""),
            allow_unauthenticated=_flag("CAPTCHA_ALLOW_UNAUTHENTICATED", False),
            enable_image=_flag("CAPTCHA_ENABLE_IMAGE", True),
            enable_audio=_flag("CAPTCHA_ENABLE_AUDIO", True),
            ocr_model_path=Path(
                os.environ.get(
                    "CAPTCHA_OCR_MODEL_PATH",
                    str(
                        _first_existing(
                            model_root / "common.onnx",
                            root / "captcha-models" / "common.onnx",
                        )
                    ),
                )
            ),
            ocr_charset_path=Path(
                os.environ.get(
                    "CAPTCHA_OCR_CHARSET_PATH",
                    str(
                        _first_existing(
                            model_root / "ocr-charset.json",
                            root
                            / "src"
                            / "RpaFlow.Playwright"
                            / "V2"
                            / "Captcha"
                            / "ocr-charset.json",
                        )
                    ),
                )
            ),
            ocr_manifest_path=Path(
                os.environ.get(
                    "CAPTCHA_OCR_MANIFEST_PATH",
                    str(
                        _first_existing(
                            model_root / "captcha-models.manifest.json",
                            root
                            / "src"
                            / "RpaFlow.Playwright"
                            / "V2"
                            / "Captcha"
                            / "captcha-models.manifest.json",
                        )
                    ),
                )
            ),
            whisper_model=os.environ.get("CAPTCHA_WHISPER_MODEL", "base"),
            whisper_revision=os.environ.get(
                "CAPTCHA_WHISPER_REVISION",
                "ebe41f70d5b6dfa9166e2c581c45c9c0cfc57b66",
            ),
            max_request_bytes=_positive_int(
                "CAPTCHA_MAX_REQUEST_BYTES", 15 * 1024 * 1024
            ),
            max_image_bytes=_positive_int(
                "CAPTCHA_MAX_IMAGE_BYTES", 5 * 1024 * 1024
            ),
            max_audio_bytes=_positive_int(
                "CAPTCHA_MAX_AUDIO_BYTES", 5 * 1024 * 1024
            ),
            max_image_pixels=_positive_int("CAPTCHA_MAX_IMAGE_PIXELS", 16_000_000),
            max_audio_seconds=_positive_int("CAPTCHA_MAX_AUDIO_SECONDS", 30),
            enable_vlm=_flag("CAPTCHA_ENABLE_VLM", False),
            litellm_url=os.environ.get("CAPTCHA_LITELLM_URL", "").strip(),
            litellm_api_key=os.environ.get("CAPTCHA_LITELLM_API_KEY", "").strip(),
            litellm_model=os.environ.get("CAPTCHA_LITELLM_MODEL", "").strip(),
            litellm_is_local=_flag("CAPTCHA_LITELLM_IS_LOCAL", False),
            vlm_timeout_seconds=_positive_int("CAPTCHA_VLM_TIMEOUT_SECONDS", 45),
            vlm_max_actions=_positive_int("CAPTCHA_VLM_MAX_ACTIONS", 25),
            enable_hcaptcha=_flag("CAPTCHA_ENABLE_HCAPTCHA", True),
            hcaptcha_model_dir=Path(
                os.environ.get(
                    "CAPTCHA_HCAPTCHA_MODEL_DIR",
                    str(
                        _first_existing(
                            model_root / "hcaptcha",
                            root / "captcha-models" / "hcaptcha",
                        )
                    ),
                )
            ),
            hcaptcha_manifest_path=Path(
                os.environ.get(
                    "CAPTCHA_HCAPTCHA_MANIFEST_PATH",
                    str(
                        _first_existing(
                            model_root / "hcaptcha" / "hcaptcha-models.manifest.json",
                            root
                            / "src"
                            / "RpaFlow.Playwright"
                            / "V2"
                            / "Captcha"
                            / "hcaptcha-models.manifest.json",
                        )
                    ),
                )
            ),
            max_hcaptcha_tiles=_positive_int("CAPTCHA_MAX_HCAPTCHA_TILES", 16),
        )

    def validate(self) -> None:
        if not self.api_key and not self.allow_unauthenticated:
            raise RuntimeError(
                "Configure CAPTCHA_API_KEY ou habilite "
                "CAPTCHA_ALLOW_UNAUTHENTICATED=1 apenas em localhost."
            )
        if (
            not self.enable_image
            and not self.enable_audio
            and not self.enable_vlm
            and not self.enable_hcaptcha
        ):
            raise RuntimeError("Habilite pelo menos uma capacidade de inferência.")
        if self.max_image_bytes > self.max_request_bytes:
            raise RuntimeError(
                "CAPTCHA_MAX_IMAGE_BYTES não pode exceder CAPTCHA_MAX_REQUEST_BYTES."
            )
        if self.max_audio_bytes > self.max_request_bytes:
            raise RuntimeError(
                "CAPTCHA_MAX_AUDIO_BYTES não pode exceder CAPTCHA_MAX_REQUEST_BYTES."
            )
        if self.max_hcaptcha_tiles > 64:
            raise RuntimeError("CAPTCHA_MAX_HCAPTCHA_TILES não pode exceder 64.")
        if self.enable_vlm:
            if not self.litellm_url or not self.litellm_model:
                raise RuntimeError(
                    "CAPTCHA_LITELLM_URL e CAPTCHA_LITELLM_MODEL são obrigatórios "
                    "quando CAPTCHA_ENABLE_VLM=1."
                )
            if not self.litellm_api_key:
                raise RuntimeError(
                    "CAPTCHA_LITELLM_API_KEY é obrigatória quando o VLM está habilitado."
                )
            parsed = urlsplit(self.litellm_url)
            if (
                parsed.scheme not in {"http", "https"}
                or not parsed.hostname
                or parsed.username
                or parsed.password
                or parsed.query
                or parsed.fragment
            ):
                raise RuntimeError("CAPTCHA_LITELLM_URL deve ser uma URL HTTP(S) base.")
            if self.vlm_max_actions > 100:
                raise RuntimeError("CAPTCHA_VLM_MAX_ACTIONS não pode exceder 100.")
