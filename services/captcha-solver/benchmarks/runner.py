"""Execução, avaliação de thresholds e relatórios seguros do corpus."""

from __future__ import annotations

import csv
import hashlib
import io
import json
import os
import platform
import tempfile
import time
from dataclasses import asdict
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable, Iterable

from captcha_solver.config import Settings
from captcha_solver.registry import SolverRegistry

from .generators import GeneratedCase, detect_espeak_ng, generate_case
from .manifest import CAPABILITIES, Capability, CorpusCase, CorpusManifest, ExpectedAction, expand_cases
from .metrics import action_metrics, text_metrics, transcription_metrics

_SKIP_REASONS = {
    "configuration_invalid",
    "generator_unavailable",
    "model_disabled",
    "model_unavailable",
    "unavailable",
}
_CSV_FIELDS = (
    "caseId",
    "cohortId",
    "capability",
    "status",
    "skipReason",
    "errorCode",
    "solver",
    "modelVersion",
    "assetSha256",
    "elapsedMs",
    "exact",
    "cer",
    "wer",
    "actionRoleKindExact",
    "coordinateMae",
    "coordinateMaxError",
)


@dataclass(frozen=True)
class Availability:
    available: bool
    reason: str | None = None


@dataclass(frozen=True)
class Prediction:
    answer: str | None = None
    actions: tuple[Any, ...] = ()
    solver: str | None = None
    model_version: str | None = None


class RegistryEvaluator:
    """Adaptador fino para a configuração e o registry usados em produção."""

    def __init__(self, settings: Settings, registry: SolverRegistry | None = None) -> None:
        self.settings = settings
        self.registry = registry or SolverRegistry(settings)
        self._readiness: dict[str, Any] | None = None

    @classmethod
    def from_environment(cls) -> "RegistryEvaluator":
        return cls(Settings.from_environment())

    def availability(self, capability: Capability) -> Availability:
        enabled = {
            "image_ocr": self.settings.enable_image,
            "audio_transcription": self.settings.enable_audio,
            "geometry": self.settings.enable_vlm,
        }[capability]
        if not enabled:
            return Availability(False, "model_disabled")
        if capability == "geometry" and not (
            self.settings.litellm_url
            and self.settings.litellm_api_key
            and self.settings.litellm_model
        ):
            return Availability(False, "configuration_invalid")
        if self._readiness is None:
            try:
                self._readiness = self.registry.readiness()
            except Exception:
                self._readiness = {"checks": {}}
        key = {
            "image_ocr": "image",
            "audio_transcription": "recaptcha_v2_audio",
            "geometry": "visual",
        }[capability]
        ready = self._readiness.get("checks", {}).get(key, {}).get("ready") is True
        return Availability(ready, None if ready else "model_unavailable")

    def evaluate(self, case: CorpusCase, generated: GeneratedCase) -> Any:
        if case.capability == "image_ocr":
            return self.registry.solve_image(generated.raw)
        if case.capability == "audio_transcription":
            return self.registry.solve_audio(generated.raw, case.params["language"])
        task = case.params["task"]
        hint = (
            "Clique no centro do círculo azul."
            if task == "point"
            else "Arraste o círculo verde até o quadrado vermelho."
        )
        return self.registry.solve_visual(
            generated.raw,
            task=task,
            provider="synthetic-benchmark",
            variant="geometry-v1",
            hint=hint,
            geometry={"width": case.params["width"], "height": case.params["height"]},
            deadline=None,
            local_only=False,
        )


class _UnavailableEvaluator:
    def availability(self, _: Capability) -> Availability:
        return Availability(False, "configuration_invalid")


