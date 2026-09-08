from __future__ import annotations

import base64
import hashlib
import io
import json
import os
from dataclasses import replace
from pathlib import Path
from typing import Any

import pytest
from fastapi.testclient import TestClient

from captcha_solver.config import Settings
from captcha_solver.errors import SolverError
from captcha_solver.hcaptcha import HCaptchaBinarySolver, _normalize_text
from captcha_solver.main import create_app
from captcha_solver.schemas import InferenceResult, TileDecision
from provision_models import _validate_hcaptcha_download_url


REPOSITORY_ROOT = Path(__file__).resolve().parents[3]
REAL_MANIFEST = (
    REPOSITORY_ROOT
    / "src"
    / "RpaFlow.Playwright"
    / "V2"
    / "Captcha"
    / "hcaptcha-models.manifest.json"
)

TILE_PNG = base64.b64encode(b"tile-0").decode("ascii")


def _tile(index: int) -> str:
    return base64.b64encode(f"tile-{index}".encode("utf-8")).decode("ascii")


def _payload(**overrides: Any) -> dict[str, Any]:
    body: dict[str, Any] = {
        "contractVersion": 2,
        "requestId": "request-hcaptcha",
        "challengeId": "challenge-hcaptcha",
        "snapshotId": "snapshot-hcaptcha",
        "type": "hcaptcha_image_label",
        "hint": "Please click each image containing an airplane",
        "assets": {"tilesBase64": [_tile(0), _tile(1), _tile(2)]},
        "options": {"maxAttempts": 1},
    }
    body.update(overrides)
    return body


def _settings(
    tmp_path: Path,
    *,
    enable_hcaptcha: bool = True,
    enable_image: bool = False,
) -> Settings:
    return Settings(
        api_key="",
        allow_unauthenticated=True,
        enable_image=enable_image,
        enable_audio=False,
        ocr_model_path=tmp_path / "common.onnx",
        ocr_charset_path=tmp_path / "charset.json",
        ocr_manifest_path=tmp_path / "manifest.json",
        whisper_model="base",
        whisper_revision="revision",
        max_request_bytes=64 * 1024,
        max_image_bytes=16 * 1024,
        max_audio_bytes=256,
        max_image_pixels=1_000_000,
        max_audio_seconds=30,
        enable_hcaptcha=enable_hcaptcha,
        hcaptcha_model_dir=tmp_path / "hcaptcha",
        hcaptcha_manifest_path=tmp_path / "hcaptcha-models.manifest.json",
        max_hcaptcha_tiles=4,
    )


class FakeHcaptchaRegistry:
    def readiness(self) -> dict[str, Any]:
        return {
            "ready": True,
            "checks": {"hcaptcha_image_label": {"ready": True}},
        }

    def capabilities(self) -> list[dict[str, Any]]:
        return [
            {
                "type": "hcaptcha_image_label",
                "solver": "fake-hcaptcha",
                "modelVersion": "test",
            }
        ]

    def solve_hcaptcha(self, tiles: list[bytes], hint: str) -> InferenceResult:
        assert tiles and hint
        return InferenceResult(
            solver="fake-hcaptcha",
            model_version="test-model",
            tiles=[
                TileDecision(index=index, match=index % 2 == 0, confidence=0.9)
                for index, _ in enumerate(tiles)
            ],
        )


class BusyHcaptchaRegistry(FakeHcaptchaRegistry):
    def solve_hcaptcha(self, tiles: list[bytes], hint: str) -> InferenceResult:
        raise SolverError("busy", "inferência ocupada", 429, retryable=True)


class MissingModelRegistry(FakeHcaptchaRegistry):
    def solve_hcaptcha(self, tiles: list[bytes], hint: str) -> InferenceResult:
        raise SolverError(
            "model_missing",
            "Nenhum modelo hCaptcha provisionado cobre o rótulo do desafio.",
            503,
            details={"suggestion": "human_handoff"},
        )


def test_default_hcaptcha_limits_fit_the_request_budget(tmp_path: Path) -> None:
    settings = replace(
        _settings(tmp_path),
        max_request_bytes=15 * 1024 * 1024,
        max_image_bytes=5 * 1024 * 1024,
        max_hcaptcha_tiles=16,
    )
    settings.validate()


