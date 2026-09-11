# scripts

Utility scripts for the PoMemeVideo project. Run from the repository root unless otherwise noted.

## Prerequisites

- Python 3.10+
- `pip install azure-storage-blob azure-data-tables mutagen` (for storage scripts)
- Docker Desktop running (for Azurite)
- FFmpeg on PATH (for video rendering — installed by `setup.ps1`)

---

## setup.ps1 ⭐ one-command bootstrap

**Purpose:** The single bootstrap entrypoint. Installs prerequisites via winget, clears ports
7000/5001, starts Azurite using docker compose, validates local mock-key readiness, downloads and
seeds the meme sound library, clones the agent tooling, and checks `az login` status.

**Usage:**
```powershell
# Full setup
pwsh -File scripts/setup.ps1

# Skip package installation and run only project bootstrap
pwsh -File scripts/setup.ps1 -SkipWinget
```

---

## check-azurite.py

**Purpose:** Verifies that the local Azurite Docker container is running and all three storage endpoints (Blob, Queue, Table) are reachable.

**Usage:**
```bash
python scripts/check-azurite.py
```

---

## download-meme-sounds.py

**Purpose:** Downloads the initial set of curated meme audio clips from public sources into `scripts/meme-sounds/`.

**Usage:**
```bash
python scripts/download-meme-sounds.py
```

---

## seed-meme-sounds.py

**Purpose:** Seeds Blob Storage and the SoundAssets Table with meme sound metadata. Targets Azurite
by default; accepts `--connection-string` for real Azure Storage.

**Usage:**
```bash
# Local Azurite (default)
python scripts/seed-meme-sounds.py

# Real Azure Storage
python scripts/seed-meme-sounds.py --connection-string "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net"
```

The BlobUrl stored in each table row is automatically computed from the connection string (Azurite
format vs. `https://<account>.blob.core.windows.net/…` for real Azure). Re-runs are idempotent.

---

## meme-sounds/

Directory containing the raw `.mp3` audio files and `sounds-metadata.json` used for seeding. Only
the metadata is committed — the audio is gitignored and fetched by `download-meme-sounds.py`.

---

## Deploying sounds to real Azure Storage

There is no separate deploy script. `seed-meme-sounds.py --connection-string "<real azure>"`
uploads the blobs and writes the table rows against whatever account the connection string names,
and computes the right `BlobUrl` form for it. Re-runs are idempotent.