def run_benchmark(
    manifest: CorpusManifest,
    *,
    required: Iterable[str] = (),
    evaluator: Any | None = None,
    generator: Callable[[CorpusCase, str | None], GeneratedCase] = generate_case,
    espeak_detector: Callable[[], str | None] = detect_espeak_ng,
) -> dict[str, Any]:
    required_set = _required_set(required)
    if evaluator is None:
        try:
            evaluator = RegistryEvaluator.from_environment()
        except Exception:
            evaluator = _UnavailableEvaluator()

    availability = {
        capability: _evaluator_availability(evaluator, capability)
        for capability in CAPABILITIES
    }
    espeak: str | None = None
    if availability["audio_transcription"].available:
        try:
            espeak = espeak_detector()
        except Exception:
            espeak = None
        if espeak is None:
            availability["audio_transcription"] = Availability(
                False, "generator_unavailable"
            )

    rows: list[dict[str, Any]] = []
    for case in expand_cases(manifest):
        state = availability[case.capability]
        if not state.available:
            rows.append(_row(case, "skipped", skip_reason=state.reason or "unavailable"))
            continue
        try:
            generated = generator(case, espeak)
        except Exception:
            rows.append(_row(case, "error", error_code="generation_error"))
            continue
        try:
            started = time.perf_counter_ns()
            prediction = _prediction(_evaluate(evaluator, case, generated))
            metrics = _case_metrics(case, generated, prediction)
            elapsed_ms = (time.perf_counter_ns() - started) / 1_000_000
            rows.append(
                _row(
                    case,
                    "evaluated",
                    metrics=metrics,
                    prediction=prediction,
                    asset_sha256=hashlib.sha256(generated.raw).hexdigest(),
                    elapsed_ms=elapsed_ms,
                )
            )
        except Exception:
            rows.append(_row(case, "error", error_code="evaluation_error"))

    rows.sort(key=lambda row: row["caseId"])
    cohort_reports = [
        _cohort_report(cohort.id, cohort.capability, cohort.thresholds, rows)
        for cohort in sorted(manifest.cohorts, key=lambda item: item.id)
    ]
    required_unavailable = sorted(
        capability
        for capability in required_set
        if any(
            row["capability"] == capability and row["status"] == "skipped"
            for row in rows
        )
    )
    errors = sum(row["status"] == "error" for row in rows)
    skipped = sum(row["status"] == "skipped" for row in rows)
    quality_gate_passed = errors == 0 and all(
        cohort["status"] != "failed" for cohort in cohort_reports
    )
    quality_passed = quality_gate_passed and all(
        cohort["status"] == "passed" for cohort in cohort_reports
    )
    manifest_sha256 = hashlib.sha256(
        json.dumps(
            asdict(manifest),
            sort_keys=True,
            separators=(",", ":"),
            ensure_ascii=False,
            allow_nan=False,
        ).encode("utf-8")
    ).hexdigest()
    return {
        "schemaVersion": 1,
        "runnerVersion": "1.0.0",
        "corpusId": manifest.corpus_id,
        "corpusVersion": manifest.corpus_version,
        "corpusLicense": manifest.license,
        "manifestSha256": manifest_sha256,
        "environment": {
            "pythonVersion": platform.python_version(),
            "system": platform.system().lower(),
        },
        "summary": {
            "total": len(rows),
            "evaluated": sum(row["status"] == "evaluated" for row in rows),
            "skipped": skipped,
            "errors": errors,
            "complete": skipped == 0 and errors == 0,
            "qualityPassed": quality_passed,
            "qualityGatePassed": quality_gate_passed,
            "requiredUnavailable": required_unavailable,
        },
        "cohorts": cohort_reports,
        "rows": rows,
    }


def report_exit_code(report: dict[str, Any], *, report_only: bool = False) -> int:
    summary = report["summary"]
    if summary["requiredUnavailable"]:
        return 2
    if summary["errors"]:
        return 1
    if not report_only and not summary["qualityGatePassed"]:
        return 1
    return 0


