"""Gera/atualiza hcaptcha-models.manifest.json com modelos ResNet fixados.

Baixa os classificadores binários do model hub do hcaptcha-challenger (release
`model`), valida o contrato ONNX (entrada float32 [1,3,64,64], saída float32
[1,2], índice positivo 0) e grava SHA-256/tamanho no manifesto. Uso:

    python tools/Update-HcaptchaModelManifest.py [--verify-only]

O provisionamento em si é feito por tools/Get-CaptchaModels.ps1 ou por
services/captcha-solver/provision_models.py; este script só (re)gera o
manifesto versionado. Não executa downloads em --verify-only.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
import urllib.request
from pathlib import Path

import yaml

REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
MANIFEST_PATH = (
    REPOSITORY_ROOT
    / "src"
    / "RpaFlow.Playwright"
    / "V2"
    / "Captcha"
    / "hcaptcha-models.manifest.json"
)
RELEASE_DOWNLOAD = (
    "https://github.com/QIN2DIM/hcaptcha-challenger/releases/download/model"
)
OBJECTS_URLS = [
    "https://raw.githubusercontent.com/QIN2DIM/hcaptcha-challenger/main/archive/src/objects2022.yaml",
    "https://raw.githubusercontent.com/QIN2DIM/hcaptcha-challenger/main/archive/src/objects2023.yaml",
]
USER_AGENT = "RpaBlockly-hcaptcha-manifest/1"

# Conjunto curado de labels binários provisionados por padrão. Cada entrada:
# stem do modelo no model hub -> aliases adicionais por idioma. Os aliases
# oficiais (en/zh) vêm dos objects*.yaml do upstream; aqui ficam apenas
# complementos editoriais (pt-BR e variantes en ausentes no upstream).
CURATED: dict[str, dict[str, list[str]]] = {
    "airplane2310": {"en": ["aeroplane"], "pt": ["avião"]},
    "bicycle2309": {"pt": ["bicicleta"]},
    "bird2309": {"pt": ["pássaro", "ave"]},
    "boat2310": {"pt": ["barco"]},
    "car2309": {"pt": ["carro"]},
    "cat2311": {"pt": ["gato"]},
    "dog2312": {"pt": ["cachorro", "cão"]},
    "elephant2309": {"pt": ["elefante"]},
    "motorcycle2309": {"en": ["motorbike"], "pt": ["motocicleta", "moto"]},
    "train2309": {"pt": ["trem"]},
    "truck2310": {"pt": ["caminhão"]},
    "mountain2309": {"pt": ["montanha"]},
    "robot2312": {"pt": ["robô"]},
    "panda2309": {"pt": ["panda"]},
    "fox2310": {"pt": ["raposa"]},
    "owl2309": {"pt": ["coruja"]},
    "goose2309": {"pt": ["ganso"]},
    "helicopter2310": {"pt": ["helicóptero"]},
    "castle2309": {"pt": ["castelo"]},
    "trees2309": {"pt": ["árvores", "árvore"]},
    "sheep2309": {"pt": ["ovelha"]},
    "snowman2311": {"pt": ["boneco de neve"]},
    "motor_vehicle2309": {"pt": ["veículo motorizado", "veículo a motor"]},
    "flower": {"pt": ["flor"]},
    "bridge": {"pt": ["ponte"]},
    "parrot": {"pt": ["papagaio"]},
    "seaplane": {"pt": ["hidroavião"]},
    "tractor": {"pt": ["trator"]},
    "desert": {"pt": ["deserto"]},
    "forest": {"pt": ["floresta"]},
    "beach": {"pt": ["praia"]},
    "butterfly": {"pt": ["borboleta"]},
    "ocean": {"pt": ["oceano", "mar"]},
    "hummingbird": {"pt": ["beija-flor"]},
}


def download(url: str) -> bytes:
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    with urllib.request.urlopen(request, timeout=120) as response:
        return response.read()


def sha256_upper(payload: bytes) -> str:
    return hashlib.sha256(payload).hexdigest().upper()


def load_upstream_aliases() -> dict[str, dict[str, list[str]]]:
    aliases: dict[str, dict[str, list[str]]] = {}
    for url in OBJECTS_URLS:
        data = yaml.safe_load(download(url).decode("utf-8"))
        for stem, languages in (data.get("label_alias") or {}).items():
            slot = aliases.setdefault(stem, {})
            for language, values in (languages or {}).items():
                if language not in {"en", "zh"} or not values:
                    continue
                merged = slot.setdefault(language, [])
                for value in values:
                    cleaned = " ".join(str(value).split()).strip()
                    if cleaned and cleaned.lower() not in {
                        item.lower() for item in merged
                    }:
                        merged.append(cleaned)
    return aliases


def validate_onnx_contract(stem: str, payload: bytes) -> None:
    import onnxruntime as ort

    session = ort.InferenceSession(payload, providers=["CPUExecutionProvider"])
    inputs = session.get_inputs()
    outputs = session.get_outputs()
    if (
        len(inputs) != 1
        or inputs[0].type != "tensor(float)"
        or inputs[0].shape != [1, 3, 64, 64]
        or len(outputs) != 1
        or outputs[0].type != "tensor(float)"
        or outputs[0].shape != [1, 2]
    ):
        raise RuntimeError(
            f"model_mismatch: {stem} não respeita o contrato "
            "float32[1,3,64,64] -> float32[1,2]."
        )


def build_manifest() -> dict:
    upstream = load_upstream_aliases()
    models = []
    for stem in sorted(CURATED):
        url = f"{RELEASE_DOWNLOAD}/{stem}.onnx"
        print(f"baixando {stem}...", flush=True)
        payload = download(url)
        validate_onnx_contract(stem, payload)
        aliases: dict[str, list[str]] = {}
        for language, values in (upstream.get(stem) or {}).items():
            aliases[language] = list(values)
        for language, extras in CURATED[stem].items():
            merged = aliases.setdefault(language, [])
            for value in extras:
                if value.lower() not in {item.lower() for item in merged}:
                    merged.append(value)
        if not any(aliases.values()):
            raise RuntimeError(f"model_missing: {stem} ficou sem alias.")
        models.append(
            {
                "id": stem,
                "publishedFile": f"{stem}.onnx",
                "artifactUrl": url,
                "modelSha256": sha256_upper(payload),
                "modelSizeBytes": len(payload),
                "aliases": aliases,
            }
        )
    return {
        "schemaVersion": 1,
        "suite": "hcaptcha-resnet-binary",
        "source": {
            "repository": "https://github.com/QIN2DIM/hcaptcha-challenger",
            "releaseTag": "model",
            "objects": [
                "archive/src/objects2022.yaml",
                "archive/src/objects2023.yaml",
            ],
            "license": {
                "spdx": "CC0-1.0",
                "source": "https://github.com/QIN2DIM/hcaptcha-challenger/blob/main/LICENSE",
                "note": (
                    "O autor do upstream autorizou o uso dos modelos ONNX como "
                    "CC0 (registro do mantenedor RpaBlockly). O código-fonte do "
                    "upstream não é incorporado; os arquivos ONNX são dados "
                    "carregados em runtime e provisionados pelo usuário."
                ),
            },
        },
        "defaults": {
            "kind": "resnet-binary",
            "tensors": {
                "inputType": "float32",
                "inputShape": [1, 3, 64, 64],
                "outputType": "float32",
                "outputShape": [1, 2],
            },
            "preprocessing": {
                "resize": [64, 64],
                "scale": "1/255",
                "colorOrder": "RGB",
                "denoiseWhenHeight": 144,
                "positiveIndex": 0,
            },
        },
        "models": models,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--verify-only", action="store_true")
    args = parser.parse_args()

    if args.verify_only:
        manifest = json.loads(MANIFEST_PATH.read_text("utf-8"))
        ids = [model["id"] for model in manifest["models"]]
        if manifest["schemaVersion"] != 1 or len(ids) != len(set(ids)):
            raise RuntimeError("model_mismatch: manifesto hCaptcha inválido.")
        print(f"manifest_ok: {len(ids)} modelos fixados em {MANIFEST_PATH}")
        return 0

    manifest = build_manifest()
    MANIFEST_PATH.write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(f"manifest_ready: {MANIFEST_PATH} ({len(manifest['models'])} modelos)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
