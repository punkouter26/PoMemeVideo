"""
deploy-meme-sounds.py
=====================
Production deployment tool: uploads meme sound MP3 files to Azure Blob Storage
and upserts rows into Azure Table Storage so the live PoMemeVideo service can
serve the full sound library.

Authentication (pick one, checked in order):
  1. --connection-string  Full Azure Storage connection string
                          (use for quick manual deploys / CI secrets)
  2. --blob-endpoint + --table-endpoint
                          Azure endpoint URLs; uses DefaultAzureCredential
                          (Managed Identity, az login, workload identity, …)
  3. Environment variable AZURE_STORAGE_CONNECTION_STRING
  4. Environment variables AZURE_BLOB_ENDPOINT + AZURE_TABLE_ENDPOINT

Usage examples:
  # Connection string (copy from Azure Portal → Storage Account → Access keys)
  python tools/deploy-meme-sounds.py \\
      --connection-string "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net"

  # Managed identity (e.g. in a GitHub Actions workflow using OIDC)
  python tools/deploy-meme-sounds.py \\
      --blob-endpoint  https://mystorageaccount.blob.core.windows.net \\
      --table-endpoint https://mystorageaccount.table.core.windows.net

  # Dry-run (no writes, just validate files + metadata)
  python tools/deploy-meme-sounds.py --dry-run \\
      --connection-string "..."

  # Re-upload everything even if already present
  python tools/deploy-meme-sounds.py --force \\
      --connection-string "..."

Dependencies (pip install):
  azure-storage-blob
  azure-data-tables
  azure-identity
  mutagen
"""

from __future__ import annotations

import argparse
import io
import json
import os
import sys
import uuid
from pathlib import Path

# ── UTF-8 safe output on Windows ─────────────────────────────────────────────
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding="utf-8", errors="replace")

try:
    from azure.data.tables import TableServiceClient, TableEntity
    from azure.storage.blob import BlobServiceClient, ContentSettings
except ImportError:
    print("✗ Missing Azure SDK. Run:  pip install azure-storage-blob azure-data-tables azure-identity mutagen")
    sys.exit(1)

try:
    from mutagen.mp3 import MP3
    from mutagen.wave import WAVE
except ImportError:
    print("✗ Missing mutagen. Run:  pip install mutagen")
    sys.exit(1)

# ── Constants matching the application layer ──────────────────────────────────
BLOB_CONTAINER = "sounds"
TABLE_NAME     = "SoundAssets"
PARTITION_KEY  = "library"

SOUNDS_DIR = Path(__file__).parent / "meme-sounds"
META_FILE  = SOUNDS_DIR / "sounds-metadata.json"

# ─────────────────────────────────────────────────────────────────────────────

def get_duration_ms(path: Path) -> int:
    try:
        if path.suffix.lower() == ".mp3":
            audio = MP3(str(path))
        elif path.suffix.lower() == ".wav":
            audio = WAVE(str(path))
        else:
            return 0
        return int(audio.info.length * 1000)
    except Exception:
        return 0


def make_sound_id(slug: str, filename: str) -> str:
    """Deterministic UUID from slug so re-runs are idempotent."""
    return str(uuid.uuid5(uuid.NAMESPACE_DNS, slug or filename))


def prod_blob_url(blob_endpoint: str, container: str, blob_name: str) -> str:
    """Return the canonical HTTPS blob URL for a file in production Azure."""
    base = blob_endpoint.rstrip("/")
    return f"{base}/{container}/{blob_name}"


def content_type_for(filename: str) -> str:
    ext = Path(filename).suffix.lower()
    return {"mp3": "audio/mpeg", "wav": "audio/wav", "ogg": "audio/ogg"}.get(ext.lstrip("."), "application/octet-stream")


# ─── Auth helpers ─────────────────────────────────────────────────────────────

