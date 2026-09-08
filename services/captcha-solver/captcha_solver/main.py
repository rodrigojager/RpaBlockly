"""API FastAPI compatível com /solve legado e contractVersion 2."""

from __future__ import annotations

import base64
import asyncio
import binascii
import json
import secrets
import time
from contextlib import asynccontextmanager
from datetime import datetime, timezone
from typing import Any, Callable

from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse
from pydantic import ValidationError
from starlette.concurrency import run_in_threadpool

from .config import Settings
from .errors import SolverError, invalid_payload
from .registry import SolverRegistry
from .schemas import SolveRequestV2


SERVICE_VERSION = "0.4.0"


class RequestBodyLimitMiddleware:
    """Limita bytes realmente recebidos, mesmo sem Content-Length."""

    def __init__(
        self, app: Callable[..., Any], maximum_bytes: int, api_key: str = ""
    ) -> None:
        self.app = app
        self.maximum_bytes = maximum_bytes
        self.api_key = api_key

    async def __call__(self, scope: dict[str, Any], receive: Any, send: Any) -> None:
        if scope.get("type") != "http" or scope.get("path") != "/solve":
            await self.app(scope, receive, send)
            return

        headers = {key.lower(): value for key, value in scope.get("headers", [])}
        if self.api_key:
            supplied = headers.get(b"authorization", b"")
            expected = f"Bearer {self.api_key}".encode("utf-8")
            if not secrets.compare_digest(supplied, expected):
                await _raw_error(401, "unauthorized", "Bearer inválido.")(
                    scope, receive, send
                )
                return
        declared = headers.get(b"content-length")
        if declared is not None:
            try:
                if int(declared) > self.maximum_bytes:
                    await _raw_error(413, "invalid_payload", "request_too_large")(
                        scope, receive, send
                    )
                    return
            except ValueError:
                await _raw_error(400, "invalid_payload", "bad_content_length")(
                    scope, receive, send
                )
                return

        body = bytearray()
        more = True
        while more:
            message = await receive()
            if message.get("type") == "http.disconnect":
                return
            if message.get("type") != "http.request":
                continue
            body.extend(message.get("body", b""))
            if len(body) > self.maximum_bytes:
                await _raw_error(413, "invalid_payload", "request_too_large")(
                    scope, receive, send
                )
                return
            more = bool(message.get("more_body", False))

        delivered = False

        async def replay() -> dict[str, Any]:
            nonlocal delivered
            if not delivered:
                delivered = True
                return {"type": "http.request", "body": bytes(body), "more_body": False}
            return {"type": "http.request", "body": b"", "more_body": False}

        await self.app(scope, replay, send)


