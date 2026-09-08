"""Geradores locais do corpus; nenhum binário é persistido ou lido do manifesto."""

from __future__ import annotations

import hashlib
import io
import random
import shutil
import subprocess
from dataclasses import dataclass
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

from .manifest import CorpusCase, ExpectedAction

_WORDS = ("zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine")


@dataclass(frozen=True)
class GeneratedCase:
    raw: bytes
    expected_text: str | None = None
    expected_actions: tuple[ExpectedAction, ...] = ()


def detect_espeak_ng() -> str | None:
    found = shutil.which("espeak-ng")
    if not found:
        return None
    path = Path(found).resolve()
    if not path.is_file() or path.name.lower() not in {"espeak-ng", "espeak-ng.exe"}:
        return None
    return str(path)


def generate_case(case: CorpusCase, espeak_executable: str | None = None) -> GeneratedCase:
    if case.generator == "pillow_image_v1":
        text = _image_text(case)
        return GeneratedCase(_image_png(case, text), expected_text=text)
    if case.generator == "espeak_ng_audio_v1":
        if espeak_executable is None:
            raise RuntimeError("espeak-ng indisponível")
        text = _audio_text(case)
        return GeneratedCase(_audio_wav(case, text, espeak_executable), expected_text=text)
    if case.generator == "pillow_geometry_v1":
        actions = _geometry_actions(case)
        return GeneratedCase(_geometry_png(case, actions), expected_actions=actions)
    raise RuntimeError("gerador não permitido")


def _image_text(case: CorpusCase) -> str:
    alphabet = case.params["alphabet"]
    rng = random.Random(_stable_seed(case, "text"))
    return "".join(rng.choice(alphabet) for _ in range(case.params["length"]))


def _audio_text(case: CorpusCase) -> str:
    digest = _digest(case, "audio-text")
    return " ".join(_WORDS[digest[index] % len(_WORDS)] for index in range(3))


def _image_png(case: CorpusCase, text: str) -> bytes:
    width, height = case.params["width"], case.params["height"]
    rng = random.Random(_stable_seed(case, "pixels"))
    image = Image.new("L", (width, height), 245)
    draw = ImageDraw.Draw(image)
    font = ImageFont.load_default(size=max(14, min(42, height * 3 // 5)))
    box = draw.textbbox((0, 0), text, font=font, stroke_width=1)
    x = (width - (box[2] - box[0])) // 2
    y = (height - (box[3] - box[1])) // 2 - box[1]
    draw.text((x, y), text, fill=25, font=font, stroke_width=1, stroke_fill=25)
    for _ in range(int(width * height * case.params["noise"])):
        shade = rng.randrange(80, 225)
        draw.point((rng.randrange(width), rng.randrange(height)), fill=shade)
    return _png_bytes(image)


def _audio_wav(case: CorpusCase, text: str, executable: str) -> bytes:
    path = Path(executable)
    if not path.is_file() or path.name.lower() not in {"espeak-ng", "espeak-ng.exe"}:
        raise RuntimeError("executável espeak-ng inválido")
    completed = subprocess.run(
        [
            str(path),
            "--stdout",
            "-v",
            case.params["voice"],
            "-s",
            str(case.params["rate"]),
            text,
        ],
        check=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.DEVNULL,
        timeout=15,
    )
    if not completed.stdout.startswith(b"RIFF") or len(completed.stdout) > 5 * 1024 * 1024:
        raise RuntimeError("saída espeak-ng inválida")
    return completed.stdout


def _geometry_actions(case: CorpusCase) -> tuple[ExpectedAction, ...]:
    origin = _jittered(case, case.params["origin"], "origin")
    if case.params["task"] == "point":
        return (ExpectedAction("Click", "challenge", *origin),)
    target = _jittered(case, case.params["target"], "target")
    return (ExpectedAction("Drag", "handle", *origin, *target),)


def _geometry_png(case: CorpusCase, actions: tuple[ExpectedAction, ...]) -> bytes:
    width, height = case.params["width"], case.params["height"]
    image = Image.new("RGB", (width, height), (246, 244, 238))
    draw = ImageDraw.Draw(image)
    for x in range(0, width, 20):
        draw.line((x, 0, x, height), fill=(224, 222, 216), width=1)
    for y in range(0, height, 20):
        draw.line((0, y, width, y), fill=(224, 222, 216), width=1)
    action = actions[0]
    assert action.x is not None and action.y is not None
    if action.kind == "Click":
        radius = 12
        draw.ellipse(
            (action.x - radius, action.y - radius, action.x + radius, action.y + radius),
            fill=(20, 104, 180),
            outline=(8, 46, 82),
            width=3,
        )
    else:
        assert action.to_x is not None and action.to_y is not None
        draw.line((action.x, action.y, action.to_x, action.to_y), fill=(180, 62, 38), width=5)
        draw.ellipse((action.x - 10, action.y - 10, action.x + 10, action.y + 10), fill=(28, 116, 91))
        draw.rectangle((action.to_x - 11, action.to_y - 11, action.to_x + 11, action.to_y + 11), fill=(180, 62, 38))
    return _png_bytes(image)


def _jittered(case: CorpusCase, point: dict[str, float], label: str) -> tuple[float, float]:
    jitter = case.params["jitter"]
    digest = _digest(case, label)
    dx = ((digest[0] / 255.0) * 2.0 - 1.0) * jitter
    dy = ((digest[1] / 255.0) * 2.0 - 1.0) * jitter
    return round(point["x"] + dx, 6), round(point["y"] + dy, 6)


def _png_bytes(image: Image.Image) -> bytes:
    output = io.BytesIO()
    image.save(output, format="PNG", optimize=False, compress_level=9)
    return output.getvalue()


def _stable_seed(case: CorpusCase, label: str) -> int:
    return int.from_bytes(_digest(case, label)[:8], "big")


def _digest(case: CorpusCase, label: str) -> bytes:
    value = f"{case.seed}:{case.cohort_id}:{case.index}:{label}".encode("ascii")
    return hashlib.sha256(value).digest()
