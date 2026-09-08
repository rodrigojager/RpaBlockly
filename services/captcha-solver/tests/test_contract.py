from __future__ import annotations

import json
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timedelta, timezone
from pathlib import Path
from threading import Event
from typing import Any

from fastapi.testclient import TestClient

from captcha_solver.config import Settings
from captcha_solver.errors import SolverError
from captcha_solver.main import RequestBodyLimitMiddleware, create_app
from captcha_solver.schemas import InferenceResult
from captcha_solver.schemas import InferenceAction


class FakeRegistry:
    def readiness(self) -> dict[str, Any]:
        return {"ready": True, "checks": {"image": {"ready": True}}}

    def capabilities(self) -> list[dict[str, Any]]:
        return [{"type": "image", "solver": "fake", "modelVersion": "test"}]

    def solve_image(self, raw: bytes) -> InferenceResult:
        assert raw == b"image"
        return InferenceResult(
            answer="a3x9z", solver="fake-ocr", model_version="test-model"
        )

    def solve_audio(self, raw: bytes, language: str) -> InferenceResult:
        assert raw == b"audio"
        assert language == "pt"
        return InferenceResult(
            answer="sete quatro", solver="fake-whisper", model_version="test-audio"
        )

    def solve_visual(
        self,
        raw: bytes,
        *,
        task: str,
        provider: str,
        variant: str | None,
        hint: str | None,
        geometry: dict[str, Any],
        deadline: Any,
        local_only: bool,
    ) -> InferenceResult:
        assert raw == b"image"
        assert task == "grid"
        assert provider == "hcaptcha"
        assert local_only is False
        return InferenceResult(
            solver="fake-vlm",
            model_version="vision-test",
            confidence=0.8,
            actions=[InferenceAction(kind="Click", x=10, y=20)],
        )


class FailingImageRegistry(FakeRegistry):
    def solve_image(self, raw: bytes) -> InferenceResult:
        raise SolverError("model_missing", "modelo local ausente", 503)

    def solve_visual(
        self,
        raw: bytes,
        *,
        task: str,
        provider: str,
        variant: str | None,
        hint: str | None,
        geometry: dict[str, Any],
        deadline: Any,
        local_only: bool,
    ) -> InferenceResult:
        assert task == "text"
        assert local_only is False
        return InferenceResult(
            answer="vlm42",
            solver="fake-vlm",
            model_version="vision-test",
            actions=[
                InferenceAction(
                    kind="TypeText", targetRole="response", text="vlm42"
                )
            ],
        )


class TransientImageRegistry(FakeRegistry):
    def __init__(self, succeed_on: int) -> None:
        self.succeed_on = succeed_on
        self.calls = 0

    def solve_image(self, raw: bytes) -> InferenceResult:
        self.calls += 1
        if self.calls < self.succeed_on:
            raise SolverError(
                "busy", "inferência temporariamente ocupada", 503, retryable=True
            )
        return super().solve_image(raw)


class BlockingImageRegistry(FakeRegistry):
    def __init__(self) -> None:
        self.started = Event()
        self.release = Event()

    def solve_image(self, raw: bytes) -> InferenceResult:
        self.started.set()
        if not self.release.wait(timeout=10):
            raise AssertionError("A inferência bloqueante não foi liberada pelo teste.")
        return super().solve_image(raw)


def settings(
    tmp_path: Path,
    *,
    api_key: str = "",
    enable_vlm: bool = False,
) -> Settings:
    return Settings(
        api_key=api_key,
        allow_unauthenticated=not api_key,
        enable_image=True,
        enable_audio=True,
        ocr_model_path=tmp_path / "common.onnx",
        ocr_charset_path=tmp_path / "charset.json",
        ocr_manifest_path=tmp_path / "manifest.json",
        whisper_model="base",
        whisper_revision="revision",
        max_request_bytes=1024,
        max_image_bytes=256,
        max_audio_bytes=256,
        max_image_pixels=1000,
        max_audio_seconds=30,
        enable_vlm=enable_vlm,
        litellm_url="http://litellm.test:4000" if enable_vlm else "",
        litellm_api_key="router-test" if enable_vlm else "",
        litellm_model="captcha-vision" if enable_vlm else "",
    )


def test_health_ready_and_capabilities(tmp_path: Path) -> None:
    app = create_app(settings(tmp_path), FakeRegistry())
    with TestClient(app) as client:
        assert client.get("/health").json()["ok"] is True
        ready = client.get("/ready")
        assert ready.status_code == 200
        assert ready.json()["ready"] is True
        capabilities = client.get("/capabilities").json()
        assert capabilities["contractVersions"] == [1, 2]
        assert capabilities["limits"]["audioBytes"] == 256