def test_hcaptcha_redirect_accepts_only_github_asset_hosts() -> None:
    original = "https://github.com/QIN2DIM/example/releases/download/model/a.onnx"
    _validate_hcaptcha_download_url(original, original)
    _validate_hcaptcha_download_url(
        original,
        "https://objects.githubusercontent.com/path/a.onnx?signature=test",
    )
    _validate_hcaptcha_download_url(
        original,
        "https://release-assets.githubusercontent.com/path/a.onnx?signature=test",
    )
    with pytest.raises(RuntimeError, match="host não permitido"):
        _validate_hcaptcha_download_url(
            original,
            "https://objects.githubusercontent.com.evil.example/path/a.onnx",
        )


def test_v2_returns_tile_decisions_with_correlated_ids(tmp_path: Path) -> None:
    app = create_app(_settings(tmp_path), FakeHcaptchaRegistry())
    with TestClient(app) as client:
        response = client.post("/solve", json=_payload())
    body = response.json()
    assert response.status_code == 200
    assert body["status"] == "AnswerProduced"
    assert body["requestId"] == "request-hcaptcha"
    assert body["challengeId"] == "challenge-hcaptcha"
    assert body["snapshotId"] == "snapshot-hcaptcha"
    assert body["solver"] == "fake-hcaptcha"
    assert body["modelVersion"] == "test-model"
    assert body["attempts"] == 1
    assert body["error"] is None
    assert body["tiles"] == [
        {"index": 0, "match": True, "confidence": 0.9},
        {"index": 1, "match": False, "confidence": 0.9},
        {"index": 2, "match": True, "confidence": 0.9},
    ]


def test_v2_requires_tiles(tmp_path: Path) -> None:
    app = create_app(_settings(tmp_path), FakeHcaptchaRegistry())
    with TestClient(app) as client:
        response = client.post("/solve", json=_payload(assets={}))
    assert response.status_code == 400
    # Envelope inválido responde no formato legado (error como string).
    assert response.json()["error"] == "invalid_payload"


def test_v2_requires_prompt_hint(tmp_path: Path) -> None:
    app = create_app(_settings(tmp_path), FakeHcaptchaRegistry())
    with TestClient(app) as client:
        response = client.post("/solve", json=_payload(hint=None))
        assert response.status_code == 400
        assert response.json()["error"] == "invalid_payload"
        response = client.post("/solve", json=_payload(hint="   "))
        assert response.status_code == 400
        assert response.json()["error"] == "invalid_payload"


def test_v2_rejects_mixed_assets(tmp_path: Path) -> None:
    app = create_app(_settings(tmp_path), FakeHcaptchaRegistry())
    with TestClient(app) as client:
        response = client.post(
            "/solve",
            json=_payload(
                assets={"tilesBase64": [_tile(0)], "imageBase64": _tile(1)}
            ),
        )
    assert response.status_code == 400
    assert response.json()["error"] == "invalid_payload"


def test_disabled_capability_returns_unsupported_type(tmp_path: Path) -> None:
    app = create_app(
        _settings(tmp_path, enable_hcaptcha=False, enable_image=True),
        FakeHcaptchaRegistry(),
    )
    with TestClient(app) as client:
        response = client.post("/solve", json=_payload())
    body = response.json()
    assert response.status_code == 422
    assert body["error"]["code"] == "unsupported_type"


def test_busy_returns_429_with_retry_after(tmp_path: Path) -> None:
    app = create_app(_settings(tmp_path), BusyHcaptchaRegistry())
    with TestClient(app) as client:
        response = client.post("/solve", json=_payload())
    body = response.json()
    assert response.status_code == 429
    assert response.headers.get("Retry-After") == "1"
    assert body["error"]["code"] == "busy"
    assert body["error"]["retryable"] is True
    assert body["tiles"] is None


def test_missing_model_maps_to_503_with_handoff_suggestion(tmp_path: Path) -> None:
    app = create_app(_settings(tmp_path), MissingModelRegistry())
    with TestClient(app) as client:
        response = client.post("/solve", json=_payload())
    body = response.json()
    assert response.status_code == 503
    assert body["error"]["code"] == "model_missing"
    assert body["error"]["suggestion"] == "human_handoff"