def build_clients(args: argparse.Namespace) -> tuple[BlobServiceClient, TableServiceClient, str]:
    """
    Returns (blob_service_client, table_service_client, blob_endpoint_base_url).
    blob_endpoint_base_url is used to construct the public URL stored in the table.
    """
    conn_str = (
        args.connection_string
        or os.environ.get("AZURE_STORAGE_CONNECTION_STRING", "")
    )

    blob_endpoint_arg  = args.blob_endpoint  or os.environ.get("AZURE_BLOB_ENDPOINT", "")
    table_endpoint_arg = args.table_endpoint or os.environ.get("AZURE_TABLE_ENDPOINT", "")

    if conn_str:
        blob_svc  = BlobServiceClient.from_connection_string(conn_str)
        table_svc = TableServiceClient.from_connection_string(conn_str)
        # Detect Azurite / development storage
        if conn_str.strip().lower() == "usedevelopmentstorage=true":
            blob_base = "http://127.0.0.1:10000/devstoreaccount1"
            print(f"  Auth: Azurite (UseDevelopmentStorage=true)")
        else:
            props = dict(part.split("=", 1) for part in conn_str.split(";") if "=" in part)
            account_name = props.get("AccountName", "")
            suffix       = props.get("EndpointSuffix", "core.windows.net")
            blob_base    = f"https://{account_name}.blob.{suffix}"
            print(f"  Auth: connection string  (account={account_name})")
        return blob_svc, table_svc, blob_base

    if blob_endpoint_arg and table_endpoint_arg:
        try:
            from azure.identity import DefaultAzureCredential
        except ImportError:
            print("✗ azure-identity not installed. Run:  pip install azure-identity")
            sys.exit(1)
        cred      = DefaultAzureCredential()
        blob_svc  = BlobServiceClient(blob_endpoint_arg,  credential=cred)
        table_svc = TableServiceClient(table_endpoint_arg, credential=cred)
        print(f"  Auth: DefaultAzureCredential")
        print(f"  Blob endpoint:  {blob_endpoint_arg}")
        print(f"  Table endpoint: {table_endpoint_arg}")
        return blob_svc, table_svc, blob_endpoint_arg.rstrip("/")

    print("✗ No Azure credentials supplied.")
    print("  Provide --connection-string OR (--blob-endpoint + --table-endpoint)")
    print("  or set AZURE_STORAGE_CONNECTION_STRING in the environment.")
    sys.exit(1)


# ─── Core deploy logic ────────────────────────────────────────────────────────

