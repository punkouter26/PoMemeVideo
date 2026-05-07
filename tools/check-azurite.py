import sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

from azure.data.tables import TableServiceClient
from azure.storage.blob import BlobServiceClient

CONN = "UseDevelopmentStorage=true"

# Table check
try:
    tbl = TableServiceClient.from_connection_string(CONN).get_table_client("SoundAssets")
    filter_str = "PartitionKey eq 'library'"
    rows = list(tbl.query_entities(filter_str))
    print(f"Table 'SoundAssets' rows : {len(rows)}")
    if rows:
        r = rows[0]
        print(f"  Sample DisplayName  : {r.get('DisplayName','')}")
        print(f"  Sample BlobUrl      : {r.get('BlobUrl','')[:70]}")
        print(f"  Sample ClipPosition : {r.get('ClipPosition','(none)')}")
        print(f"  Sample Intensity    : {r.get('Intensity','(none)')}")
        print(f"  Sample UseCase      : {r.get('UseCase','')[:60]}")
except Exception as e:
    print(f"Table error: {e}")

print()

# Blob check
try:
    container = BlobServiceClient.from_connection_string(CONN).get_container_client("sounds")
    blobs = list(container.list_blobs())
    total_bytes = sum(b.size for b in blobs)
    print(f"Blob container 'sounds' : {len(blobs)} files  ({total_bytes/1_048_576:.1f} MB)")
    if blobs:
        print(f"  Sample blob : {blobs[0].name}  ({blobs[0].size} bytes)")
except Exception as e:
    print(f"Blob error: {e}")
