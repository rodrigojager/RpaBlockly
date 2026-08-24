"""Executa a relocalização real do Scrapling sobre fixtures sanitizadas."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

from scrapling import Selector
from scrapling.core.utils._utils import _StorageTools


def evaluate(case_path: Path) -> dict:
    case = json.loads(case_path.read_text(encoding="utf-8"))
    root = case_path.parent
    original_html = (root / case["originalHtml"]).read_text(encoding="utf-8")
    changed_html = (root / case["changedHtml"]).read_text(encoding="utf-8")
    original = Selector(original_html, url="https://fixture.invalid", adaptive=False)
    changed = Selector(changed_html, url="https://fixture.invalid", adaptive=False)
    original_element = original.css(case["originalSelector"])[0]
    fingerprint = _StorageTools.element_to_dict(original_element._root)

    scores: list[dict] = []
    for index, candidate in enumerate(changed.css("*")):
        score = changed._Selector__calculate_similarity_score(
            fingerprint, candidate._root
        )
        scores.append(
            {
                "index": index,
                "tag": candidate.tag,
                "id": candidate.attrib.get("id"),
                "dataId": candidate.attrib.get("data-id"),
                "score": score,
            }
        )

    scores.sort(key=lambda item: (-item["score"], item["index"]))
    highest = scores[0]["score"] if scores else 0
    winners = [item for item in scores if item["score"] == highest]
    return {
        "reference": {
            "package": "scrapling",
            "version": "0.4.14",
            "commit": "5d213a2d4764002bfc4fed33c32fe09fa8b0bf7f",
        },
        "case": case["id"],
        "minimumPercentage": case["minimumPercentage"],
        "accepted": highest >= case["minimumPercentage"],
        "highestScore": highest,
        "tieCount": len(winners),
        "winners": winners,
        "ranking": scores,
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("case", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    result = evaluate(args.case.resolve())
    rendered = json.dumps(result, ensure_ascii=False, indent=2) + "\n"
    if args.output:
        args.output.resolve().write_text(rendered, encoding="utf-8", newline="\n")
    else:
        print(rendered, end="")


if __name__ == "__main__":
    main()
