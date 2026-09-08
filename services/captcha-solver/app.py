"""Entrada compatível do uvicorn: ``uvicorn app:app``."""

from captcha_solver.main import app, create_app

__all__ = ["app", "create_app"]
