from __future__ import annotations

import io
import json
from collections import Counter
from copy import deepcopy
from dataclasses import replace
from pathlib import Path
from types import SimpleNamespace
from typing import Any

import pytest
from PIL import Image

from benchmarks import generators as corpus_generators
from benchmarks.generators import generate_case
from benchmarks.manifest import (
    CorpusManifest,
    ExpectedAction,
    ManifestError,
    expand_cases,
    load_manifest,
    parse_manifest,
)
from benchmarks.metrics import action_metrics, text_metrics, transcription_metrics
from benchmarks.run_corpus import main
from benchmarks.runner import (
    Prediction,
    report_exit_code,
    run_benchmark,
    write_reports,
)

MANIFEST_PATH = Path(__file__).parents[1] / "benchmarks" / "corpus-v1.json"


class UnavailableEvaluator:
    def availability(self, _: str) -> tuple[bool, str]:
        return False, "model_unavailable"

    def evaluate(self, *_: Any) -> Prediction:
        raise AssertionError("casos indisponíveis não podem ser avaliados")


def _data() -> dict[str, Any]:
    return json.loads(MANIFEST_PATH.read_text(encoding="utf-8"))


def _small_manifest(capability: str, *, count: int = 2) -> CorpusManifest:
    manifest = load_manifest(MANIFEST_PATH)
    cohort = next(item for item in manifest.cohorts if item.capability == capability)
    return replace(manifest, cohorts=(replace(cohort, count=count),))


def test_manifest_expands_exact_versioned_counts_without_binary_assets() -> None:
    manifest = load_manifest(MANIFEST_PATH)
    cases = expand_cases(manifest)
    counts = Counter(case.capability for case in cases)

    assert manifest.schema_version == 1
    assert manifest.corpus_version == "1.0.0"
    assert counts == {
        "image_ocr": 200,
        "audio_transcription": 50,
        "geometry": 30,
    }
    assert len(cases) == 280
    assert [case.id for case in cases] == sorted(case.id for case in cases)
    assert len({case.id for case in cases}) == len(cases)
    assert not any(
        path.suffix.lower() in {".png", ".wav", ".mp3", ".jpg", ".jpeg"}
        for path in MANIFEST_PATH.parent.iterdir()
        if path.is_file()
    )


def test_pillow_image_and_geometry_generation_are_deterministic() -> None:
    cases = expand_cases(load_manifest(MANIFEST_PATH))
    for capability in ("image_ocr", "geometry"):
        case = next(item for item in cases if item.capability == capability)
        first = generate_case(case)
        second = generate_case(case)
        assert first == second
        assert first.raw.startswith(b"\x89PNG\r\n\x1a\n")
        with Image.open(io.BytesIO(first.raw)) as image:
            image.verify()


