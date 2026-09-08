from __future__ import annotations

import base64
import io
import os

import pytest
from PIL import Image, ImageFile

from captcha_solver.config import Settings
from captcha_solver.errors import SolverError
from captcha_solver.registry import SolverRegistry


IMAGE_A3X9Z = (
    "iVBORw0KGgoAAAANSUhEUgAAAKAAAAA8CAYAAADha7EVAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAP4SURBVHhe7ZxBcuowEERzXQ7B1muuAFdgyZYbcApOoWAKEtvqlkeWorFJv6pZRRFC8zQjper/ryCEIxJQuCIBhSsSULgiAYUrElC4IgGFKxJQuCIBhSsSULgiAYUrElC4IgGFKxJQuCIBhStft9stlIYQS6lSAZGULUNsl49owUjKliGWoztgBZCULWPLrF7A+6ULu90uiu5yf43w5B4uXby23a4LLZeHpGwZJaxWQCbeNI5OBeB2xOsZxzH8hwaNpLTGKgW0yveOthKyqseibTXcGusT8H4JHUxkKtol2Vb5JtFdHtoKxOoEhNVvVOJu4Tj9+SOa3AnJ4Rgv7xj9vI913FnXx8oEBO0NVQ8kQoM+jA4HFAutT1UQUlfAufZpSsK4wpkTHAmIKyUTFd87h60d3f14649b9XRs7l1yGJ9zr6wj4Jx40yitVqDNQVFJO4w/3tLWkTD8lTtfLSVgTwUBSaWZCSiMBSgVFwE+GiaV2DKmhoDjgycBe4oFXPQqfEbO38hSyZpLxkx1g0KjOdEa+GfDfZGAEWUCwtYLNsecZAarskaJ6edbWu8vSCo8lqw38yFCD3fpFWZFVHqE/G442xu0meZ9TN0xjZOYK3VKEnKnHEvIDssjMgTEj6K8ObZAJQHn+TMBn2GphAkxfmKuKpe0zUcY5aHyZV1btsHfC5iQZ2kngQmyJJdUsHfYHkYWkUlY1kj363PufUMqC5hXIZYK2GO/j42hrTirtc1LeLwAkWY/g89bsldrpoqAvGWko2hTUaWwTMiqYJaAL9Bc73my18cPr60yb5NiAdOX+9+2UXQHhIBqUVBh+qiaaCBnan66j59a+l6UCYgqANkwq4DTcXz/8wVMH5Y+6t2zUFdg3+W/vHgRRQLGCeWvNIuAMBE8a3ktbuYB8hPJlMfS46qW8Udruq7Pe/EiCgTM2GTysot8gePwnEho3uJw64UPhUfQedD6kLCpu+EQsi81K/HaqSxgH5OTm6g8ccGyzYlbaV71fQuB2x8TgKxv+EWsh40cCjz2c6ncgvMCbjStCumgVQsegKFgRCrSihe9+MEXXTTPMJJXhbagf+thjbJHSI4sXReNZdJkJ4eWDFxlos8l3wOvj1VpEjVFHsZgXpTYllFCmYA9FgmfgoDEpU6x8dFAK9+DVOudUqUVT+Nwhgnr43ra49+xxv4Urq+5tky5gC8syY6TPH/ZppViUPWmyX3G+QB/73AGY59xDad9PH6Y6Ciup7Cfju8jdbBe1KyAW6aKgDA5DUNsF/3vWMKVai1YiCVIQOGKBBSuSEDhigQUrkhA4YoEFK5IQOGKBBSuSEDhigQUrkhA4YoEFK5IQOFICN8WQpe3OBxNtAAAAABJRU5ErkJggg=="
)


@pytest.mark.skipif(
    os.environ.get("RPABLOCKLY_REQUIRE_CAPTCHA_MODELS") != "1",
    reason=(
        "smoke real desabilitado; CI obrigatório usa "
        "RPABLOCKLY_REQUIRE_CAPTCHA_MODELS=1"
    ),
)
def test_pinned_ocr_model_decodes_fixture() -> None:
    settings = Settings.from_environment()
    raw_truncated = base64.b64decode(IMAGE_A3X9Z)
    ImageFile.LOAD_TRUNCATED_IMAGES = True
    with Image.open(io.BytesIO(raw_truncated)) as source:
        source.load()
        repaired = io.BytesIO()
        source.save(repaired, format="PNG")

    registry = SolverRegistry(settings)
    with pytest.raises(SolverError, match="não pôde ser inferida") as failure:
        registry.solve_image(raw_truncated)
    assert failure.value.code == "decode_failed"

    result = registry.solve_image(repaired.getvalue())
    assert result.answer == "a3x9z"
    assert result.model_version == "1.4.11-common-old"