def create_app(
    settings: Settings | None = None,
    registry: SolverRegistry | None = None,
) -> FastAPI:
    configured = settings or Settings.from_environment()
    solvers = registry or SolverRegistry(configured)

    @asynccontextmanager
    async def lifespan(_: FastAPI):
        configured.validate()
        yield

    application = FastAPI(
        title="captcha-solver", version=SERVICE_VERSION, lifespan=lifespan
    )
    application.add_middleware(
        RequestBodyLimitMiddleware,
        maximum_bytes=configured.max_request_bytes,
        api_key=configured.api_key,
    )

    @application.get("/health")
    def health() -> dict[str, Any]:
        return {
            "ok": True,
            "authenticationRequired": bool(configured.api_key),
            "version": SERVICE_VERSION,
        }

    @application.get("/ready")
    def ready() -> JSONResponse:
        state = solvers.readiness()
        return JSONResponse(status_code=200 if state["ready"] else 503, content=state)

    @application.get("/capabilities")
    def capabilities() -> dict[str, Any]:
        return {
            "contractVersions": [1, 2],
            "capabilities": solvers.capabilities(),
            "limits": {
                "requestBytes": configured.max_request_bytes,
                "imageBytes": configured.max_image_bytes,
                "audioBytes": configured.max_audio_bytes,
                "imagePixels": configured.max_image_pixels,
                "audioSeconds": configured.max_audio_seconds,
            },
        }

    @application.post("/solve")
    async def solve(request: Request) -> JSONResponse:
        started = time.perf_counter()
        attempts_used = 0
        body: dict[str, Any] | None = None
        parsed_v2: SolveRequestV2 | None = None
        try:
            _authenticate(request, configured)
            try:
                candidate = await request.json()
            except (json.JSONDecodeError, UnicodeDecodeError) as exception:
                raise invalid_payload("O corpo não contém JSON válido.") from exception
            if not isinstance(candidate, dict):
                raise invalid_payload("O corpo JSON deve ser um objeto.")
            body = candidate

            if body.get("contractVersion") == 2:
                try:
                    parsed_v2 = SolveRequestV2.model_validate(body)
                except ValidationError as exception:
                    raise invalid_payload(
                        "O envelope V2 não respeita o schema.",
                        validationErrors=exception.errors(
                            include_url=False,
                            include_context=False,
                            include_input=False,
                        ),
                    ) from exception
                _check_deadline(parsed_v2.deadlineUtc)
                kind = parsed_v2.type
                language = parsed_v2.options.language
                if kind == "hcaptcha_image_label":
                    encoded = None
                    encoded_tiles = parsed_v2.assets.tilesBase64 or []
                else:
                    encoded_tiles = []
                    encoded = (
                        parsed_v2.assets.audioBase64
                        if kind == "recaptcha_v2_audio"
                        else parsed_v2.assets.imageBase64
                    )
            else:
                kind = body.get("type")
                language = str(body.get("language", "en"))
                if kind not in {"image", "recaptcha_v2_audio"}:
                    raise SolverError(
                        "unsupported_type",
                        f"Tipo legado não suportado: {kind!r}.",
                        422,
                        details={"suggestion": "human_handoff"},
                    )
                field = (
                    "imageBase64" if kind == "image" else "audioBase64"
                )
                encoded = body.get(field)

            if kind not in _enabled_types(configured):
                raise SolverError(
                    "unsupported_type",
                    f"Tipo não suportado ou desabilitado: {kind!r}.",
                    422,
                    details={"suggestion": "human_handoff"},
                )
            maximum = (
                configured.max_audio_bytes
                if kind == "recaptcha_v2_audio"
                else configured.max_image_bytes
            )
            if kind == "hcaptcha_image_label":
                tiles = [_decode_base64(item, maximum) for item in encoded_tiles]
                raw = b""
            else:
                raw = _decode_base64(encoded, maximum)
                tiles = []
            if (
                kind == "visual"
                and parsed_v2 is not None
                and not parsed_v2.options.allowVlmFallback
            ):
                raise SolverError(
                    "needs_configuration",
                    "A inferência visual exige allowVlmFallback=true.",
                    422,
                )
            maximum_attempts = parsed_v2.options.maxAttempts if parsed_v2 else 1
            image_uses_vlm = False
            while attempts_used < maximum_attempts:
                _check_deadline(parsed_v2.deadlineUtc if parsed_v2 else None)
                attempts_used += 1
                try:
                    if kind == "image":
                        if image_uses_vlm:
                            assert parsed_v2 is not None
                            result = await run_in_threadpool(
                                solvers.solve_visual,
                                raw,
                                task="text",
                                provider=parsed_v2.provider,
                                variant=parsed_v2.variant,
                                hint=parsed_v2.hint,
                                geometry=parsed_v2.geometry,
                                deadline=parsed_v2.deadlineUtc,
                                local_only=parsed_v2.options.localOnly,
                            )
                        else:
                            try:
                                result = await run_in_threadpool(
                                    solvers.solve_image, raw
                                )
                            except SolverError as exception:
                                if not _may_fallback_to_vlm(parsed_v2, exception):
                                    raise
                                if attempts_used >= maximum_attempts:
                                    raise
                                image_uses_vlm = True
                                continue
                    elif kind == "recaptcha_v2_audio":
                        result = await run_in_threadpool(
                            solvers.solve_audio, raw, language
                        )
                    elif kind == "hcaptcha_image_label":
                        assert parsed_v2 is not None and parsed_v2.hint is not None
                        result = await run_in_threadpool(
                            solvers.solve_hcaptcha, tiles, parsed_v2.hint
                        )
                    else:
                        assert parsed_v2 is not None and parsed_v2.task is not None
                        result = await run_in_threadpool(
                            solvers.solve_visual,
                            raw,
                            task=parsed_v2.task,
                            provider=parsed_v2.provider,
                            variant=parsed_v2.variant,
                            hint=parsed_v2.hint,
                            geometry=parsed_v2.geometry,
                            deadline=parsed_v2.deadlineUtc,
                            local_only=parsed_v2.options.localOnly,
                        )
                    break
                except SolverError as exception:
                    if not exception.retryable or attempts_used >= maximum_attempts:
                        raise
                    await asyncio.sleep(min(0.1 * attempts_used, 1.0))
            if parsed_v2 is not None:
                _check_deadline(parsed_v2.deadlineUtc)
                return JSONResponse(
                    content=_v2_success(
                        parsed_v2,
                        result.model_dump(),
                        _elapsed_ms(started),
                        attempts_used,
                    )
                )
            if result.answer is None:
                raise SolverError(
                    "contract_violation",
                    "O solver legado não produziu resposta textual.",
                    502,
                )
            return JSONResponse(
                content={
                    "success": True,
                    "text": result.answer,
                    "type": kind,
                }
            )
        except SolverError as exception:
            headers = {"Retry-After": "1"} if exception.status_code == 429 else None
            if parsed_v2 is not None:
                content = _v2_error(
                    parsed_v2, exception, _elapsed_ms(started), attempts_used
                )
            else:
                content = {
                    "success": False,
                    "error": exception.code,
                    "message": exception.message,
                    **exception.details,
                }
            return JSONResponse(
                status_code=exception.status_code, content=content, headers=headers
            )

    return application