def test_optional_audio_generation_uses_an_explicit_espeak_executable(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    executable = tmp_path / "espeak-ng.exe"
    executable.write_bytes(b"")
    observed: list[tuple[list[str], dict[str, Any]]] = []

    def fake_run(command: list[str], **kwargs: Any) -> SimpleNamespace:
        observed.append((command, kwargs))
        return SimpleNamespace(stdout=b"RIFFdeterministic-wave")

    monkeypatch.setattr(corpus_generators.subprocess, "run", fake_run)
    case = next(
        item
        for item in expand_cases(load_manifest(MANIFEST_PATH))
        if item.capability == "audio_transcription"
    )
    first = generate_case(case, str(executable))
    second = generate_case(case, str(executable))

    assert first == second
    assert len(observed) == 2
    assert observed[0][0][0] == str(executable)
    assert observed[0][0][1] == "--stdout"
    assert "shell" not in observed[0][1]


def test_manifest_rejects_unknown_fields_generators_duplicates_and_bad_paths() -> None:
    unknown = _data()
    unknown["unexpected"] = True
    with pytest.raises(ManifestError):
        parse_manifest(unknown)

    generator = _data()
    generator["cohorts"][0]["generator"] = "shell_command_v1"
    with pytest.raises(ManifestError):
        parse_manifest(generator)

    duplicate = _data()
    duplicate["cohorts"].append(deepcopy(duplicate["cohorts"][0]))
    with pytest.raises(ManifestError, match="únicos"):
        parse_manifest(duplicate)

    for unsafe in ("../escape", "/absolute/path", "C:\\absolute", "a//b"):
        path = _data()
        path["cohorts"][0]["assetPath"] = unsafe
        with pytest.raises(ManifestError, match="relativo seguro"):
            parse_manifest(path)


@pytest.mark.parametrize(
    "change",
    [
        lambda data: data["cohorts"][0].__setitem__("count", 0),
        lambda data: data["cohorts"][0]["params"].__setitem__("width", 1),
        lambda data: data["cohorts"][0]["thresholds"].__setitem__("minExact", 1.1),
        lambda data: data["cohorts"][3]["params"]["origin"].__setitem__("x", float("nan")),
        lambda data: data["cohorts"][3]["params"]["origin"].__setitem__("y", float("inf")),
    ],
)
def test_manifest_rejects_invalid_counts_bounds_and_nonfinite_coordinates(
    change: Any,
) -> None:
    data = _data()
    change(data)
    with pytest.raises(ManifestError):
        parse_manifest(data)


def test_json_loader_rejects_duplicate_object_fields(tmp_path: Path) -> None:
    path = tmp_path / "duplicate.json"
    path.write_text(
        '{"schemaVersion":1,"schemaVersion":1,"corpusVersion":"1.0.0","cohorts":[]}',
        encoding="utf-8",
    )
    with pytest.raises(ManifestError, match="duplicados"):
        load_manifest(path)


def test_text_and_transcription_metric_edge_cases() -> None:
    assert text_metrics("", "") == {"exact": 1.0, "cer": 0.0}
    assert text_metrics("", "xyz") == {"exact": 0.0, "cer": 3.0}
    assert text_metrics("A b C", "abc") == {"exact": 1.0, "cer": 0.0}
    assert text_metrics("kitten", "sitting")["cer"] == pytest.approx(0.5)

    assert transcription_metrics("Hello, WORLD!", "hello world") == {
        "exact": 1.0,
        "wer": 0.0,
    }
    assert transcription_metrics("", "one two") == {"exact": 0.0, "wer": 2.0}
    assert transcription_metrics("one two three", "one four")["wer"] == pytest.approx(
        2 / 3
    )


def test_action_metrics_cover_role_kind_and_coordinate_failures() -> None:
    expected = (ExpectedAction("Click", "challenge", 10.0, 20.0),)
    exact = action_metrics(
        expected,
        [{"kind": "Click", "targetRole": "challenge", "x": 13.0, "y": 24.0}],
    )
    assert exact == {
        "actionRoleKindExact": 1.0,
        "coordinateMae": 5.0,
        "coordinateMaxError": 5.0,
    }

    wrong_role = action_metrics(
        expected,
        [{"kind": "Click", "targetRole": "handle", "x": 10.0, "y": 20.0}],
    )
    assert wrong_role["actionRoleKindExact"] == 0.0
    missing = action_metrics(
        expected,
        [{"kind": "Click", "targetRole": "challenge", "x": None, "y": 20.0}],
    )
    assert missing["coordinateMae"] is None
    assert missing["coordinateMaxError"] is None


def test_reports_are_ordered_utf8_lf_and_redact_assets_errors_and_paths(
    tmp_path: Path,
) -> None:
    secret = "router-secret-value"

    class FailingEvaluator:
        def availability(self, capability: str) -> bool:
            return capability == "image_ocr"

        def evaluate(self, *_: Any) -> Prediction:
            raise RuntimeError(f"{secret} at {tmp_path.resolve()}")

    manifest = _small_manifest("image_ocr")
    report = run_benchmark(manifest, evaluator=FailingEvaluator())
    json_path, csv_path = tmp_path / "report.json", tmp_path / "report.csv"
    write_reports(report, json_path, csv_path)

    json_bytes = json_path.read_bytes()
    csv_bytes = csv_path.read_bytes()
    assert json_bytes.startswith(b"{\n") and not json_bytes.startswith(b"\xef\xbb\xbf")
    assert json_bytes.endswith(b"\n") and b"\r\n" not in json_bytes
    assert csv_bytes.endswith(b"\n") and b"\r\n" not in csv_bytes
    assert csv_bytes.decode("utf-8").splitlines()[0].split(",") == [
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
    ]

    serialized = json_bytes.decode("utf-8") + csv_bytes.decode("utf-8")
    assert secret not in serialized
    assert str(tmp_path.resolve()) not in serialized
    assert "base64" not in serialized.casefold()
    assert "assetPath" not in serialized
    parsed = json.loads(json_bytes)
    assert len(parsed["rows"]) == 2
    assert [row["caseId"] for row in parsed["rows"]] == sorted(
        row["caseId"] for row in parsed["rows"]
    )
    assert {row["errorCode"] for row in parsed["rows"]} == {"evaluation_error"}


def test_optional_skip_and_required_unavailable_write_reports(
    tmp_path: Path,
) -> None:
    optional_json, optional_csv = tmp_path / "optional.json", tmp_path / "optional.csv"
    optional_code = main(
        [
            "--manifest",
            str(MANIFEST_PATH),
            "--json",
            str(optional_json),
            "--csv",
            str(optional_csv),
        ],
        evaluator=UnavailableEvaluator(),
    )
    optional = json.loads(optional_json.read_text(encoding="utf-8"))
    assert optional_code == 0
    assert optional["summary"] == {
        "total": 280,
        "evaluated": 0,
        "skipped": 280,
        "errors": 0,
        "complete": False,
        "qualityPassed": False,
        "qualityGatePassed": True,
        "requiredUnavailable": [],
    }

    required_json, required_csv = tmp_path / "required.json", tmp_path / "required.csv"
    required_code = main(
        [
            "--manifest",
            str(MANIFEST_PATH),
            "--json",
            str(required_json),
            "--csv",
            str(required_csv),
            "--require",
            "image_ocr,audio_transcription",
        ],
        evaluator=UnavailableEvaluator(),
    )
    required = json.loads(required_json.read_text(encoding="utf-8"))
    assert required_code == 2
    assert required["summary"]["requiredUnavailable"] == [
        "audio_transcription",
        "image_ocr",
    ]
    assert required_json.is_file() and required_csv.is_file()


def test_manifest_thresholds_fail_quality_but_report_only_returns_zero() -> None:
    class WrongEvaluator:
        def availability(self, capability: str) -> bool:
            return capability == "image_ocr"

        def evaluate(self, *_: Any) -> Prediction:
            return Prediction(answer="definitely-wrong")

    report = run_benchmark(
        _small_manifest("image_ocr", count=1), evaluator=WrongEvaluator()
    )
    assert report["summary"]["qualityPassed"] is False
    assert report["cohorts"][0]["status"] == "failed"
    assert report_exit_code(report) == 1
    assert report_exit_code(report, report_only=True) == 0
