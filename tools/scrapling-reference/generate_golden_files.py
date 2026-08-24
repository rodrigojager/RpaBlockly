"""Regenera todos os golden files de referência de forma determinística."""

from __future__ import annotations

import json
from pathlib import Path

from evaluate_fixture import evaluate


ROOT = Path(__file__).resolve().parents[2]
CASES = Path(__file__).resolve().parent / "fixtures"
OUTPUT = ROOT / "tests" / "RpaFlow.PlaywrightChecks" / "Fixtures" / "adaptive"


def main() -> None:
    OUTPUT.mkdir(parents=True, exist_ok=True)
    for case_path in sorted(CASES.glob("*.case.json")):
        result = evaluate(case_path)
        destination = OUTPUT / f"{case_path.stem.removesuffix('.case')}.golden.json"
        destination.write_text(
            json.dumps(result, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8",
            newline="\n",
        )
        print(destination.relative_to(ROOT))


if __name__ == "__main__":
    main()