def _solver_for_real_manifest() -> HCaptchaBinarySolver:
    manifest = json.loads(REAL_MANIFEST.read_text("utf-8"))
    settings = Settings(
        api_key="",
        allow_unauthenticated=True,
        enable_image=False,
        enable_audio=False,
        ocr_model_path=Path("unused.onnx"),
        ocr_charset_path=Path("unused.json"),
        ocr_manifest_path=Path("unused-manifest.json"),
        whisper_model="base",
        whisper_revision="revision",
        max_request_bytes=64 * 1024,
        max_image_bytes=16 * 1024,
        max_audio_bytes=256,
        max_image_pixels=1_000_000,
        max_audio_seconds=30,
        enable_hcaptcha=True,
        hcaptcha_model_dir=Path("unused-models"),
        hcaptcha_manifest_path=REAL_MANIFEST,
        max_hcaptcha_tiles=len(manifest["models"]),
    )
    return HCaptchaBinarySolver(settings)


def test_prompt_alias_resolution_languages() -> None:
    solver = _solver_for_real_manifest()
    cases = {
        "Please click each image containing an airplane": "airplane2310",
        "Please click each image containing a seaplane": "seaplane",
        "Clique em cada imagem que contém um avião": "airplane2310",
        "Selecione todas as imagens com uma ponte": "bridge",
        "请点击每张包含飞机的图片": "airplane2310",
        "Please click each image containing an аirрlane": "airplane2310",
        "Please click each image containing a motor vehicle": "motor_vehicle2309",
    }
    for prompt, expected in cases.items():
        assert solver._resolve_label(prompt)["id"] == expected, prompt


def test_prompt_alias_resolution_longest_alias_wins() -> None:
    solver = _solver_for_real_manifest()
    descriptor = solver._resolve_label(
        "Please click each image containing a motor vehicle"
    )
    assert descriptor["id"] == "motor_vehicle2309"
    assert solver._resolve_label("seaplane")["id"] == "seaplane"


def test_prompt_without_model_maps_to_model_missing() -> None:
    solver = _solver_for_real_manifest()
    with pytest.raises(SolverError) as failure:
        solver._resolve_label("Please click each image containing a zepelim")
    assert failure.value.code == "model_missing"
    assert failure.value.details["suggestion"] == "human_handoff"


def test_normalize_text_strips_homoglyphs_and_punctuation() -> None:
    assert _normalize_text("  Аirрlane. ") == "airplane"
    assert _normalize_text("Ponte\n") == "ponte"


@pytest.mark.skipif(
    os.environ.get("RPABLOCKLY_REQUIRE_CAPTCHA_MODELS") != "1",
    reason=(
        "smoke real desabilitado; CI obrigatório usa "
        "RPABLOCKLY_REQUIRE_CAPTCHA_MODELS=1"
    ),
)
def test_provisioned_models_match_manifest() -> None:
    settings = Settings.from_environment()
    manifest = json.loads(settings.hcaptcha_manifest_path.read_text("utf-8"))
    assert len(manifest["models"]) >= 1
    for model in manifest["models"]:
        path = settings.hcaptcha_model_dir / model["publishedFile"]
        assert path.is_file(), f"model_missing: {path}"
        assert path.stat().st_size == model["modelSizeBytes"]
        digest = hashlib.sha256(path.read_bytes()).hexdigest()
        assert digest.upper() == model["modelSha256"].upper()


@pytest.mark.skipif(
    os.environ.get("RPABLOCKLY_REQUIRE_CAPTCHA_MODELS") != "1",
    reason=(
        "smoke real desabilitado; CI obrigatório usa "
        "RPABLOCKLY_REQUIRE_CAPTCHA_MODELS=1"
    ),
)
def test_pinned_hcaptcha_model_classifies_synthetic_tiles() -> None:
    from PIL import Image

    settings = Settings.from_environment()
    solver = HCaptchaBinarySolver(settings)

    def tile(color: tuple[int, int, int]) -> bytes:
        buffer = io.BytesIO()
        Image.new("RGB", (128, 128), color).save(buffer, format="PNG")
        return buffer.getvalue()

    tiles = [tile((200, 30, 30)), tile((30, 30, 200))]
    result = solver.solve(
        tiles, "Please click each image containing an airplane"
    )
    assert result.solver == "hcaptcha-resnet-onnx"
    assert result.model_version is not None
    assert result.tiles is not None and len(result.tiles) == len(tiles)
    for index, decision in enumerate(result.tiles):
        assert decision.index == index
        assert decision.confidence is None or 0 <= decision.confidence <= 1
    repeated = solver.solve(
        tiles, "Please click each image containing an airplane"
    )
    assert [item.match for item in result.tiles] == [
        item.match for item in repeated.tiles or []
    ]