def _authenticate(request: Request, settings: Settings) -> None:
    if not settings.api_key:
        return
    supplied = request.headers.get("authorization", "")
    if not secrets.compare_digest(supplied, f"Bearer {settings.api_key}"):
        raise SolverError("unauthorized", "Bearer inválido.", 401)


def _enabled_types(settings: Settings) -> set[str]:
    result: set[str] = set()
    if settings.enable_image:
        result.add("image")
    if settings.enable_audio:
        result.add("recaptcha_v2_audio")
    if settings.enable_vlm:
        result.add("visual")
    if settings.enable_hcaptcha:
        result.add("hcaptcha_image_label")
    return result


def _may_fallback_to_vlm(
    request: SolveRequestV2 | None,
    error: SolverError,
) -> bool:
    return (
        request is not None
        and request.options.allowVlmFallback
        and error.code
        in {
            "low_confidence",
            "model_missing",
            "model_mismatch",
            "upstream_unavailable",
        }
    )


def _decode_base64(value: Any, maximum_bytes: int) -> bytes:
    if not isinstance(value, str) or not value:
        raise invalid_payload("Asset base64 obrigatório ausente.")
    maximum_encoded = 4 * ((maximum_bytes + 2) // 3)
    if len(value) > maximum_encoded:
        raise SolverError("invalid_payload", "Asset excede o limite de bytes.", 413)
    try:
        raw = base64.b64decode(value, validate=True)
    except (ValueError, TypeError, binascii.Error) as exception:
        raise invalid_payload("Asset base64 inválido.") from exception
    if len(raw) > maximum_bytes:
        raise SolverError("invalid_payload", "Asset excede o limite de bytes.", 413)
    return raw


def _check_deadline(deadline: datetime | None) -> None:
    if deadline is None:
        return
    normalized = deadline if deadline.tzinfo else deadline.replace(tzinfo=timezone.utc)
    if normalized <= datetime.now(timezone.utc):
        raise SolverError(
            "deadline_exceeded", "O prazo da requisição expirou.", 504, retryable=False
        )


def _v2_success(
    request: SolveRequestV2,
    result: dict[str, Any],
    elapsed_ms: int,
    attempts: int,
) -> dict[str, Any]:
    answer = result.get("answer")
    actions = result.get("actions") or []
    if not actions and answer:
        actions = [
            {"kind": "TypeText", "targetRole": "response", "text": answer}
        ]
    return {
        "contractVersion": 2,
        "requestId": request.requestId,
        "challengeId": request.challengeId,
        "snapshotId": request.snapshotId,
        "status": "AnswerProduced",
        "actions": actions,
        "answer": answer,
        "tiles": result.get("tiles"),
        "solver": result["solver"],
        "modelVersion": result["model_version"],
        "confidence": result.get("confidence"),
        "attempts": attempts,
        "elapsedMs": elapsed_ms,
        "error": None,
    }


def _v2_error(
    request: SolveRequestV2,
    error: SolverError,
    elapsed_ms: int,
    attempts: int,
) -> dict[str, Any]:
    return {
        "contractVersion": 2,
        "requestId": request.requestId,
        "challengeId": request.challengeId,
        "snapshotId": request.snapshotId,
        "status": "Failed",
        "actions": [],
        "answer": None,
        "tiles": None,
        "solver": None,
        "modelVersion": None,
        "confidence": None,
        "attempts": attempts,
        "elapsedMs": elapsed_ms,
        "error": {
            "code": error.code,
            "message": error.message,
            "retryable": error.retryable,
            **error.details,
        },
    }


def _elapsed_ms(started: float) -> int:
    return max(0, round((time.perf_counter() - started) * 1000))


def _raw_error(status_code: int, code: str, message: str) -> JSONResponse:
    return JSONResponse(
        status_code=status_code,
        content={"success": False, "error": code, "message": message},
    )


app = create_app()