def write_reports(report: dict[str, Any], json_path: str | Path, csv_path: str | Path) -> None:
    json_target, csv_target = Path(json_path), Path(csv_path)
    if _same_path(json_target, csv_target):
        raise ValueError("Os relatórios JSON e CSV precisam de caminhos distintos.")
    encoded_json = (
        json.dumps(report, ensure_ascii=False, allow_nan=False, indent=2) + "\n"
    ).encode("utf-8")
    output = io.StringIO(newline="")
    writer = csv.DictWriter(output, fieldnames=_CSV_FIELDS, lineterminator="\n")
    writer.writeheader()
    for row in report["rows"]:
        writer.writerow({name: _csv_value(row[name]) for name in _CSV_FIELDS})
    encoded_csv = output.getvalue().encode("utf-8")
    _atomic_write(json_target, encoded_json)
    _atomic_write(csv_target, encoded_csv)


def _case_metrics(
    case: CorpusCase, generated: GeneratedCase, prediction: Prediction
) -> dict[str, float | None]:
    values: dict[str, float | None] = {
        "exact": None,
        "cer": None,
        "wer": None,
        "actionRoleKindExact": None,
        "coordinateMae": None,
        "coordinateMaxError": None,
    }
    if case.capability == "image_ocr":
        assert generated.expected_text is not None
        values.update(text_metrics(generated.expected_text, prediction.answer))
    elif case.capability == "audio_transcription":
        assert generated.expected_text is not None
        values.update(transcription_metrics(generated.expected_text, prediction.answer))
    else:
        values.update(action_metrics(generated.expected_actions, prediction.actions))
    return {key: _rounded(value) for key, value in values.items()}


def _row(
    case: CorpusCase,
    status: str,
    *,
    skip_reason: str | None = None,
    error_code: str | None = None,
    metrics: dict[str, float | None] | None = None,
    prediction: Prediction | None = None,
    asset_sha256: str | None = None,
    elapsed_ms: float | None = None,
) -> dict[str, Any]:
    values = metrics or {}
    return {
        "caseId": case.id,
        "cohortId": case.cohort_id,
        "capability": case.capability,
        "status": status,
        "skipReason": _safe_skip_reason(skip_reason),
        "errorCode": error_code,
        "solver": prediction.solver if prediction else None,
        "modelVersion": prediction.model_version if prediction else None,
        "assetSha256": asset_sha256,
        "elapsedMs": _rounded(elapsed_ms),
        "exact": values.get("exact"),
        "cer": values.get("cer"),
        "wer": values.get("wer"),
        "actionRoleKindExact": values.get("actionRoleKindExact"),
        "coordinateMae": values.get("coordinateMae"),
        "coordinateMaxError": values.get("coordinateMaxError"),
    }


def _cohort_report(
    cohort_id: str,
    capability: Capability,
    thresholds: dict[str, float],
    rows: list[dict[str, Any]],
) -> dict[str, Any]:
    selected = [row for row in rows if row["cohortId"] == cohort_id]
    evaluated = [row for row in selected if row["status"] == "evaluated"]
    errors = sum(row["status"] == "error" for row in selected)
    metrics = {
        "exact": _mean(evaluated, "exact"),
        "cer": _mean(evaluated, "cer"),
        "wer": _mean(evaluated, "wer"),
        "actionRoleKindExact": _mean(evaluated, "actionRoleKindExact"),
        "coordinateMae": _mean(evaluated, "coordinateMae"),
        "coordinateMaxError": _maximum(evaluated, "coordinateMaxError"),
    }
    displayed_thresholds = {
        "minExact": thresholds.get("minExact"),
        "maxCer": thresholds.get("maxCer"),
        "maxWer": thresholds.get("maxWer"),
        "minActionRoleKind": thresholds.get("minActionRoleKind"),
        "maxCoordinateMae": thresholds.get("maxCoordinateMae"),
        "maxCoordinateError": thresholds.get("maxCoordinateError"),
    }
    if not evaluated and errors == 0:
        status = "skipped"
    else:
        status = "passed" if errors == 0 and _passes(metrics, thresholds) else "failed"
    return {
        "cohortId": cohort_id,
        "capability": capability,
        "status": status,
        "evaluated": len(evaluated),
        "skipped": sum(row["status"] == "skipped" for row in selected),
        "errors": errors,
        "metrics": metrics,
        "thresholds": displayed_thresholds,
    }