def test_legacy_contract_is_preserved(tmp_path: Path) -> None:
    app = create_app(settings(tmp_path), FakeRegistry())
    with TestClient(app) as client:
        response = client.post(
            "/solve",
            json={"type": "image", "imageBase64": "aW1hZ2U="},
        )
    assert response.status_code == 200
    assert response.json() == {"success": True, "text": "a3x9z", "type": "image"}


def test_blocking_inference_does_not_block_health(tmp_path: Path) -> None:
    registry = BlockingImageRegistry()
    app = create_app(settings(tmp_path), registry)

    with TestClient(app) as client, ThreadPoolExecutor(max_workers=2) as executor:
        solve_request = executor.submit(
            client.post,
            "/solve",
            json={"type": "image", "imageBase64": "aW1hZ2U="},
        )
        assert registry.started.wait(timeout=2)
        health_request = executor.submit(client.get, "/health")
        try:
            health_response = health_request.result(timeout=2)
        finally:
            registry.release.set()
        solve_response = solve_request.result(timeout=2)

    assert health_response.status_code == 200
    assert health_response.json()["ok"] is True
    assert solve_response.status_code == 200


def test_v2_returns_answer_produced_with_correlated_ids(tmp_path: Path) -> None:
    app = create_app(settings(tmp_path), FakeRegistry())
    payload = {
        "contractVersion": 2,
        "requestId": "request-1",
        "challengeId": "challenge-1",
        "snapshotId": "snapshot-1",
        "type": "recaptcha_v2_audio",
        "assets": {"audioBase64": "YXVkaW8="},
        "geometry": {},
        "deadlineUtc": (datetime.now(timezone.utc) + timedelta(minutes=1)).isoformat(),
        "options": {"language": "pt"},
    }
    with TestClient(app) as client:
        response = client.post("/solve", json=payload)
    body = response.json()
    assert response.status_code == 200
    assert body["status"] == "AnswerProduced"
    assert body["requestId"] == "request-1"
    assert body["challengeId"] == "challenge-1"
    assert body["snapshotId"] == "snapshot-1"
    assert body["actions"] == [
        {"kind": "TypeText", "targetRole": "response", "text": "sete quatro"}
    ]
    assert body["error"] is None


def test_v2_max_attempts_limits_challenge_retries(tmp_path: Path) -> None:
    registry = TransientImageRegistry(succeed_on=3)
    app = create_app(settings(tmp_path), registry)
    payload = {
        "contractVersion": 2,
        "requestId": "request-retry",
        "challengeId": "challenge-retry",
        "snapshotId": "snapshot-retry",
        "type": "image",
        "assets": {"imageBase64": "aW1hZ2U="},
        "options": {"maxAttempts": 3},
    }
    with TestClient(app) as client:
        response = client.post("/solve", json=payload)
    assert response.status_code == 200
    assert response.json()["attempts"] == 3
    assert registry.calls == 3

    exhausted = TransientImageRegistry(succeed_on=3)
    exhausted_app = create_app(settings(tmp_path), exhausted)
    with TestClient(exhausted_app) as client:
        response = client.post(
            "/solve",
            json={
                **payload,
                "requestId": "request-exhausted",
                "options": {"maxAttempts": 2},
            },
        )
    assert response.status_code == 503
    assert response.json()["attempts"] == 2
    assert exhausted.calls == 2


