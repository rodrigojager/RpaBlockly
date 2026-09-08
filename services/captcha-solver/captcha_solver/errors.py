"""Erros estáveis compartilhados pelos contratos legado e V2."""

from __future__ import annotations

from typing import Any


class SolverError(Exception):
    def __init__(
        self,
        code: str,
        message: str,
        status_code: int,
        *,
        retryable: bool = False,
        details: dict[str, Any] | None = None,
    ) -> None:
        super().__init__(message)
        self.code = code
        self.message = message
        self.status_code = status_code
        self.retryable = retryable
        self.details = details or {}


def invalid_payload(message: str, **details: Any) -> SolverError:
    return SolverError("invalid_payload", message, 400, details=details)