def _passes(metrics: dict[str, float | None], thresholds: dict[str, float]) -> bool:
    comparisons = {
        "minExact": ("exact", lambda value, limit: value >= limit),
        "maxCer": ("cer", lambda value, limit: value <= limit),
        "maxWer": ("wer", lambda value, limit: value <= limit),
        "minActionRoleKind": (
            "actionRoleKindExact",
            lambda value, limit: value >= limit,
        ),
        "maxCoordinateMae": (
            "coordinateMae",
            lambda value, limit: value <= limit,
        ),
        "maxCoordinateError": (
            "coordinateMaxError",
            lambda value, limit: value <= limit,
        ),
    }
    for threshold, limit in thresholds.items():
        metric, compare = comparisons[threshold]
        value = metrics[metric]
        if value is None or not compare(value, limit):
            return False
    return True


def _evaluate(evaluator: Any, case: CorpusCase, generated: GeneratedCase) -> Any:
    method = getattr(evaluator, "evaluate", None)
    return method(case, generated) if callable(method) else evaluator(case, generated)


def _prediction(result: Any) -> Prediction:
    if isinstance(result, Prediction):
        return result
    if isinstance(result, dict):
        answer, actions = result.get("answer"), result.get("actions", ())
        solver, model_version = result.get("solver"), result.get("model_version")
    else:
        answer, actions = getattr(result, "answer", None), getattr(result, "actions", ())
        solver = getattr(result, "solver", None)
        model_version = getattr(result, "model_version", None)
    if answer is not None and not isinstance(answer, str):
        raise TypeError("answer")
    if not isinstance(actions, (list, tuple)):
        raise TypeError("actions")
    if solver is not None and not isinstance(solver, str):
        raise TypeError("solver")
    if model_version is not None and not isinstance(model_version, str):
        raise TypeError("model_version")
    return Prediction(answer, tuple(actions), solver, model_version)


def _evaluator_availability(evaluator: Any, capability: Capability) -> Availability:
    method = getattr(evaluator, "availability", None)
    if not callable(method):
        return Availability(True)
    try:
        result = method(capability)
    except Exception:
        return Availability(False, "unavailable")
    if isinstance(result, Availability):
        return Availability(result.available, _safe_skip_reason(result.reason))
    if isinstance(result, tuple) and len(result) == 2:
        return Availability(bool(result[0]), _safe_skip_reason(result[1]))
    return Availability(bool(result), None if result else "unavailable")


def _required_set(required: Iterable[str]) -> set[Capability]:
    values = list(required)
    if len(values) != len(set(values)) or any(value not in CAPABILITIES for value in values):
        raise ValueError("A lista --require contém capacidade inválida ou duplicada.")
    return set(values)  # type: ignore[return-value]


def _safe_skip_reason(value: Any) -> str | None:
    return value if isinstance(value, str) and value in _SKIP_REASONS else ("unavailable" if value else None)


def _mean(rows: list[dict[str, Any]], field: str) -> float | None:
    values = [row[field] for row in rows]
    if not values or any(value is None for value in values):
        return None
    return _rounded(sum(values) / len(values))


def _maximum(rows: list[dict[str, Any]], field: str) -> float | None:
    values = [row[field] for row in rows]
    if not values or any(value is None for value in values):
        return None
    return _rounded(max(values))


def _rounded(value: float | None) -> float | None:
    return round(float(value), 12) if value is not None else None


def _csv_value(value: Any) -> str | int | float:
    if value is None:
        return ""
    if isinstance(value, bool):
        return "true" if value else "false"
    return value


def _same_path(left: Path, right: Path) -> bool:
    return os.path.abspath(left) == os.path.abspath(right)


def _atomic_write(path: Path, data: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary = tempfile.mkstemp(prefix=f".{path.name}.", dir=path.parent)
    try:
        with os.fdopen(descriptor, "wb") as handle:
            handle.write(data)
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(temporary, path)
    except BaseException:
        try:
            os.unlink(temporary)
        except OSError:
            pass
        raise