def deploy(args: argparse.Namespace) -> None:
    if not META_FILE.exists():
        print(f"✗ Metadata file not found: {META_FILE}")
        print("  Run tools/download-meme-sounds.py first.")
        sys.exit(1)

    with open(META_FILE, encoding="utf-8") as f:
        meta = json.load(f)

    sounds: list[dict] = meta["sounds"]
    print(f"\nPoMemeVideo — Meme Sound Library Deploy")
    print(f"{'─' * 50}")
    print(f"  Sounds in metadata : {len(sounds)}")
    print(f"  Dry-run            : {args.dry_run}")
    print(f"  Force re-upload    : {args.force}")
    print(f"  Source dir         : {SOUNDS_DIR}")
    print()

    blob_svc, table_svc, blob_base = build_clients(args)
    print()

    # ── Ensure blob container exists ──────────────────────────────────────────
    container_client = blob_svc.get_container_client(BLOB_CONTAINER)
    if not args.dry_run:
        try:
            container_client.create_container(public_access=None)  # private
            print(f"✓ Created blob container  '{BLOB_CONTAINER}'")
        except Exception:
            print(f"  Blob container '{BLOB_CONTAINER}' already exists.")

    # ── Ensure table exists ───────────────────────────────────────────────────
    if not args.dry_run:
        try:
            table_svc.create_table(TABLE_NAME)
            print(f"✓ Created table           '{TABLE_NAME}'")
        except Exception:
            print(f"  Table '{TABLE_NAME}' already exists.")

    table_client = table_svc.get_table_client(TABLE_NAME)

    print()

    # ── Process each sound ────────────────────────────────────────────────────
    uploaded = 0
    skipped  = 0
    failed   = 0
    missing  = 0

    for entry in sounds:
        filename = entry["filename"]
        local    = SOUNDS_DIR / filename

        if not local.exists():
            print(f"  [MISS] {filename}")
            missing += 1
            continue

        slug     = entry.get("id", "")
        sound_id = make_sound_id(slug, filename)
        blob_name = filename

        # Check if row already exists (skip unless --force)
        if not args.force and not args.dry_run:
            try:
                table_client.get_entity(PARTITION_KEY, sound_id)
                skipped += 1
                continue   # silent skip for already-seeded rows
            except Exception:
                pass  # not found → proceed

        display  = entry["displayName"]
        tags     = entry.get("actionVectorTags", [])
        use_case = entry.get("useCase", "")
        clip_pos = entry.get("clipPosition", "any")
        intensity = entry.get("intensity", "medium")
        origin   = entry.get("origin", "myinstants.com")

        label = f"{display[:52]:<52}"
        print(f"  {'[DRY]' if args.dry_run else '[  . ]'} {label}", end="", flush=True)

        if args.dry_run:
            duration_ms = get_duration_ms(local)
            print(f"  {duration_ms}ms  tags={len(tags)}")
            uploaded += 1
            continue

        # Upload blob ──────────────────────────────────────────────────────────
        blob_client = container_client.get_blob_client(blob_name)
        try:
            if args.force or not blob_client.exists():
                with open(local, "rb") as fh:
                    blob_client.upload_blob(
                        fh,
                        content_settings=ContentSettings(content_type=content_type_for(filename)),
                        overwrite=True,
                    )
        except Exception as e:
            print(f"  BLOB FAIL: {e}")
            failed += 1
            continue

        # Measure duration ────────────────────────────────────────────────────
        duration_ms = get_duration_ms(local)

        # Construct production blob URL ────────────────────────────────────────
        url = prod_blob_url(blob_base, BLOB_CONTAINER, blob_name)

        # Build table entity ───────────────────────────────────────────────────
        # Columns match exactly what SoundAssetTableRepository reads:
        #   RowKey        → SoundId (Guid)
        #   DisplayName   → SoundAsset.DisplayName
        #   DurationMs    → SoundAsset.DurationMs
        #   Tags          → SoundAsset.ActionVectorTags (comma-separated)
        #   BlobUrl       → SoundAsset.BlobUrl
        #   EmbeddingVector → SoundAsset.EmbeddingVector (populated later by AI pipeline)
        #   UseCase       → AI director hint (human-readable)
        #   ClipPosition  → AI director hint (cut-point, peak-moment, …)
        #   Intensity     → mixer volume hint (low / medium / high)
        #   Origin        → provenance
        entity: TableEntity = {
            "PartitionKey":    PARTITION_KEY,
            "RowKey":          sound_id,
            "DisplayName":     display,
            "DurationMs":      duration_ms,
            "Tags":            ",".join(tags),
            "BlobUrl":         url,
            "EmbeddingVector": "",        # populated later by embedding pipeline
            "UseCase":         use_case,
            "ClipPosition":    clip_pos,
            "Intensity":       intensity,
            "Origin":          origin,
        }

        try:
            table_client.upsert_entity(entity)
        except Exception as e:
            print(f"  TABLE FAIL: {e}")
            failed += 1
            continue

        print(f"\r  [ OK ] {label}  {duration_ms:>5}ms  {clip_pos:<25}  {intensity}")
        uploaded += 1

    # ── Summary ───────────────────────────────────────────────────────────────
    print()
    print(f"{'─' * 50}")
    print(f"  Uploaded  : {uploaded}")
    print(f"  Skipped   : {skipped}  (already in table; use --force to re-upload)")
    print(f"  Missing   : {missing}  (MP3 not in {SOUNDS_DIR.name}/)")
    print(f"  Failed    : {failed}")
    print()
    if not args.dry_run:
        print(f"  Blob container : https://.../{BLOB_CONTAINER}/")
        print(f"  Table          : {TABLE_NAME}  (PartitionKey='{PARTITION_KEY}')")
    if missing:
        print()
        print(f"  Tip: run tools/download-meme-sounds.py to download missing MP3 files.")
    if failed:
        sys.exit(1)


# ─── CLI ─────────────────────────────────────────────────────────────────────

def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(
        description="Deploy meme sound library to Azure Blob + Table Storage.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__,
    )

    auth = p.add_argument_group("Authentication (one required)")
    auth.add_argument(
        "--connection-string", "-c",
        metavar="CONN_STR",
        default="",
        help="Full Azure Storage connection string.",
    )
    auth.add_argument(
        "--blob-endpoint",
        metavar="URL",
        default="",
        help="Azure Blob Storage endpoint URL (uses DefaultAzureCredential).",
    )
    auth.add_argument(
        "--table-endpoint",
        metavar="URL",
        default="",
        help="Azure Table Storage endpoint URL (uses DefaultAzureCredential).",
    )

    p.add_argument(
        "--dry-run", "-n",
        action="store_true",
        help="Validate files and metadata without writing anything to Azure.",
    )
    p.add_argument(
        "--force", "-f",
        action="store_true",
        help="Re-upload blobs and upsert table rows even if they already exist.",
    )
    return p.parse_args()


if __name__ == "__main__":
    args = parse_args()
    deploy(args)
