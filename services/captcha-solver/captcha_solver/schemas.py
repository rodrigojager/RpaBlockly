"""Envelope V2 tipado; o contrato legado continua aceito em paralelo."""

from __future__ import annotations

from datetime import datetime
from typing import Any, Literal

from pydantic import BaseModel, ConfigDict, Field, model_validator


VisualTask = Literal[
    "text",
    "grid",
    "point",
    "bounding_box",
    "slider",
    "rotation",
    "drag",
]


class SolveAssets(BaseModel):
    model_config = ConfigDict(extra="forbid")

    imageBase64: str | None = None
    audioBase64: str | None = None
    tilesBase64: list[str] | None = Field(default=None, max_length=64)


class SolveOptions(BaseModel):
    model_config = ConfigDict(extra="forbid")

    maxAttempts: int = Field(default=1, ge=1, le=10)
    localOnly: bool = True
    allowVlmFallback: bool = False
    language: str = Field(default="en", min_length=2, max_length=16)


class SolveRequestV2(BaseModel):
    model_config = ConfigDict(extra="forbid")

    contractVersion: Literal[2]
    requestId: str = Field(min_length=1, max_length=128)
    challengeId: str = Field(min_length=1, max_length=128)
    snapshotId: str = Field(min_length=1, max_length=256)
    type: Literal["image", "recaptcha_v2_audio", "visual", "hcaptcha_image_label"]
    assets: SolveAssets
    geometry: dict[str, Any] = Field(default_factory=dict)
    hint: str | None = Field(default=None, max_length=1024)
    provider: str = Field(default="generic", min_length=1, max_length=64)
    variant: str | None = Field(default=None, max_length=64)
    task: VisualTask | None = None
    deadlineUtc: datetime | None = None
    options: SolveOptions = Field(default_factory=SolveOptions)

    @model_validator(mode="after")
    def validate_assets(self) -> "SolveRequestV2":
        if self.type == "recaptcha_v2_audio":
            if not self.assets.audioBase64:
                raise ValueError("audioBase64 é obrigatório para áudio")
            if self.assets.imageBase64 or self.assets.tilesBase64:
                raise ValueError("somente audioBase64 é permitido para áudio")
        elif self.type == "hcaptcha_image_label":
            if not self.assets.tilesBase64:
                raise ValueError("tilesBase64 é obrigatório para hCaptcha")
            if self.assets.imageBase64 or self.assets.audioBase64:
                raise ValueError("somente tilesBase64 é permitido para hCaptcha")
            if not self.hint or not self.hint.strip():
                raise ValueError("hint com o prompt do desafio é obrigatório para hCaptcha")
            if self.task is not None:
                raise ValueError("task não é permitido para hCaptcha")
        elif not self.assets.imageBase64:
            raise ValueError("imageBase64 é obrigatório para imagem/visual")
        elif self.assets.audioBase64 or self.assets.tilesBase64:
            raise ValueError("somente imageBase64 é permitido para imagem/visual")
        if self.type == "visual" and self.task is None:
            raise ValueError("task é obrigatório para inferência visual")
        return self


class InferenceAction(BaseModel):
    model_config = ConfigDict(extra="forbid", allow_inf_nan=False)

    kind: Literal["TypeText", "Click", "Drag"]
    targetRole: Literal["challenge", "response", "handle"] = "challenge"
    text: str | None = Field(default=None, max_length=1024)
    x: float | None = None
    y: float | None = None
    toX: float | None = None
    toY: float | None = None


class TileDecision(BaseModel):
    model_config = ConfigDict(extra="forbid", allow_inf_nan=False)

    index: int = Field(ge=0, le=63)
    match: bool
    confidence: float | None = Field(default=None, ge=0, le=1)


class InferenceResult(BaseModel):
    model_config = ConfigDict(allow_inf_nan=False)

    answer: str | None = None
    solver: str
    model_version: str
    confidence: float | None = Field(default=None, ge=0, le=1)
    actions: list[InferenceAction] = Field(default_factory=list, max_length=100)
    tiles: list[TileDecision] | None = Field(default=None, max_length=64)
