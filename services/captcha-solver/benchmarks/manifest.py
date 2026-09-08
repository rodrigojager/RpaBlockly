"""Carregamento estrito do manifesto e expansão determinística de casos."""

from __future__ import annotations

import json
import math
import re
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Literal

Capability = Literal["image_ocr", "audio_transcription", "geometry"]
CAPABILITIES: tuple[Capability, ...] = (
    "image_ocr",
    "audio_transcription",
    "geometry",
)
GENERATOR_BY_CAPABILITY = {
    "image_ocr": "pillow_image_v1",
    "audio_transcription": "espeak_ng_audio_v1",
    "geometry": "pillow_geometry_v1",
}
MINIMUM_CASES = {"image_ocr": 200, "audio_transcription": 50, "geometry": 30}
_ID = re.compile(r"[a-z][a-z0-9-]{0,63}\Z")
_VERSION = re.compile(r"[1-9][0-9]*\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\Z")
_PATH_PART = re.compile(r"[A-Za-z0-9][A-Za-z0-9._-]{0,63}\Z")


class ManifestError(ValueError):
    """O manifesto não respeita o schema fechado do corpus."""


@dataclass(frozen=True)
class ExpectedAction:
    kind: str
    target_role: str
    x: float | None = None
    y: float | None = None
    to_x: float | None = None
    to_y: float | None = None


@dataclass(frozen=True)
class Cohort:
    id: str
    capability: Capability
    generator: str
    asset_path: str
    count: int
    seed: int
    params: dict[str, Any]
    thresholds: dict[str, float]


@dataclass(frozen=True)
class CorpusManifest:
    schema_version: int
    corpus_id: str
    corpus_version: str
    license: str
    cohorts: tuple[Cohort, ...]


@dataclass(frozen=True)
class CorpusCase:
    id: str
    cohort_id: str
    capability: Capability
    generator: str
    asset_path: str
    index: int
    seed: int
    params: dict[str, Any]


def load_manifest(path: str | Path) -> CorpusManifest:
    try:
        text = Path(path).read_text(encoding="utf-8")
        data = json.loads(
            text,
            object_pairs_hook=_unique_object,
            parse_constant=lambda value: _reject_constant(value),
        )
    except ManifestError:
        raise
    except (OSError, UnicodeError, json.JSONDecodeError) as exception:
        raise ManifestError("O manifesto não é JSON UTF-8 válido.") from exception
    return parse_manifest(data)


def parse_manifest(data: Any) -> CorpusManifest:
    root = _object(data, "manifesto")
    _fields(
        root,
        {"schemaVersion", "corpusId", "corpusVersion", "license", "cohorts"},
        "manifesto",
    )
    if _integer(root["schemaVersion"], "schemaVersion", 1, 1) != 1:
        raise ManifestError("schemaVersion deve ser 1.")
    version = root["corpusVersion"]
    if not isinstance(version, str) or not _VERSION.fullmatch(version):
        raise ManifestError("corpusVersion deve ser uma versão SemVer estável.")
    corpus_id = root["corpusId"]
    if not isinstance(corpus_id, str) or not _ID.fullmatch(corpus_id):
        raise ManifestError("corpusId é inválido.")
    license_id = root["license"]
    if license_id != "CC0-1.0":
        raise ManifestError("license deve ser CC0-1.0 para o corpus sintético.")
    raw_cohorts = root["cohorts"]
    if not isinstance(raw_cohorts, list) or not raw_cohorts or len(raw_cohorts) > 100:
        raise ManifestError("cohorts deve conter entre 1 e 100 itens.")

    cohorts = tuple(_parse_cohort(item, index) for index, item in enumerate(raw_cohorts))
    ids = [cohort.id for cohort in cohorts]
    paths = [cohort.asset_path for cohort in cohorts]
    if len(ids) != len(set(ids)):
        raise ManifestError("IDs de cohorts devem ser únicos.")
    if len(paths) != len(set(paths)):
        raise ManifestError("assetPath de cohorts devem ser únicos.")
    fingerprints = [
        json.dumps(
            {
                "capability": cohort.capability,
                "generator": cohort.generator,
                "count": cohort.count,
                "seed": cohort.seed,
                "params": cohort.params,
                "thresholds": cohort.thresholds,
            },
            sort_keys=True,
            separators=(",", ":"),
        )
        for cohort in cohorts
    ]
    if len(fingerprints) != len(set(fingerprints)):
        raise ManifestError("Cohorts semanticamente duplicados não são permitidos.")

    totals = {capability: 0 for capability in CAPABILITIES}
    for cohort in cohorts:
        totals[cohort.capability] += cohort.count
    for capability, minimum in MINIMUM_CASES.items():
        if totals[capability] < minimum:
            raise ManifestError(
                f"O corpus requer ao menos {minimum} casos de {capability}."
            )
    if sum(totals.values()) > 10_000:
        raise ManifestError("O corpus não pode exceder 10000 casos.")
    return CorpusManifest(1, corpus_id, version, license_id, cohorts)


