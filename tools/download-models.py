#!/usr/bin/env python3

from __future__ import annotations

import argparse
import json
import os
import shutil
import sys
import tempfile
import urllib.parse
import urllib.request
from pathlib import Path


def load_manifest(manifest_path: Path) -> list[dict]:
    with manifest_path.open("r", encoding="utf-8") as handle:
        payload = json.load(handle)

    models = payload.get("models")
    if not isinstance(models, list):
        raise ValueError("tools/model-manifest.json must contain a 'models' array.")

    return models


def build_request(url: str) -> urllib.request.Request:
    headers = {"User-Agent": "PoMemeVideo model bootstrapper/1.0"}
    token = os.getenv("HF_TOKEN")
    if token:
        headers["Authorization"] = f"Bearer {token}"
    return urllib.request.Request(url, headers=headers)


def download_file(url: str, destination: Path, force: bool) -> None:
    if destination.exists() and not force:
        print(f"skip  {destination}")
        return

    destination.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(delete=False, dir=destination.parent) as temp_handle:
        temp_path = Path(temp_handle.name)

    try:
        with urllib.request.urlopen(build_request(url)) as response:
            with temp_path.open("wb") as output_handle:
                shutil.copyfileobj(response, output_handle)
        temp_path.replace(destination)
        print(f"saved {destination}")
    except Exception:
        temp_path.unlink(missing_ok=True)
        raise


def resolve_models(models: list[dict], requested_ids: list[str]) -> list[dict]:
    if requested_ids:
        by_id = {model["id"]: model for model in models}
        missing = [model_id for model_id in requested_ids if model_id not in by_id]
        if missing:
            raise ValueError(f"Unknown model id(s): {', '.join(missing)}")
        return [by_id[model_id] for model_id in requested_ids]

    return [model for model in models if model.get("recommended")]


def list_models(models: list[dict]) -> int:
    for model in models:
        status = "manual" if model.get("manual") else "downloadable"
        recommendation = " recommended" if model.get("recommended") else ""
        print(f"{model['id']}: {status}{recommendation}")
        if model.get("notes"):
            print(f"  {model['notes']}")
    return 0


def download_model(model: dict, target_root: Path, force: bool) -> int:
    model_id = model["id"]
    if model.get("manual"):
        print(f"skip  {model_id}: {model['notes']}")
        return 0

    repo = model["repo"]
    revision = model.get("revision", "main")
    files = model.get("files", [])
    if not isinstance(files, list) or not files:
        raise ValueError(f"Model '{model_id}' has no files configured.")

    print(f"downloading {model_id} from {repo}@{revision}")
    for relative_path in files:
        encoded_path = urllib.parse.quote(relative_path, safe="/")
        url = f"https://huggingface.co/{repo}/resolve/{revision}/{encoded_path}?download=1"
        destination = target_root / model_id / relative_path
        download_file(url, destination, force)

    return 0


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Download BrowserLLM model assets into the local MODEL directory."
    )
    parser.add_argument(
        "model_ids",
        nargs="*",
        help="Model ids to download. Defaults to the recommended public models.",
    )
    parser.add_argument(
        "--list",
        action="store_true",
        help="List supported model ids and exit.",
    )
    parser.add_argument(
        "--force",
        action="store_true",
        help="Re-download files even if they already exist locally.",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()

    repo_root = Path(__file__).resolve().parent.parent
    manifest_path = Path(__file__).resolve().with_name("model-manifest.json")
    models_root = repo_root / "MODEL"

    try:
        models = load_manifest(manifest_path)
        if args.list:
            return list_models(models)

        selected_models = resolve_models(models, args.model_ids)
        if not selected_models:
            raise ValueError("No models selected. Use --list to inspect the manifest.")

        models_root.mkdir(parents=True, exist_ok=True)
        for model in selected_models:
            download_model(model, models_root, args.force)
    except Exception as error:
        print(f"error: {error}", file=sys.stderr)
        return 1

    print(f"models available under {models_root}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())