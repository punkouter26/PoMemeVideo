# SCRIPTS

Utility scripts for the PoMemeVideo project. Run from the repository root unless otherwise noted.

## Prerequisites

- Python 3.10+
- `pip install azure-storage-blob azure-data-tables mutagen` (for storage scripts)
- Docker Desktop running (for Azurite)
- FFmpeg on PATH (for video rendering — see setup-new-machine.py)

---

## setup.ps1 ⭐ one-command bootstrap

**Purpose:** Windows-first bootstrap entrypoint that installs prerequisites via winget,
starts Azurite using docker compose, validates local mock-key readiness, and then
executes the Python bootstrap pipeline.

**Usage:**
```powershell
# Full setup
pwsh -File SCRIPTS/setup.ps1

# Skip package installation and run only project bootstrap
pwsh -File SCRIPTS/setup.ps1 -SkipWinget
```

---

## setup-new-machine.py ⭐ start here on a new machine

**Purpose:** One-shot bootstrap for a freshly cloned repository. Checks Python, installs Python
dependencies, checks FFmpeg, starts Azurite if needed, downloads ONNX browser-LLM models, downloads
meme sounds, and seeds storage — all in one go.

**Usage:**
```bash
# Full setup (local Azurite)
python SCRIPTS/setup-new-machine.py

# Skip individual steps
python SCRIPTS/setup-new-machine.py --skip-models --skip-sounds

# Target real Azure Storage instead of Azurite
python SCRIPTS/setup-new-machine.py --connection-string "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net"

# Private HuggingFace models
python SCRIPTS/setup-new-machine.py --hf-token hf_xxxx
# or: set HF_TOKEN=hf_xxxx before running
```

---

## check-azurite.py

**Purpose:** Verifies that the local Azurite Docker container is running and all three storage endpoints (Blob, Queue, Table) are reachable.

**Usage:**
```bash
python SCRIPTS/check-azurite.py
```

---

## download-meme-sounds.py

**Purpose:** Downloads the initial set of curated meme audio clips from public sources into `SCRIPTS/meme-sounds/`.

**Usage:**
```bash
python SCRIPTS/download-meme-sounds.py
```

---

## download-more-meme-sounds.py

**Purpose:** Downloads an extended set of meme audio clips to supplement the base library.

**Usage:**
```bash
python SCRIPTS/download-more-meme-sounds.py
```

---

## download-models.py

**Purpose:** Downloads ONNX/embedding models listed in `model-manifest.json` into the local `models/` directory used by the BrowserLLM feature.

**Usage:**
```bash
python SCRIPTS/download-models.py
```

---

## seed-meme-sounds.py

**Purpose:** Seeds Blob Storage and the SoundAssets Table with meme sound metadata. Targets Azurite
by default; accepts `--connection-string` for real Azure Storage.

**Usage:**
```bash
# Local Azurite (default)
python SCRIPTS/seed-meme-sounds.py

# Real Azure Storage
python SCRIPTS/seed-meme-sounds.py --connection-string "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net"
```

The BlobUrl stored in each table row is automatically computed from the connection string (Azurite
format vs. `https://<account>.blob.core.windows.net/…` for real Azure). Re-runs are idempotent.

---

## deploy-meme-sounds.py

**Purpose:** Deploys meme sound files from `SCRIPTS/meme-sounds/` to Azure Blob Storage in the target resource group. Requires `az login` and correct subscription context.

**Usage:**
```bash
python SCRIPTS/deploy-meme-sounds.py
```

---

## model-manifest.json

Configuration file listing model names, download URLs, and target paths used by `download-models.py`.

---

## meme-sounds/

Directory containing the raw `.mp3` audio files and `sounds-metadata.json` used for seeding.
