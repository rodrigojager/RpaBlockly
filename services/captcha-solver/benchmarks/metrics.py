"""Métricas puras e determinísticas do corpus."""

from __future__ import annotations

import math
import re
import unicodedata
from typing import Any, Sequence

from .manifest import ExpectedAction


def text_metrics(expected: str, actual: str | None) -> dict[str, float]:
    reference = "".join(_normalized(expected).split())
    candidate = "".join(_normalized(actual or "").split())
    return {
        "exact": float(reference == candidate),
        "cer": _edit_distance(reference, candidate) / max(1, len(reference)),
    }


def transcription_metrics(expected: str, actual: str | None) -> dict[str, float]:
    reference = _words(expected)
    candidate = _words(actual or "")
    return {
        "exact": float(reference == candidate),
        "wer": _edit_distance(reference, candidate) / max(1, len(reference)),
    }


def action_metrics(
    expected: Sequence[ExpectedAction], actual: Sequence[Any]
) -> dict[str, float | None]:
    candidate = tuple(_action(item) for item in actual)
    role_kind = float(
        len(expected) == len(candidate)
        and all(
            observed is not None
            and wanted.kind == observed.kind
            and wanted.target_role == observed.target_role
            for wanted, observed in zip(expected, candidate, strict=True)
        )
    )
    if len(expected) != len(candidate) or any(item is None for item in candidate):
        return {"actionRoleKindExact": role_kind, "coordinateMae": None, "coordinateMaxError": None}

    errors: list[float] = []
    for wanted, observed in zip(expected, candidate, strict=True):
        assert observed is not None
        wanted_points = ((wanted.x, wanted.y),)
        observed_points = ((observed.x, observed.y),)
        if wanted.kind == "Drag":
            wanted_points += ((wanted.to_x, wanted.to_y),)
            observed_points += ((observed.to_x, observed.to_y),)
        for reference, point in zip(wanted_points, observed_points, strict=True):
            if not _finite_point(reference) or not _finite_point(point):
                return {"actionRoleKindExact": role_kind, "coordinateMae": None, "coordinateMaxError": None}
            assert reference[0] is not None and reference[1] is not None
            assert point[0] is not None and point[1] is not None
            errors.append(math.hypot(point[0] - reference[0], point[1] - reference[1]))
    return {
        "actionRoleKindExact": role_kind,
        "coordinateMae": sum(errors) / len(errors) if errors else 0.0,
        "coordinateMaxError": max(errors, default=0.0),
    }


def _action(value: Any) -> ExpectedAction | None:
    def field(*names: str) -> Any:
        if isinstance(value, dict):
            return next((value[name] for name in names if name in value), None)
        return next((getattr(value, name) for name in names if hasattr(value, name)), None)

    kind = field("kind")
    role = field("targetRole", "target_role")
    if not isinstance(kind, str) or not isinstance(role, str):
        return None
    return ExpectedAction(
        kind,
        role,
        _coordinate(field("x")),
        _coordinate(field("y")),
        _coordinate(field("toX", "to_x")),
        _coordinate(field("toY", "to_y")),
    )


def _coordinate(value: Any) -> float | None:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return None
    number = float(value)
    return number if math.isfinite(number) else None


def _finite_point(point: tuple[float | None, float | None]) -> bool:
    return all(value is not None and math.isfinite(value) for value in point)


def _normalized(value: str) -> str:
    return unicodedata.normalize("NFKC", value).casefold().strip()


def _words(value: str) -> tuple[str, ...]:
    return tuple(re.findall(r"\w+", _normalized(value), flags=re.UNICODE))


def _edit_distance(reference: Sequence[Any], candidate: Sequence[Any]) -> int:
    previous = list(range(len(candidate) + 1))
    for row, left in enumerate(reference, 1):
        current = [row]
        for column, right in enumerate(candidate, 1):
            current.append(
                min(
                    current[-1] + 1,
                    previous[column] + 1,
                    previous[column - 1] + (left != right),
                )
            )
        previous = current
    return previous[-1]
