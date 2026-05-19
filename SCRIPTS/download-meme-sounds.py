"""
Meme Sounds Downloader
Downloads ~200 meme sounds from myinstants.com API and generates metadata JSON.
Output: tools/meme-sounds/ (MP3 files + sounds-metadata.json)
"""

import json
import os
import re
import time
import urllib.request
import urllib.error
from pathlib import Path

OUTPUT_DIR = Path(__file__).parent / "meme-sounds"
META_FILE  = OUTPUT_DIR / "sounds-metadata.json"
API_BASE   = "https://www.myinstants.com/api/v1/instants/?format=json&page={page}"
TARGET     = 200
DELAY_SEC  = 0.3  # polite crawl delay

# ── Action-vector tag inference ────────────────────────────────────────────────
# Maps keywords found in the sound name → meme-action tags used by the AI director
KEYWORD_TAGS: list[tuple[list[str], list[str]]] = [
    (["boom", "vine boom", "explosion", "blast", "cannon"],         ["impact", "boom", "sudden"]),
    (["bruh", "bro"],                                               ["reaction", "fail", "disapproval"]),
    (["fart", "toot"],                                              ["funny", "fail", "gross"]),
    (["wow", "woah", "whoa"],                                       ["reaction", "surprise"]),
    (["laugh", "lol", "haha", "giggle", "cackle"],                  ["laugh", "funny", "happy"]),
    (["sad", "crying", "cry", "violin", "dramatic"],                ["sad", "fail", "dramatic"]),
    (["win", "victory", "success", "yeah", "yay"],                  ["win", "celebration", "happy"]),
    (["fail", "loss", "loser", "oops", "wrong", "error"],           ["fail", "sad", "sudden"]),
    (["scream", "ahh", "ahhh", "shriek", "yell"],                   ["surprise", "reaction", "sudden"]),
    (["nyan", "cat", "meow", "woof", "dog"],                        ["funny", "animal"]),
    (["oof"],                                                        ["fail", "impact", "sudden"]),
    (["airhorn", "horn", "honk"],                                   ["impact", "sudden", "win"]),
    (["surprise", "gasp", "shocked"],                               ["surprise", "reaction", "sudden"]),
    (["music", "song", "theme", "bgm", "ost"],                      ["music", "background"]),
    (["evil", "villain", "sinister", "dark"],                       ["dramatic", "suspense"]),
    (["clap", "applause", "crowd"],                                  ["win", "celebration", "reaction"]),
    (["pop", "bubble"],                                             ["funny", "soft", "pop"]),
    (["alert", "notification", "ping", "ding"],                     ["sudden", "notification"]),
    (["anime", "ja", "nani", "nani?"],                              ["reaction", "anime", "surprise"]),
    (["troll", "meme", "rickroll", "never gonna"],                   ["funny", "troll"]),
    (["gun", "shot", "bang", "pistol"],                             ["impact", "sudden", "action"]),
    (["money", "cash", "coins"],                                    ["win", "celebration"]),
    (["hit", "punch", "smack", "slap"],                             ["impact", "action", "sudden"]),
    (["run", "chase", "escape"],                                    ["action", "motion"]),
    (["power", "charge", "up"],                                     ["win", "dramatic", "action"]),
    (["no", "stop", "dont", "don't"],                               ["reaction", "disapproval", "fail"]),
    (["yes", "okay", "ok", "alright"],                              ["reaction", "win"]),
    (["silence", "quiet", "awkward"],                               ["awkward", "dramatic"]),
    (["drum", "roll", "snare"],                                     ["dramatic", "suspense"]),
    (["whistle", "tweet"],                                          ["funny", "soft"]),
]


def infer_tags(name: str) -> list[str]:
    name_lower = name.lower()
    tags: set[str] = set()
    for keywords, tag_list in KEYWORD_TAGS:
        if any(kw in name_lower for kw in keywords):
            tags.update(tag_list)
    return sorted(tags) if tags else ["reaction", "funny"]


