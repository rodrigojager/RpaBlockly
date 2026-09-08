"""Núcleo do serviço self-hosted de inferência de captchas."""

from typing import Any


def create_app(*args: Any, **kwargs: Any) -> Any:
    """Importa a fábrica sem inicializar a aplicação ao carregar o pacote."""
    from .main import create_app as factory

    return factory(*args, **kwargs)

__all__ = ["create_app"]
