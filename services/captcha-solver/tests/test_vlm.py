from __future__ import annotations

import io
import json
from dataclasses import replace
from pathlib import Path

import httpx
import pytest
from PIL import Image

from captcha_solver.config import Settings
from captcha_solver.errors import SolverError
from captcha_solver.registry import SolverRegistry
from captcha_solver.schemas import InferenceAction
from captcha_solver.vlm import LiteLlmVisionSolver


def _settings(tmp_path: Path) -> Settings:
    return Settings(
        api_key="service-key",
        allow_unauthenticated=False,
        enable_image=False,
        enable_audio=False,
        ocr_model_path=tmp_path / "common.onnx",
        ocr_charset_path=tmp_path / "charset.json",
        ocr_manifest_path=tmp_path / "manifest.json",
        whisper_model="base",
        whisper_revision="revision",
        max_request_bytes=1024 * 1024,
        max_image_bytes=512 * 1024,
        max_audio_bytes=512 * 1024,
        max_image_pixels=10_000,
        max_audio_seconds=30,
        enable_vlm=True,
        litellm_url="http://litellm.test:4000",
        litellm_api_key="router-key",
        litellm_model="captcha-vision",
        litellm_is_local=True,
        vlm_timeout_seconds=10,
        vlm_max_actions=4,
    )


def _png() -> bytes:
    stream = io.BytesIO()
    Image.new("RGB", (100, 60), "white").save(stream, format="PNG")
    return stream.getvalue()


def test_litellm_returns_bounded_typed_actions(tmp_path: Path) -> None:
    observed: dict[str, object] = {}

    def handler(request: httpx.Request) -> httpx.Response:
        observed["authorization"] = request.headers.get("authorization")
        observed["payload"] = json.loads(request.content)
        return httpx.Response(
            200,
            json={
                "choices": [
                    {
                        "message": {
                            "content": json.dumps(
                                {
                                    "answer": None,
                                    "confidence": 0.75,
                                    "actions": [
                                        {
                                            "kind": "Click",
                                            "targetRole": "challenge",
                                            "x": 25,
                                            "y": 30,
                                        }
                                    ],
                                }
                            )
                        }
                    }
                ]
            },
        )

    client = httpx.Client(transport=httpx.MockTransport(handler))
    solver = LiteLlmVisionSolver(_settings(tmp_path), client)
    result = solver.solve(
        _png(), "grid", "hcaptcha", None, "selecione ônibus", {}, None
    )
    assert result.actions[0].kind == "Click"
    assert result.confidence is None
    assert observed["authorization"] == "Bearer router-key"
    payload = observed["payload"]
    assert isinstance(payload, dict) and payload["model"] == "captcha-vision"


def test_litellm_rejects_coordinates_outside_snapshot(tmp_path: Path) -> None:
    def handler(_: httpx.Request) -> httpx.Response:
        return httpx.Response(
            200,
            json={
                "choices": [
                    {
                        "message": {
                            "content": (
                                '{"answer":null,"confidence":0.9,"actions":['
                                '{"kind":"Click","x":100,"y":20}]}'
                            )
                        }
                    }
                ]
            },
        )

    solver = LiteLlmVisionSolver(
        _settings(tmp_path),
        httpx.Client(transport=httpx.MockTransport(handler)),
    )
    with pytest.raises(SolverError) as failure:
        solver.solve(_png(), "point", "generic", None, None, {}, None)
    assert failure.value.code == "contract_violation"


def test_litellm_classifies_invalid_decision_as_contract_violation(
    tmp_path: Path,
) -> None:
    def handler(_: httpx.Request) -> httpx.Response:
        return httpx.Response(
            200,
            json={
                "choices": [
                    {
                        "message": {
                            "content": (
                                '{"answer":null,"confidence":0.9,"actions":['
                                '{"kind":"Delete","x":10,"y":20}]}'
                            )
                        }
                    }
                ]
            },
        )

    solver = LiteLlmVisionSolver(
        _settings(tmp_path),
        httpx.Client(transport=httpx.MockTransport(handler)),
    )
    with pytest.raises(SolverError) as failure:
        solver.solve(_png(), "grid", "hcaptcha", None, None, {}, None)
    assert failure.value.code == "contract_violation"
    assert failure.value.retryable is False


def test_local_only_blocks_external_litellm_router(tmp_path: Path) -> None:
    settings = replace(_settings(tmp_path), litellm_is_local=False)
    registry = SolverRegistry(settings, vlm_solver=object())  # type: ignore[arg-type]
    with pytest.raises(SolverError) as failure:
        registry.solve_visual(
            _png(),
            task="grid",
            provider="hcaptcha",
            variant=None,
            hint=None,
            geometry={},
            deadline=None,
            local_only=True,
        )
    assert failure.value.code == "needs_configuration"


@pytest.mark.parametrize(
    ("task", "action"),
    [
        (
            "grid",
            InferenceAction(
                kind="Drag", targetRole="challenge", x=1, y=1, toX=2, toY=2
            ),
        ),
        ("drag", InferenceAction(kind="Click", targetRole="challenge", x=1, y=1)),
        ("text", InferenceAction(kind="TypeText", targetRole="challenge", text="abc")),
    ],
)
def test_vlm_rejects_action_incompatible_with_task(
    task: str, action: InferenceAction
) -> None:
    with pytest.raises(SolverError) as failure:
        LiteLlmVisionSolver._validate_actions(  # type: ignore[arg-type]
            [action], task, 100, 60
        )
    assert failure.value.code == "contract_violation"


def test_vlm_rejects_oversized_router_response(tmp_path: Path) -> None:
    def handler(_: httpx.Request) -> httpx.Response:
        return httpx.Response(200, content=b"x" * (256 * 1024 + 1))

    solver = LiteLlmVisionSolver(
        _settings(tmp_path),
        httpx.Client(transport=httpx.MockTransport(handler)),
    )
    with pytest.raises(SolverError) as failure:
        solver.solve(_png(), "grid", "hcaptcha", None, None, {}, None)
    assert failure.value.code == "contract_violation"
