"""Corpus versionado e runner de benchmark do captcha-solver."""

from .manifest import (
    CAPABILITIES,
    CorpusCase,
    CorpusManifest,
    ManifestError,
    expand_cases,
    load_manifest,
    parse_manifest,
)
from .metrics import action_metrics, text_metrics, transcription_metrics
from .runner import Prediction, RegistryEvaluator, run_benchmark, write_reports

__all__ = [
    "CAPABILITIES",
    "CorpusCase",
    "CorpusManifest",
    "ManifestError",
    "Prediction",
    "RegistryEvaluator",
    "action_metrics",
    "expand_cases",
    "load_manifest",
    "parse_manifest",
    "run_benchmark",
    "text_metrics",
    "transcription_metrics",
    "write_reports",
]
