"""Provisiona modelos fixados antes de iniciar o serviço.

Não é chamado por uma requisição operacional. Publica o ONNX somente depois de
validar tamanho e SHA-256 do wheel e do arquivo interno.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
import tempfile
import urllib.request
import zipfile
from pathlib import Path
from typing import BinaryIO
from urllib.parse import urlsplit


_HCAPTCHA_ASSET_HOSTS = {
    "objects.githubusercontent.com",
    "release-assets.githubusercontent.com",
}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        while chunk := stream.read(1024 * 1024):
            digest.update(chunk)
    return digest.hexdigest().upper()


def require_file(path: Path, expected_hash: str, expected_size: int | None) -> None:
    if not path.is_file():
        raise RuntimeError(f"model_missing: {path} não existe.")
    if expected_size is not None and path.stat().st_size != expected_size:
        raise RuntimeError(f"model_mismatch: tamanho inválido para {path.name}.")
    actual = sha256(path)
    if actual != expected_hash.upper():
        raise RuntimeError(
            f"model_mismatch: SHA-256 de {path.name} é {actual}; "
            f"esperado {expected_hash.upper()}."
        )


def copy_limited(source: BinaryIO, output: BinaryIO, maximum_bytes: int) -> None:
    total = 0
    while chunk := source.read(min(1024 * 1024, maximum_bytes - total + 1)):
        total += len(chunk)
        if total > maximum_bytes:
            raise RuntimeError("model_mismatch: artefato excede o tamanho fixado.")
        output.write(chunk)


def provision_ocr(
    manifest_path: Path,
    destination: Path,
    charset_source: Path,
    *,
    force: bool,
    verify_only: bool,
) -> None:
    manifest = json.loads(manifest_path.read_text("utf-8"))
    models = manifest.get("models", [])
    if manifest.get("schemaVersion") != 1 or len(models) != 1:
        raise RuntimeError("model_mismatch: manifesto OCR inválido.")
    model = models[0]
    destination.mkdir(parents=True, exist_ok=True)
    target = destination / model["publishedFile"]
    charset_target = destination / "ocr-charset.json"
    manifest_target = destination / "captcha-models.manifest.json"

    require_file(
        charset_source,
        model["charset"]["sha256"],
        None,
    )
    if target.exists():
        try:
            require_file(target, model["modelSha256"], model["modelSizeBytes"])
            shutil.copy2(charset_source, charset_target)
            shutil.copy2(manifest_path, manifest_target)
            print(f"cache_valid: {target}")
            return
        except RuntimeError:
            if verify_only or not force:
                raise
    elif verify_only:
        raise RuntimeError(f"model_missing: {target} não existe.")

    with tempfile.TemporaryDirectory(dir=destination) as temporary_name:
        temporary = Path(temporary_name)
        artifact = temporary / "artifact.whl"
        request = urllib.request.Request(
            model["source"]["artifactUrl"],
            headers={"User-Agent": "RpaBlockly-captcha-model-provisioner/1"},
        )
        with urllib.request.urlopen(request, timeout=120) as response:
            if response.geturl() != model["source"]["artifactUrl"]:
                raise RuntimeError("model_mismatch: redirect de modelo não permitido.")
            with artifact.open("wb") as output:
                copy_limited(
                    response,
                    output,
                    model["source"]["artifactSizeBytes"],
                )
        require_file(
            artifact,
            model["source"]["artifactSha256"],
            model["source"]["artifactSizeBytes"],
        )

        extracted = temporary / "model.onnx"
        with zipfile.ZipFile(artifact) as archive:
            try:
                with archive.open(model["source"]["innerPath"]) as source:
                    with extracted.open("wb") as output:
                        copy_limited(source, output, model["modelSizeBytes"])
            except KeyError as exception:
                raise RuntimeError(
                    "model_missing: arquivo ONNX fixado não existe no wheel."
                ) from exception
        require_file(extracted, model["modelSha256"], model["modelSizeBytes"])

        published = destination / f".{target.name}.{os.getpid()}.tmp"
        shutil.copy2(extracted, published)
        os.replace(published, target)
        shutil.copy2(charset_source, charset_target)
        shutil.copy2(manifest_path, manifest_target)
        print(f"model_ready: {target}")


def provision_whisper(model: str, revision: str) -> None:
    from huggingface_hub import snapshot_download

    snapshot_download(
        repo_id=f"Systran/faster-whisper-{model}",
        revision=revision,
    )
    print(f"whisper_ready: {model}@{revision}")


def _validate_hcaptcha_download_url(original_url: str, final_url: str) -> None:
    if final_url == original_url:
        return
    try:
        parsed = urlsplit(final_url)
        port = parsed.port
    except ValueError as exception:
        raise RuntimeError(
            "model_mismatch: redirect de modelo para URL inválida."
        ) from exception
    if (
        parsed.scheme != "https"
        or parsed.hostname not in _HCAPTCHA_ASSET_HOSTS
        or parsed.username
        or parsed.password
        or port not in {None, 443}
        or parsed.fragment
    ):
        raise RuntimeError(
            "model_mismatch: redirect de modelo para host não permitido."
        )


def provision_hcaptcha(
    manifest_path: Path,
    destination: Path,
    license_path: Path,
    *,
    force: bool,
    verify_only: bool,
) -> None:
    """Publica os classificadores binários fixados do model hub hCaptcha.

    Os assets do GitHub Releases usam redirect assinado para a CDN; a
    integridade é garantida pelo SHA-256 fixado no manifesto.
    """
    manifest = json.loads(manifest_path.read_text("utf-8"))
    models = manifest.get("models", [])
    if manifest.get("schemaVersion") != 1 or not models:
        raise RuntimeError("model_mismatch: manifesto hCaptcha inválido.")
    if manifest.get("suite") != "hcaptcha-resnet-binary":
        raise RuntimeError("model_mismatch: suíte hCaptcha desconhecida.")
    if not license_path.is_file():
        raise RuntimeError(
            f"model_missing: licença dos modelos não encontrada em {license_path}."
        )
    destination.mkdir(parents=True, exist_ok=True)

    for model in models:
        target = destination / model["publishedFile"]
        if target.exists():
            try:
                require_file(target, model["modelSha256"], model["modelSizeBytes"])
                print(f"cache_valid: {target}")
                continue
            except RuntimeError:
                if verify_only or not force:
                    raise
        elif verify_only:
            raise RuntimeError(f"model_missing: {target} não existe.")

        with tempfile.TemporaryDirectory(dir=destination) as temporary_name:
            temporary = Path(temporary_name) / "model.onnx"
            request = urllib.request.Request(
                model["artifactUrl"],
                headers={"User-Agent": "RpaBlockly-captcha-model-provisioner/1"},
            )
            with urllib.request.urlopen(request, timeout=120) as response:
                _validate_hcaptcha_download_url(
                    model["artifactUrl"], response.geturl()
                )
                with temporary.open("wb") as output:
                    copy_limited(response, output, model["modelSizeBytes"])
            require_file(temporary, model["modelSha256"], model["modelSizeBytes"])

            published = destination / f".{target.name}.{os.getpid()}.tmp"
            shutil.copy2(temporary, published)
            os.replace(published, target)
            print(f"model_ready: {target}")

    license_target = destination / "LICENSE"
    if verify_only:
        if not license_target.is_file() or sha256(license_target) != sha256(license_path):
            raise RuntimeError(
                "model_mismatch: licença provisionada não corresponde à fonte versionada."
            )
        print(f"cache_valid: {license_target}")
    else:
        shutil.copy2(license_path, license_target)
        print(f"license_ready: {license_target}")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--destination", type=Path, required=True)
    parser.add_argument("--charset-source", type=Path, required=True)
    parser.add_argument("--force", action="store_true")
    parser.add_argument("--verify-only", action="store_true")
    parser.add_argument("--whisper-model")
    parser.add_argument("--whisper-revision")
    parser.add_argument("--hcaptcha-manifest", type=Path)
    parser.add_argument("--hcaptcha-destination", type=Path)
    parser.add_argument("--hcaptcha-license", type=Path)
    args = parser.parse_args()

    provision_ocr(
        args.manifest,
        args.destination,
        args.charset_source,
        force=args.force,
        verify_only=args.verify_only,
    )
    if args.whisper_model:
        if not args.whisper_revision:
            raise RuntimeError("--whisper-revision é obrigatório com --whisper-model.")
        provision_whisper(args.whisper_model, args.whisper_revision)
    if args.hcaptcha_manifest:
        if not args.hcaptcha_destination:
            raise RuntimeError("--hcaptcha-destination é obrigatório com --hcaptcha-manifest.")
        if not args.hcaptcha_license:
            raise RuntimeError("--hcaptcha-license é obrigatório com --hcaptcha-manifest.")
        provision_hcaptcha(
            args.hcaptcha_manifest,
            args.hcaptcha_destination,
            args.hcaptcha_license,
            force=args.force,
            verify_only=args.verify_only,
        )


if __name__ == "__main__":
    main()
