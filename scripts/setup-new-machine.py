#!/usr/bin/env python3
"""
PoMemeVideo — New Machine Setup
================================
Run once when bringing the source code to a new development machine.

Steps performed (all skippable via flags):
  1. Check Python version >= 3.10
  2. Install required Python packages
  3. Check FFmpeg is on PATH (print install instructions if missing)
  4. Check Docker is running and Azurite container is reachable
  5. Download browser LLM ONNX models (from HuggingFace via model-manifest.json)
  6. Download meme sound MP3s (if scripts/meme-sounds/ not already populated)
  7. Seed Azurite (or real Azure Storage) with meme sounds

Usage:
    python scripts/setup-new-machine.py [options]

Options:
    --skip-models          Skip downloading ONNX browser-LLM models
    --skip-sounds          Skip downloading meme sound MP3 files
    --skip-seed            Skip seeding storage (blobs + table rows)
    --connection-string CS  Storage connection string
                            Default: UseDevelopmentStorage=true (Azurite)
    --hf-token TOKEN        HuggingFace token for gated model downloads
                            (also reads HF_TOKEN env var)
"""

from __future__ import annotations

import argparse
import importlib.util
import io
import os
import shutil
import subprocess
import sys
from pathlib import Path

# ── ensure UTF-8 on Windows terminals ─────────────────────────────────────────
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

SCRIPTS_DIR = Path(__file__).parent
REPO_ROOT   = SCRIPTS_DIR.parent

REQUIRED_PACKAGES = [
    "azure-data-tables",
    "azure-storage-blob",
    "mutagen",
]

# ── Helpers ────────────────────────────────────────────────────────────────────

def banner(text: str) -> None:
    bar = "─" * 60
    print(f"\n{bar}\n  {text}\n{bar}")


def ok(msg: str)   -> None: print(f"  [OK]  {msg}")
def warn(msg: str) -> None: print(f"  [!!]  {msg}")
def fail(msg: str) -> None: print(f"  [XX]  {msg}")
def info(msg: str) -> None: print(f"        {msg}")


def run(*args: str, capture: bool = True) -> "subprocess.CompletedProcess[str]":
    return subprocess.run(
        args,
        capture_output=capture,
        text=True,
        encoding="utf-8",
        errors="replace",
    )


# ── Step 1 — Python version ────────────────────────────────────────────────────

def check_python() -> bool:
    banner("Step 1 — Python version")
    major, minor = sys.version_info[:2]
    if (major, minor) >= (3, 10):
        ok(f"Python {major}.{minor} ✓")
        return True
    fail(f"Python {major}.{minor} is too old — need 3.10+")
    info("Download from https://www.python.org/downloads/")
    return False


# ── Step 2 — Python packages ───────────────────────────────────────────────────

def _package_installed(name: str) -> bool:
    dist_name = name.replace("-", "_").split("[")[0].lower()
    spec = importlib.util.find_spec(dist_name)
    return spec is not None


def install_packages() -> bool:
    banner("Step 2 — Python packages")
    missing = [p for p in REQUIRED_PACKAGES if not _package_installed(p)]
    if not missing:
        ok("All required packages already installed.")
        return True

    info(f"Installing: {', '.join(missing)}")
    result = run(sys.executable, "-m", "pip", "install", "--quiet", *missing)
    if result.returncode != 0:
        fail("pip install failed:")
        info(result.stderr.strip())
        return False

    ok(f"Installed {len(missing)} package(s).")
    return True


# ── Step 3 — FFmpeg ───────────────────────────────────────────────────────────

FFMPEG_INSTALL = {
    "win32":  "winget install --id Gyan.FFmpeg  OR  choco install ffmpeg",
    "darwin": "brew install ffmpeg",
    "linux":  "sudo apt install ffmpeg  OR  sudo dnf install ffmpeg",
}


def check_ffmpeg() -> bool:
    banner("Step 3 — FFmpeg")
    path = shutil.which("ffmpeg")
    if path:
        result = run("ffmpeg", "-version")
        version_line = result.stdout.splitlines()[0] if result.stdout else "unknown"
        ok(f"Found: {path}")
        info(version_line)
        return True

    fail("ffmpeg not found in PATH")
    platform = sys.platform
    hint = FFMPEG_INSTALL.get(platform) or FFMPEG_INSTALL["linux"]
    info(f"Install with:  {hint}")
    info("After installing, restart your terminal before running the server.")
    # Non-fatal — app can still be set up; render will fail until fixed
    return False


# ── Step 4 — Docker / Azurite ─────────────────────────────────────────────────

def check_azurite() -> bool:
    banner("Step 4 — Docker & Azurite")

    if not shutil.which("docker"):
        fail("docker not found in PATH")
        info("Install Docker Desktop from https://www.docker.com/products/docker-desktop/")
        return False

    result = run("docker", "info")
    if result.returncode != 0:
        fail("Docker daemon is not running — start Docker Desktop first.")
        return False

    ok("Docker is running.")

    # Check for existing Azurite container
    result = run("docker", "ps", "--filter", "name=azurite", "--format", "{{.Names}}")
    containers = result.stdout.strip().splitlines()
    if containers:
        ok(f"Azurite container running: {', '.join(containers)}")
        return True

    # No running container — offer to start one
    warn("No running Azurite container found.")
    info("Starting a new Azurite container (polinks-azurite)...")
    result = run(
        "docker", "run", "-d",
        "--name", "polinks-azurite",
        "-p", "10000:10000",
        "-p", "10001:10001",
        "-p", "10002:10002",
        "-v", "polinks_azurite_data:/data",
        "mcr.microsoft.com/azure-storage/azurite",
        "azurite",
        "--blobHost",  "0.0.0.0",
        "--queueHost", "0.0.0.0",
        "--tableHost", "0.0.0.0",
        "--location",  "/data",
        "--debug",     "/data/debug.log",
        "--skipApiVersionCheck",
        capture=False,
    )
    return result.returncode == 0