def test_deadline_before_retry_does_not_count_inference(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    from captcha_solver import main as main_module

    checks = 0

    def expire_before_second_inference(_: Any) -> None:
        nonlocal checks
        checks += 1
        if checks == 3:
            raise SolverError(
                "deadline_exceeded",
                "O prazo total da requisição expirou.",
                504,
                retryable=True,
            )

    monkeypatch.setattr(main_module, "_check_deadline", expire_before_second_inference)
    registry = TransientImageRegistry(succeed_on=3)
    app = create_app(settings(tmp_path), registry)
    with TestClient(app) as client:
        response = client.post(
            "/solve",
            json={
                "contractVersion": 2,
                "requestId": "request-deadline-retry",
                "challengeId": "challenge-deadline-retry",
                "snapshotId": "snapshot-deadline-retry",
                "type": "image",
                "assets": {"imageBase64": "aW1hZ2U="},
                "options": {"maxAttempts": 3},
            },
        )
    assert response.status_code == 504
    assert response.json()["attempts"] == 1
    assert registry.calls == 1


def test_visual_v2_is_explicit_and_returns_typed_actions(tmp_path: Path) -> None:
    app = create_app(settings(tmp_path, enable_vlm=True), FakeRegistry())
    payload = {
        "contractVersion": 2,
        "requestId": "request-visual",
        "challengeId": "challenge-visual",
        "snapshotId": "snapshot-visual",
        "type": "visual",
        "provider": "hcaptcha",
        "task": "grid",
        "assets": {"imageBase64": "aW1hZ2U="},
        "geometry": {},
        "options": {"allowVlmFallback": True, "localOnly": False},
    }
    with TestClient(app) as client:
        response = client.post("/solve", json=payload)
        denied = client.post(
            "/solve",
            json={
                **payload,
                "requestId": "request-denied",
                "options": {"allowVlmFallback": False, "localOnly": False},
            },
        )
    assert response.status_code == 200
    assert response.json()["answer"] is None
    assert response.json()["actions"] == [
        {
            "kind": "Click",
            "targetRole": "challenge",
            "text": None,
            "x": 10.0,
            "y": 20.0,
            "toX": None,
            "toY": None,
        }
    ]
    assert denied.status_code == 422
    assert denied.json()["error"]["code"] == "needs_configuration"


def test_image_v2_can_fallback_to_vlm_explicitly(tmp_path: Path) -> None:
    app = create_app(settings(tmp_path, enable_vlm=True), FailingImageRegistry())
    payload = {
        "contractVersion": 2,
        "requestId": "request-fallback",
        "challengeId": "challenge-fallback",
        "snapshotId": "snapshot-fallback",
        "type": "image",
        "assets": {"imageBase64": "aW1hZ2U="},
        "options": {
            "allowVlmFallback": True,
            "localOnly": False,
            "maxAttempts": 2,
        },
    }
    with TestClient(app) as client:
        response = client.post("/solve", json=payload)
    assert response.status_code == 200
    assert response.json()["answer"] == "vlm42"
    assert response.json()["solver"] == "fake-vlm"
    assert response.json()["attempts"] == 2


def test_invalid_payloads_and_expired_deadline_are_stable(tmp_path: Path) -> None:
    app = create_app(settings(tmp_path), FakeRegistry())
    with TestClient(app) as client:
        scalar = client.post(
            "/solve", content="[]", headers={"content-type": "application/json"}
        )
        malformed = client.post(
            "/solve", content="{", headers={"content-type": "application/json"}
        )
        unsupported = client.post("/solve", json={"type": "other"})
        expired = client.post(
            "/solve",
            json={
                "contractVersion": 2,
                "requestId": "request-expired",
                "challengeId": "challenge-expired",
                "snapshotId": "snapshot-expired",
                "type": "image",
                "assets": {"imageBase64": "aW1hZ2U="},
                "deadlineUtc": (
                    datetime.now(timezone.utc) - timedelta(seconds=1)
                ).isoformat(),
            },
        )
        dual_asset = client.post(
            "/solve",
            json={
                "contractVersion": 2,
                "requestId": "request-dual",
                "challengeId": "challenge-dual",
                "snapshotId": "snapshot-dual",
                "type": "image",
                "assets": {
                    "imageBase64": "aW1hZ2U=",
                    "audioBase64": "YXVkaW8=",
                },
            },
        )
    assert scalar.status_code == 400
    assert scalar.json()["error"] == "invalid_payload"
    assert malformed.status_code == 400
    assert malformed.json()["error"] == "invalid_payload"
    assert unsupported.status_code == 422
    assert unsupported.json()["error"] == "unsupported_type"
    assert expired.status_code == 504
    assert expired.json()["error"]["code"] == "deadline_exceeded"
    assert dual_asset.status_code == 400
    assert dual_asset.json()["error"] == "invalid_payload"
    assert "aW1hZ2U=" not in dual_asset.text
    assert "YXVkaW8=" not in dual_asset.text


def test_bearer_authentication(tmp_path: Path) -> None:
    app = create_app(settings(tmp_path, api_key="secret-test"), FakeRegistry())
    with TestClient(app) as client:
        denied = client.post(
            "/solve", json={"type": "image", "imageBase64": "aW1hZ2U="}
        )
        allowed = client.post(
            "/solve",
            json={"type": "image", "imageBase64": "aW1hZ2U="},
            headers={"authorization": "Bearer secret-test"},
        )
    assert denied.status_code == 401
    assert denied.json()["error"] == "unauthorized"
    assert allowed.status_code == 200


def test_body_limit_counts_chunks_without_content_length() -> None:
    called = False

    async def downstream(scope: Any, receive: Any, send: Any) -> None:
        nonlocal called
        called = True

    middleware = RequestBodyLimitMiddleware(downstream, maximum_bytes=5)
    chunks = iter(
        [
            {"type": "http.request", "body": b"123", "more_body": True},
            {"type": "http.request", "body": b"456", "more_body": False},
        ]
    )
    sent: list[dict[str, Any]] = []

    async def receive() -> dict[str, Any]:
        return next(chunks)

    async def send(message: dict[str, Any]) -> None:
        sent.append(message)

    import asyncio

    asyncio.run(
        middleware(
            {"type": "http", "path": "/solve", "headers": []}, receive, send
        )
    )
    assert called is False
    start = next(message for message in sent if message["type"] == "http.response.start")
    body = next(message for message in sent if message["type"] == "http.response.body")
    assert start["status"] == 413
    assert json.loads(body["body"])["error"] == "invalid_payload"
