"""
Meme Sound Seeder
Uploads MP3 files to Azurite Blob Storage and inserts rows into the SoundAssets
Azure Table Storage table — matching exactly what SoundAssetTableRepository expects.

Usage:
    python tools/seed-meme-sounds.py

Reads:  tools/meme-sounds/sounds-metadata.json
Reads:  tools/meme-sounds/*.mp3
Writes: Azurite Blob container  "sounds"
Writes: Azurite Table           "SoundAssets"  (PartitionKey = "library")
"""

import io
import json
import os
import sys
import uuid
from pathlib import Path

# Ensure UTF-8 output on Windows terminals
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

from azure.data.tables import TableServiceClient, TableEntity
from azure.storage.blob import BlobServiceClient, ContentSettings
from mutagen.mp3 import MP3
from mutagen.wave import WAVE

CONN_STR   = "UseDevelopmentStorage=true"
CONTAINER  = "sounds"
TABLE_NAME = "SoundAssets"
PARTITION  = "library"

SOUNDS_DIR = Path(__file__).parent / "meme-sounds"
META_FILE  = SOUNDS_DIR / "sounds-metadata.json"


def get_duration_ms(path: Path) -> int:
    try:
        if path.suffix.lower() == ".mp3":
            audio = MP3(path)
        else:
            audio = WAVE(path)
        return int(audio.info.length * 1000)
    except Exception:
        return 0


def blob_url(container: str, blob_name: str) -> str:
    # Azurite default URL format
    return f"http://127.0.0.1:10000/devstoreaccount1/{container}/{blob_name}"


def main() -> None:
    if not META_FILE.exists():
        print(f"✗ Metadata file not found: {META_FILE}")
        print("  Run tools/download-meme-sounds.py first.")
        sys.exit(1)

    with open(META_FILE, encoding="utf-8") as f:
        meta = json.load(f)

    sounds: list[dict] = meta["sounds"]
    print(f"Found {len(sounds)} sounds in metadata.\n")

    # ── Blob Storage ──────────────────────────────────────────────────────────
    blob_svc = BlobServiceClient.from_connection_string(CONN_STR)
    container_client = blob_svc.get_container_client(CONTAINER)
    try:
        container_client.create_container()
        print(f"✓ Created blob container '{CONTAINER}'")
    except Exception:
        print(f"  Blob container '{CONTAINER}' already exists.")

    # ── Table Storage ─────────────────────────────────────────────────────────
    table_svc = TableServiceClient.from_connection_string(CONN_STR)
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

        # Insert table row
        tags_str = ",".join(entry.get("actionVectorTags", []))
        entity: TableEntity = {
            "PartitionKey":    PARTITION,
            "RowKey":          sound_id,
            "DisplayName":     entry["displayName"],
            "DurationMs":      duration_ms,
            "Tags":            tags_str,
            "BlobUrl":         blob_url(CONTAINER, blob_name),
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
    print(f"\nAzurite blob container : {CONTAINER}")
    print(f"Azurite table          : {TABLE_NAME}  (partition='{PARTITION}')")


if __name__ == "__main__":
    main()