# ── Step 5 — Browser LLM models ───────────────────────────────────────────────

def download_models(hf_token: str | None) -> bool:
    banner("Step 5 — Browser LLM models (ONNX)")

    script = SCRIPTS_DIR / "download-models.py"
    if not script.exists():
        fail(f"Script not found: {script}")
        return False

    env = os.environ.copy()
    if hf_token:
        env["HF_TOKEN"] = hf_token

    result = subprocess.run(
        [sys.executable, str(script)],
        env=env,
        cwd=str(REPO_ROOT),
    )
    return result.returncode == 0


# ── Step 6 — Meme sound MP3s ──────────────────────────────────────────────────

def download_sounds() -> bool:
    banner("Step 6 — Meme sound MP3s")

    meta_file = SCRIPTS_DIR / "meme-sounds" / "sounds-metadata.json"

    if meta_file.exists():
        import json
        with meta_file.open(encoding="utf-8") as f:
            data = json.load(f)
        count = len(data.get("sounds", []))
        mp3_count = len(list((SCRIPTS_DIR / "meme-sounds").glob("*.mp3")))
        if mp3_count >= count * 0.9:   # 90%+ present = skip
            ok(f"{mp3_count}/{count} sound files already present — skipping download.")
            return True

    for script_name in ("download-meme-sounds.py", "download-more-meme-sounds.py"):
        script = SCRIPTS_DIR / script_name
        if not script.exists():
            warn(f"Script not found, skipping: {script_name}")
            continue
        info(f"Running {script_name}…")
        result = subprocess.run([sys.executable, str(script)], cwd=str(REPO_ROOT))
        if result.returncode != 0:
            fail(f"{script_name} failed.")
            return False

    return True


# ── Step 7 — Seed storage ─────────────────────────────────────────────────────

def seed_sounds(connection_string: str) -> bool:
    banner("Step 7 — Seed storage (blobs + table rows)")

    script = SCRIPTS_DIR / "seed-meme-sounds.py"
    if not script.exists():
        fail(f"Script not found: {script}")
        return False

    result = subprocess.run(
        [sys.executable, str(script), "--connection-string", connection_string],
        cwd=str(REPO_ROOT),
    )
    return result.returncode == 0


# ── Main ──────────────────────────────────────────────────────────────────────

def main() -> int:
    parser = argparse.ArgumentParser(
        description="PoMemeVideo — new machine setup",
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    parser.add_argument("--skip-models", action="store_true", help="Skip ONNX model download")
    parser.add_argument("--skip-sounds", action="store_true", help="Skip meme sound MP3 download")
    parser.add_argument("--skip-seed",   action="store_true", help="Skip seeding storage")
    parser.add_argument(
        "--connection-string", "-c",
        default="UseDevelopmentStorage=true",
        metavar="CS",
        help="Storage connection string (default: Azurite dev storage)",
    )
    parser.add_argument(
        "--hf-token",
        default=os.getenv("HF_TOKEN"),
        metavar="TOKEN",
        help="HuggingFace token (also reads HF_TOKEN env var)",
    )
    args = parser.parse_args()

    print("\n╔══════════════════════════════════════════════╗")
    print("║     PoMemeVideo — New Machine Setup          ║")
    print("╚══════════════════════════════════════════════╝")

    results: dict[str, bool] = {}

    results["python"]  = check_python()
    if not results["python"]:
        return 1   # can't continue

    results["packages"] = install_packages()

    results["ffmpeg"]   = check_ffmpeg()   # non-fatal
    results["azurite"]  = check_azurite()  # non-fatal — may be using real Azure

    if not args.skip_models:
        results["models"] = download_models(args.hf_token)
    else:
        info("\n[skip] ONNX model download (--skip-models)")
        results["models"] = True

    if not args.skip_sounds:
        results["sounds"] = download_sounds()
    else:
        info("\n[skip] Meme sound download (--skip-sounds)")
        results["sounds"] = True

    if not args.skip_seed:
        results["seed"] = seed_sounds(args.connection_string)
    else:
        info("\n[skip] Storage seeding (--skip-seed)")
        results["seed"] = True

    # ── Summary ────────────────────────────────────────────────────────────────
    bar = "─" * 60
    print(f"\n{bar}\n  Setup Summary\n{bar}")
    all_ok = True
    for step, passed in results.items():
        symbol = "✓" if passed else "✗"
        print(f"  {symbol}  {step}")
        if not passed:
            all_ok = False

    print(bar)
    if all_ok:
        print("\n  All steps completed successfully.")
        print("  Run:  dotnet run --project src/PoMemeVideo.Api/PoMemeVideo.Api.csproj\n")
    else:
        print("\n  Some steps need attention — check the output above.\n")
    return 0 if all_ok else 1


if __name__ == "__main__":
    sys.exit(main())