def infer_use_case(name: str, tags: list[str]) -> str:
    """Human-readable description of when to use this sound in a video."""
    tag_set = set(tags)
    name_l  = name.lower()

    if "boom" in tag_set and "impact" in tag_set:
        return "Use on sudden impact moments, hard cuts, or when something hits hard on screen."
    if "fail" in tag_set and "sad" in tag_set:
        return "Use when something goes wrong, a character fails, or a dramatic loss occurs."
    if "laugh" in tag_set or "funny" in tag_set:
        return "Use during comedic moments, unexpected events, or reaction shots."
    if "win" in tag_set or "celebration" in tag_set:
        return "Use on victories, successful outcomes, or triumphant reveals."
    if "surprise" in tag_set or "sudden" in tag_set:
        return "Use on jump scares, unexpected reveals, or abrupt transitions."
    if "sad" in tag_set or "dramatic" in tag_set:
        return "Use during emotional or dramatic scenes to amplify the mood."
    if "music" in tag_set:
        return "Background music suitable for montage sequences or filler moments."
    if "disapproval" in tag_set or "reaction" in tag_set:
        return "Use as a reaction sound to character dialogue or on-screen events."
    if "action" in tag_set or "motion" in tag_set:
        return "Use during fast-paced action sequences or chase scenes."
    if "suspense" in tag_set:
        return "Use to build tension before a reveal or climactic moment."
    return "General-purpose meme reaction sound; use at comedic or surprising moments."


def slugify_filename(name: str, slug: str) -> str:
    safe = re.sub(r"[^a-z0-9_-]", "", slug.lower().replace(" ", "-"))
    return safe[:60] or "sound"


def fetch_json(url: str) -> dict:
    req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0 (MemeSoundSeeder/1.0)"})
    with urllib.request.urlopen(req, timeout=15) as r:
        return json.loads(r.read())


def download_file(url: str, dest: Path) -> bool:
    try:
        req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0 (MemeSoundSeeder/1.0)"})
        with urllib.request.urlopen(req, timeout=20) as r, open(dest, "wb") as f:
            f.write(r.read())
        return True
    except Exception as e:
        print(f"    ✗ Download failed: {e}")
        return False


def main() -> None:
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    collected: list[dict] = []
    seen_urls: set[str] = set()
    page = 1
    skipped = 0

    print(f"Target: {TARGET} sounds → {OUTPUT_DIR}\n")

    while len(collected) < TARGET:
        print(f"  Fetching page {page}…")
        try:
            data = fetch_json(API_BASE.format(page=page))
        except Exception as e:
            print(f"  API error: {e}")
            break

        results = data.get("results", [])
        if not results:
            print("  No more results.")
            break

        for item in results:
            if len(collected) >= TARGET:
                break

            sound_url: str = item.get("sound", "")
            name: str      = item.get("name", "").strip()
            slug: str      = item.get("slug", "").strip()

            if not sound_url or sound_url in seen_urls:
                skipped += 1
                continue

            # Only keep MP3 / WAV
            if not re.search(r"\.(mp3|wav|ogg)$", sound_url, re.IGNORECASE):
                skipped += 1
                continue

            seen_urls.add(sound_url)
            ext      = Path(sound_url).suffix.lower()
            filename = slugify_filename(name, slug) + ext
            dest     = OUTPUT_DIR / filename

            tags     = infer_tags(name)
            use_case = infer_use_case(name, tags)

            idx = len(collected) + 1
            print(f"  [{idx:>3}/{TARGET}] {name[:50]:<50}  tags: {', '.join(tags[:3])}")

            ok = True
            if dest.exists():
                print(f"    ↳ already exists, skipping download")
            else:
                ok = download_file(sound_url, dest)

            if ok or dest.exists():
                collected.append({
                    "id":          slug,
                    "displayName": name,
                    "filename":    filename,
                    "sourceUrl":   sound_url,
                    "durationMs":  0,           # populated at seed time via ffprobe / server-side
                    "actionVectorTags": tags,
                    "useCase":     use_case,
                    "origin":      "myinstants.com",
                })
            else:
                skipped += 1

            time.sleep(DELAY_SEC)

        page += 1
        if not data.get("next"):
            print("  Reached last page.")
            break

    # Write metadata
    meta = {
        "version":     "1.0",
        "generated":   __import__("datetime").datetime.utcnow().isoformat() + "Z",
        "totalSounds": len(collected),
        "sounds":      collected,
    }
    with open(META_FILE, "w", encoding="utf-8") as f:
        json.dump(meta, f, indent=2, ensure_ascii=False)

    print(f"\n✓ Downloaded {len(collected)} sounds ({skipped} skipped)")
    print(f"✓ Metadata → {META_FILE}")


if __name__ == "__main__":
    main()