def expand_cases(manifest: CorpusManifest) -> tuple[CorpusCase, ...]:
    cases = [
        CorpusCase(
            id=f"{cohort.id}-{index:04d}",
            cohort_id=cohort.id,
            capability=cohort.capability,
            generator=cohort.generator,
            asset_path=f"{cohort.asset_path}/{index:04d}",
            index=index,
            seed=cohort.seed,
            params=dict(cohort.params),
        )
        for cohort in manifest.cohorts
        for index in range(cohort.count)
    ]
    ids = [case.id for case in cases]
    if len(ids) != len(set(ids)):
        raise ManifestError("A expansão produziu IDs de casos duplicados.")
    return tuple(sorted(cases, key=lambda case: case.id))


def _parse_cohort(value: Any, index: int) -> Cohort:
    context = f"cohorts[{index}]"
    item = _object(value, context)
    _fields(
        item,
        {"id", "capability", "generator", "assetPath", "count", "seed", "params", "thresholds"},
        context,
    )
    cohort_id = item["id"]
    if not isinstance(cohort_id, str) or not _ID.fullmatch(cohort_id):
        raise ManifestError(f"{context}.id é inválido.")
    capability = item["capability"]
    if capability not in CAPABILITIES:
        raise ManifestError(f"{context}.capability é inválida.")
    generator = item["generator"]
    if generator != GENERATOR_BY_CAPABILITY[capability]:
        raise ManifestError(f"{context}.generator não é permitido.")
    asset_path = _relative_path(item["assetPath"], f"{context}.assetPath")
    count = _integer(item["count"], f"{context}.count", 1, 5_000)
    seed = _integer(item["seed"], f"{context}.seed", 0, 2**32 - 1)
    params = _params(capability, item["params"], context)
    thresholds = _thresholds(capability, item["thresholds"], context, params)
    return Cohort(
        cohort_id,
        capability,
        generator,
        asset_path,
        count,
        seed,
        params,
        thresholds,
    )


def _params(capability: Capability, value: Any, context: str) -> dict[str, Any]:
    params = _object(value, f"{context}.params")
    if capability == "image_ocr":
        _fields(params, {"width", "height", "length", "alphabet", "noise"}, f"{context}.params")
        width = _integer(params["width"], "image width", 64, 2048)
        height = _integer(params["height"], "image height", 24, 1024)
        length = _integer(params["length"], "image length", 1, 32)
        alphabet = params["alphabet"]
        if (
            not isinstance(alphabet, str)
            or not re.fullmatch(r"[a-z0-9]{2,64}", alphabet)
            or len(set(alphabet)) != len(alphabet)
        ):
            raise ManifestError("O alphabet OCR deve conter caracteres ASCII únicos.")
        noise = _number(params["noise"], "image noise", 0.0, 0.25)
        return {"width": width, "height": height, "length": length, "alphabet": alphabet, "noise": noise}

    if capability == "audio_transcription":
        _fields(params, {"language", "voice", "rate"}, f"{context}.params")
        language = params["language"]
        voice = params["voice"]
        if language != "en" or voice not in {"en", "en-us"}:
            raise ManifestError("Somente vozes inglesas fechadas são permitidas.")
        rate = _integer(params["rate"], "audio rate", 80, 300)
        return {"language": language, "voice": voice, "rate": rate}

    base = {"width", "height", "task", "origin", "jitter"}
    task_value = params.get("task")
    expected_fields = base | ({"target"} if task_value == "drag" else set())
    _fields(params, expected_fields, f"{context}.params")
    if task_value not in {"point", "drag"}:
        raise ManifestError("A tarefa geométrica deve ser point ou drag.")
    width = _integer(params["width"], "geometry width", 64, 2048)
    height = _integer(params["height"], "geometry height", 64, 2048)
    origin = _coordinate(params["origin"], width, height, "origin")
    target = (
        _coordinate(params["target"], width, height, "target")
        if task_value == "drag"
        else None
    )
    jitter = _number(params["jitter"], "geometry jitter", 0.0, min(width, height) / 4)
    for name, point in (("origin", origin), ("target", target)):
        if point is not None and not (
            jitter <= point["x"] < width - jitter
            and jitter <= point["y"] < height - jitter
        ):
            raise ManifestError(f"{name} e jitter excedem os limites da imagem.")
    result: dict[str, Any] = {
        "width": width,
        "height": height,
        "task": task_value,
        "origin": origin,
        "jitter": jitter,
    }
    if target is not None:
        result["target"] = target
    return result


