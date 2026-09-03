"""
Downloads all 202 genuine meme sounds specified in sounds-metadata.json into scripts/meme-sounds/
"""

import json
import time
import urllib.request
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path

SOUNDS_DIR = Path(__file__).parent / "meme-sounds"
META_FILE = SOUNDS_DIR / "sounds-metadata.json"
USER_AGENT = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"


def download_one(entry: dict) -> tuple[str, bool, int]:
    filename = entry.get("filename")
    url = entry.get("sourceUrl")
    if not filename or not url:
        return (str(filename), False, 0)

    dest = SOUNDS_DIR / filename
    if dest.exists() and dest.stat().st_size > 10000:
        return (filename, True, dest.stat().st_size)

    for attempt in range(3):
        try:
            req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
            with urllib.request.urlopen(req, timeout=15) as resp:
                content = resp.read()
                if len(content) > 1000:
                    with open(dest, "wb") as f:
                        f.write(content)
                    return (filename, True, len(content))
        except Exception:
            time.sleep(0.5)

    return (filename, False, 0)


def main():
    SOUNDS_DIR.mkdir(parents=True, exist_ok=True)
    with open(META_FILE, "r", encoding="utf-8") as f:
        meta = json.load(f)

    sounds = meta.get("sounds", [])
    print(f"Starting download for {len(sounds)} meme sounds...")

    success = 0
    failed = 0

    with ThreadPoolExecutor(max_workers=8) as pool:
        futures = {pool.submit(download_one, s): s for s in sounds}
        for fut in as_completed(futures):
            fn, ok, size = fut.result()
            if ok:
                success += 1
                if success % 25 == 0 or success == len(sounds):
                    print(f"  Progress: {success}/{len(sounds)} downloaded ({fn} - {size} bytes)")
            else:
                failed += 1
                print(f"  Failed to download: {fn}")

    print(f"\nFinished: {success} succeeded, {failed} failed.")


if __name__ == "__main__":
    main()
