"""CLI: python -m benchmarks.run_corpus."""

from __future__ import annotations

import argparse
import sys
from pathlib import Path
from typing import Any, Sequence

from .manifest import CAPABILITIES, ManifestError, load_manifest
from .runner import report_exit_code, run_benchmark, write_reports


def main(argv: Sequence[str] | None = None, *, evaluator: Any | None = None) -> int:
    parser = argparse.ArgumentParser(description="Executa o corpus versionado de CAPTCHA.")
    parser.add_argument(
        "--manifest",
        default=str(Path(__file__).with_name("corpus-v1.json")),
        help="manifesto JSON schemaVersion 1",
    )
    parser.add_argument("--json", required=True, help="relatório JSON de saída")
    parser.add_argument("--csv", required=True, help="relatório CSV de saída")
    parser.add_argument(
        "--require",
        default="",
        help="capacidades obrigatórias separadas por vírgula",
    )
    parser.add_argument(
        "--report-only",
        action="store_true",
        help="não falha o processo por thresholds de qualidade",
    )
    arguments = parser.parse_args(argv)
    try:
        required = _parse_require(arguments.require)
        manifest = load_manifest(arguments.manifest)
        report = run_benchmark(manifest, required=required, evaluator=evaluator)
        write_reports(report, arguments.json, arguments.csv)
    except (ManifestError, OSError, UnicodeError, ValueError) as exception:
        print(f"benchmark inválido: {exception}", file=sys.stderr)
        return 2
    return report_exit_code(report, report_only=arguments.report_only)


def _parse_require(value: str) -> tuple[str, ...]:
    if not isinstance(value, str):
        raise ValueError("--require deve ser uma lista separada por vírgula.")
    items = tuple(part.strip() for part in value.split(",") if part.strip())
    if len(items) != len(set(items)) or any(item not in CAPABILITIES for item in items):
        raise ValueError("--require contém capacidade inválida ou duplicada.")
    return items


if __name__ == "__main__":
    raise SystemExit(main())