def _thresholds(
    capability: Capability,
    value: Any,
    context: str,
    params: dict[str, Any],
) -> dict[str, float]:
    item = _object(value, f"{context}.thresholds")
    names = {
        "image_ocr": {"minExact", "maxCer"},
        "audio_transcription": {"minExact", "maxWer"},
        "geometry": {"minActionRoleKind", "maxCoordinateMae", "maxCoordinateError"},
    }[capability]
    _fields(item, names, f"{context}.thresholds")
    result: dict[str, float] = {}
    for name in sorted(names):
        maximum = (
            math.hypot(params["width"], params["height"])
            if name in {"maxCoordinateMae", "maxCoordinateError"}
            else 1.0
        )
        result[name] = _number(item[name], f"{context}.thresholds.{name}", 0.0, maximum)
    return result


def _coordinate(value: Any, width: int, height: int, name: str) -> dict[str, float]:
    point = _object(value, name)
    _fields(point, {"x", "y"}, name)
    return {
        "x": _number(point["x"], f"{name}.x", 0.0, math.nextafter(float(width), 0.0)),
        "y": _number(point["y"], f"{name}.y", 0.0, math.nextafter(float(height), 0.0)),
    }


def _relative_path(value: Any, name: str) -> str:
    if not isinstance(value, str) or not value or "\\" in value or value.startswith("/"):
        raise ManifestError(f"{name} deve ser um caminho relativo seguro.")
    parts = value.split("/")
    if any(part in {"", ".", ".."} or not _PATH_PART.fullmatch(part) for part in parts):
        raise ManifestError(f"{name} deve ser um caminho relativo seguro.")
    return "/".join(parts)


def _unique_object(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise ManifestError("O JSON contém campos duplicados.")
        result[key] = value
    return result


def _reject_constant(_: str) -> Any:
    raise ManifestError("Números não finitos não são permitidos.")


def _object(value: Any, name: str) -> dict[str, Any]:
    if not isinstance(value, dict) or any(not isinstance(key, str) for key in value):
        raise ManifestError(f"{name} deve ser um objeto.")
    return value


def _fields(value: dict[str, Any], expected: set[str], name: str) -> None:
    actual = set(value)
    if actual != expected:
        raise ManifestError(f"{name} possui campos ausentes ou desconhecidos.")


def _integer(value: Any, name: str, minimum: int, maximum: int) -> int:
    if isinstance(value, bool) or not isinstance(value, int) or not minimum <= value <= maximum:
        raise ManifestError(f"{name} deve ser inteiro entre {minimum} e {maximum}.")
    return value


def _number(value: Any, name: str, minimum: float, maximum: float) -> float:
    if (
        isinstance(value, bool)
        or not isinstance(value, (int, float))
        or not math.isfinite(value)
        or not minimum <= float(value) <= maximum
    ):
        raise ManifestError(f"{name} está fora dos limites permitidos.")
    return float(value)
