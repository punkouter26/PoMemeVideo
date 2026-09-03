"""
Meme Sound Seeder
Uploads MP3 files to Blob Storage and inserts rows into the SoundAssets
Azure Table Storage table — matching exactly what SoundAssetTableRepository expects.

Targets Azurite (local dev) by default; pass --connection-string to target real Azure.

Usage:
    # Local Azurite (default)
    python scripts/seed-meme-sounds.py

    # Real Azure Storage
    python scripts/seed-meme-sounds.py --connection-string "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net"

Reads:  scripts/meme-sounds/sounds-metadata.json
Reads:  scripts/meme-sounds/*.mp3
Writes: Blob container  "sounds"
Writes: Table           "SoundAssets"  (PartitionKey = "library")
"""

from __future__ import annotations

import argparse
import io
import json
import sys
import uuid
from pathlib import Path

# Ensure UTF-8 output on Windows terminals
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

from azure.data.tables import TableServiceClient, TableEntity
from azure.storage.blob import BlobServiceClient, ContentSettings
from mutagen.mp3 import MP3
from mutagen.wave import WAVE

CONTAINER  = "sounds"
TABLE_NAME = "SoundAssets"
PARTITION  = "library"

SOUNDS_DIR = Path(__file__).parent / "meme-sounds"
META_FILE  = SOUNDS_DIR / "sounds-metadata.json"

_AZURITE_CS = "UseDevelopmentStorage=true"


def get_duration_ms(path: Path) -> int:
    try:
        if path.suffix.lower() == ".mp3":
            audio = MP3(path)
        else:
            audio = WAVE(path)
        return int(audio.info.length * 1000)
    except Exception:
        return 0


def _parse_cs_key(connection_string: str, key: str) -> str:
    """Extract a named segment from a storage connection string."""
    for part in connection_string.split(";"):
        if part.startswith(f"{key}="):
            return part[len(key) + 1:]
    return ""


def resolve_blob_base_url(connection_string: str, container: str) -> str:
    """
    Derive the public blob base URL from a connection string.

    Azurite:  http://127.0.0.1:10000/devstoreaccount1/<container>
    Azure:    https://<account>.blob.core.windows.net/<container>
    """
    if connection_string.strip() == _AZURITE_CS:
        return f"http://127.0.0.1:10000/devstoreaccount1/{container}"

    account = _parse_cs_key(connection_string, "AccountName")
    suffix  = _parse_cs_key(connection_string, "EndpointSuffix") or "core.windows.net"
    protocol = _parse_cs_key(connection_string, "DefaultEndpointsProtocol") or "https"

    if not account:
        raise ValueError("Could not parse AccountName from the connection string.")

    return f"{protocol}://{account}.blob.{suffix}/{container}"


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Seed Azurite or Azure Storage with meme sound assets.",
    )
    parser.add_argument(
        "--connection-string", "-c",
        default=_AZURITE_CS,
        metavar="CS",
        help=(
            "Storage connection string. "
            "Defaults to UseDevelopmentStorage=true (Azurite). "
            "Pass a real Azure connection string to target cloud storage."
        ),
    )
    args = parser.parse_args()
    conn_str = args.connection_string

    is_azurite = conn_str.strip() == _AZURITE_CS
    target_label = "Azurite (local dev)" if is_azurite else "Azure Storage"
    print(f"Target: {target_label}\n")

    if not META_FILE.exists():
        print(f"✗ Metadata file not found: {META_FILE}")
        print("  Run scripts/download-meme-sounds.py first.")
        sys.exit(1)

    with open(META_FILE, encoding="utf-8") as f:
        meta = json.load(f)

    sounds: list[dict] = meta["sounds"]
    print(f"Found {len(sounds)} sounds in metadata.\n")

    blob_base = resolve_blob_base_url(conn_str, CONTAINER)

    # ── Blob Storage ──────────────────────────────────────────────────────────
    blob_svc = BlobServiceClient.from_connection_string(conn_str)
    container_client = blob_svc.get_container_client(CONTAINER)
    try:
        container_client.create_container()
        print(f"✓ Created blob container '{CONTAINER}'")
    except Exception:
        print(f"  Blob container '{CONTAINER}' already exists.")

    # ── Table Storage ─────────────────────────────────────────────────────────
    table_svc = TableServiceClient.from_connection_string(conn_str)
    try:
        table_svc.create_table(TABLE_NAME)
        print(f"✓ Created table '{TABLE_NAME}'")
    except Exception:
        print(f"  Table '{TABLE_NAME}' already exists.")

    table_client = table_svc.get_table_client(TABLE_NAME)

    # ── Seed each sound ───────────────────────────────────────────────────────
    uploaded   = 0
    skipped    = 0
    failed     = 0

    print()
    for entry in sounds:
        filename = entry["filename"]
        local    = SOUNDS_DIR / filename
        if not local.exists():
            print(f"  [MISS] File missing: {filename}")
            failed += 1
            continue

        blob_name    = filename          # e.g. vine-boom-sound-70972.mp3
        sound_id_str = entry.get("id", "")
        # Derive stable GUID from slug so re-runs are idempotent
        sound_id = str(uuid.uuid5(uuid.NAMESPACE_DNS, sound_id_str or filename))

        # Check if row already exists
        try:
            table_client.get_entity(PARTITION, sound_id)
            print(f"  [SKIP] already seeded: {entry['displayName'][:50]}")
            skipped += 1
            continue
        except Exception:
            pass  # not found — proceed

        # Upload blob
        blob_client = container_client.get_blob_client(blob_name)
        try:
            if not blob_client.exists():
                with open(local, "rb") as f:
                    blob_client.upload_blob(
                        f,
                        content_settings=ContentSettings(content_type="audio/mpeg"),
                        overwrite=False,
                    )
        except Exception as e:
            print(f"  [FAIL] Blob upload failed for {filename}: {e}")
            failed += 1
            continue

        # Measure duration
        duration_ms = get_duration_ms(local)

        # Insert table row — BlobUrl reflects actual storage endpoint
        tags_str = ",".join(entry.get("actionVectorTags", []))
        entity: TableEntity = {
            "PartitionKey":    PARTITION,
            "RowKey":          sound_id,
            "DisplayName":     entry["displayName"],
            "DurationMs":      duration_ms,
            "Tags":            tags_str,
            "BlobUrl":         f"{blob_base}/{blob_name}",
            "EmbeddingVector": "",     # populated later by embedding pipeline
            "UseCase":         entry.get("useCase", ""),
            "Origin":          entry.get("origin", "myinstants.com"),
        }
        try:
            table_client.upsert_entity(entity)
        except Exception as e:
            print(f"  [FAIL] Table insert failed for {entry['displayName']}: {e}")
            failed += 1
            continue

        print(f"  [OK]  [{uploaded+1:>3}] {entry['displayName'][:55]:<55}  {duration_ms}ms")
        uploaded += 1

    print(f"\n{'─'*60}")
    print(f"  Uploaded : {uploaded}")
    print(f"  Skipped  : {skipped} (already seeded)")
    print(f"  Failed   : {failed}")
    print(f"\n  Blob container : {CONTAINER}  ({blob_base})")
    print(f"  Table          : {TABLE_NAME}  (partition='{PARTITION}')")


if __name__ == "__main__":
    main()
