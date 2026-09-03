"""
Meme Sounds Downloader
Downloads ~200 meme sounds from myinstants.com API and generates metadata JSON.
Output: scripts/meme-sounds/ (MP3 files + sounds-metadata.json)
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


# ── Curated priority sounds ───────────────────────────────────────────────────
# The signature sound palette of wojak-storytelling YouTube videos (Low Budget
# Stories, Life of W, …). Always downloaded first and flagged "priority": true so
# the AI director favors them over generic trending matches. Ids must stay stable:
# they derive the table RowKey (DeriveStableGuid), so renaming one creates a new row.
PRIORITY_SOUNDS: list[dict] = [
    {"id": "vine-boom-sound-70972",                        "displayName": "VINE BOOM SOUND",                        "sourceUrl": "https://www.myinstants.com/media/sounds/vine-boom.mp3",                              "tags": ["impact", "boom", "sudden"],                    "useCase": "Use on sudden impact moments, hard cuts, or punchline zooms."},
    {"id": "jixaw-metal-pipe-falling-sound-28270",         "displayName": "jixaw metal pipe falling sound",         "sourceUrl": "https://www.myinstants.com/media/sounds/jixaw-metal-pipe-falling-sound.mp3",         "tags": ["impact", "sudden", "fail"],                    "useCase": "Use on sudden disasters, collapses, or chaotic accidents."},
    {"id": "bruh",                                          "displayName": "BRUH",                                   "sourceUrl": "https://www.myinstants.com/media/sounds/movie_1.mp3",                                "tags": ["reaction", "fail", "disapproval"],             "useCase": "Use as a deadpan reaction to something dumb or disappointing."},
    {"id": "aughhhhh-aughhhhh-7905",                        "displayName": "AUGHHHHH... AUGHHHHH",                   "sourceUrl": "https://www.myinstants.com/media/sounds/aughhhhh-aughhhhh.mp3",                      "tags": ["surprise", "reaction", "sudden", "sad"],       "useCase": "Use for internal suffering, agony, or dramatic despair."},
    {"id": "windows-xp-error",                              "displayName": "Windows XP Error",                       "sourceUrl": "https://www.myinstants.com/media/sounds/erro.mp3",                                   "tags": ["fail", "sudden", "notification"],              "useCase": "Use when something breaks, malfunctions, or goes wrong."},
    {"id": "windows-xp-shutdown",                           "displayName": "windows xp shutdown",                    "sourceUrl": "https://www.myinstants.com/media/sounds/preview_4.mp3",                              "tags": ["fail", "sad", "notification"],                 "useCase": "Use when a character mentally shuts down or gives up."},
    {"id": "badum-tss",                                     "displayName": "BADUM TSS",                              "sourceUrl": "https://www.myinstants.com/media/sounds/joke_drum_effect.mp3",                       "tags": ["dramatic", "funny", "suspense"],               "useCase": "Use after an ironic joke or corny punchline lands."},
    {"id": "sad-violin-the-meme-one",                       "displayName": "Sad Violin (the meme one)",              "sourceUrl": "https://www.myinstants.com/media/sounds/tf_nemesis.mp3",                             "tags": ["sad", "fail", "dramatic"],                     "useCase": "Use for melancholy, dramatic loss, or doomer moments."},
    {"id": "spongebob-fail-11236",                          "displayName": "SpongeBob Fail",                         "sourceUrl": "https://www.myinstants.com/media/sounds/spongebob-fail.mp3",                         "tags": ["fail", "sad", "funny"],                        "useCase": "Use on small pathetic failures and anticlimaxes."},
    {"id": "a-few-moments-later-sponge-bob-sfx-fun-80331",  "displayName": "a few moments later sponge bob sfx fun", "sourceUrl": "https://www.myinstants.com/media/sounds/a-few-moments-later-sponge-bob-sfx-fun.mp3", "tags": ["music", "background", "funny"],                "useCase": "Use as a time-skip transition between scenes."},
    {"id": "to-be-continued-jojo",                          "displayName": "To be Continued (jojo)",                 "sourceUrl": "https://www.myinstants.com/media/sounds/untitled_1071.mp3",                          "tags": ["music", "dramatic", "suspense"],               "useCase": "Use on freeze-frame cliffhanger endings right before disaster."},
    {"id": "directed-by-robert-b-weide-451",                "displayName": "Directed by Robert B Weide",             "sourceUrl": "https://www.myinstants.com/media/sounds/directed-by-robert-b_voI2Z4T.mp3",           "tags": ["music", "funny", "dramatic"],                  "useCase": "Use as a cut-to-credits right before an impending disaster."},
    {"id": "coffin-dance-meme-31063",                       "displayName": "Coffin Dance Meme",                      "sourceUrl": "https://www.myinstants.com/media/sounds/y2mate-mp3cut_sRzY6rh.mp3",                  "tags": ["music", "funny", "fail"],                      "useCase": "Use when a character is done for or something dies."},
    {"id": "gta-v-wasted",                                  "displayName": "GTA V - Wasted",                         "sourceUrl": "https://www.myinstants.com/media/sounds/gta-v-death-sound-effect-102.mp3",           "tags": ["fail", "impact", "dramatic"],                  "useCase": "Use when a character fails hard or gets destroyed."},
    {"id": "roblox-oof",                                    "displayName": "ROBLOX oof",                             "sourceUrl": "https://www.myinstants.com/media/sounds/roblox-death-sound_1.mp3",                   "tags": ["fail", "impact", "sudden"],                    "useCase": "Use on comedic minor deaths, hits, or fumbles."},
    {"id": "discord-notification-38119",                    "displayName": "Discord Notification",                   "sourceUrl": "https://www.myinstants.com/media/sounds/discord-notification.mp3",                   "tags": ["sudden", "notification"],                      "useCase": "Use when a character receives a message."},
    {"id": "discord-call-44910",                            "displayName": "discord call",                           "sourceUrl": "https://www.myinstants.com/media/sounds/discord-call-sound.mp3",                     "tags": ["sudden", "notification"],                      "useCase": "Use when someone is calling a character."},
    {"id": "iphone-notification-71441",                     "displayName": "iPhone Notification",                    "sourceUrl": "https://www.myinstants.com/media/sounds/notification_o14egLP.mp3",                   "tags": ["sudden", "notification"],                      "useCase": "Use for phone anxiety scenes or incoming texts."},
    {"id": "emotional-damage-meme-74555",                   "displayName": "Emotional Damage Meme",                  "sourceUrl": "https://www.myinstants.com/media/sounds/emotional-damage-meme.mp3",                  "tags": ["reaction", "funny", "fail"],                   "useCase": "Use when a verbal roast or insult lands."},
    {"id": "fart-with-reverb-17715",                        "displayName": "fart with reverb",                       "sourceUrl": "https://www.myinstants.com/media/sounds/fart-with-reverb.mp3",                       "tags": ["funny", "fail", "gross"],                      "useCase": "Use as a lowbrow punchline on absurd moments."},
    {"id": "taco-bell-bong-42481",                          "displayName": "Taco Bell Bong",                         "sourceUrl": "https://www.myinstants.com/media/sounds/taco-bell-bong-sfx.mp3",                     "tags": ["funny", "notification", "sudden"],             "useCase": "Use when stomach trouble or fast-food consequences loom."},
    {"id": "minecraft-villager-sound",                      "displayName": "Minecraft Villager Sound",               "sourceUrl": "https://www.myinstants.com/media/sounds/minecraft-villager-sound-effect.mp3",        "tags": ["funny", "reaction"],                           "useCase": "Use when an NPC-brained character speaks or reacts."},
    {"id": "anime-wow",                                     "displayName": "Anime Wow",                              "sourceUrl": "https://www.myinstants.com/media/sounds/anime-wow-sound-effect.mp3",                 "tags": ["reaction", "anime", "surprise"],               "useCase": "Use for fake amazement or mock wonder."},
    {"id": "awkward-cricket-74642",                         "displayName": "Awkward cricket",                        "sourceUrl": "https://www.myinstants.com/media/sounds/awkward-cricket-sound-effect.mp3",           "tags": ["awkward", "dramatic", "funny"],                "useCase": "Use when a joke bombs or an awkward silence hangs."},
    {"id": "why-are-you-running-15312",                     "displayName": "Why are you running?",                   "sourceUrl": "https://www.myinstants.com/media/sounds/why-are.mp3",                                "tags": ["action", "motion", "funny"],                   "useCase": "Use on chase or avoidance comedy moments."},
    {"id": "run-vine",                                      "displayName": "RUN vine",                               "sourceUrl": "https://www.myinstants.com/media/sounds/run-vine-sound-effect.mp3",                  "tags": ["action", "motion", "sudden"],                  "useCase": "Use on panic escapes and sudden retreats."},
    {"id": "fbi-open-up-with-explosion-491",                "displayName": "FBI OPEN UP (with explosion)",           "sourceUrl": "https://www.myinstants.com/media/sounds/fbi-open-up_dwLhIFf.mp3",                    "tags": ["impact", "sudden", "action"],                  "useCase": "Use when a character does something illegal-adjacent."},
    {"id": "no-god-please-no-noooooooooo",                  "displayName": "NO GOD! PLEASE NO!!! NOOOOOOOO",         "sourceUrl": "https://www.myinstants.com/media/sounds/no-god-please-no-noooooooooo.mp3",           "tags": ["fail", "disapproval", "reaction", "dramatic"], "useCase": "Use for maximum despair when the worst possible outcome happens."},
    {"id": "ah-shit-here-we-go-again",                      "displayName": "Ah Shit, Here We Go Again (GTA SA)",     "sourceUrl": "https://www.myinstants.com/media/sounds/ah-shit-here-we-go-again.mp3",               "tags": ["fail", "reaction", "dramatic"],                "useCase": "Use when a familiar disaster repeats or a recurring struggle restarts."},
    {"id": "mission-failed-well-get-em-next-time",          "displayName": "Mission Failed, We'll Get 'Em Next Time","sourceUrl": "https://www.myinstants.com/media/sounds/mission-failed-well-get-em-next-time.mp3",   "tags": ["fail", "sad", "dramatic"],                     "useCase": "Use when a plan collapses and the character accepts defeat."},
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

    # ── Curated priority sounds first — always included, never subject to crawl luck ──
    print(f"Downloading {len(PRIORITY_SOUNDS)} curated priority sounds…")
    for entry in PRIORITY_SOUNDS:
        sound_url = entry["sourceUrl"]
        ext       = Path(sound_url).suffix.lower() or ".mp3"
        filename  = slugify_filename(entry["displayName"], entry["id"]) + ext
        dest      = OUTPUT_DIR / filename

        seen_urls.add(sound_url)
        print(f"  [PRI] {entry['displayName'][:50]:<50}  tags: {', '.join(entry['tags'][:3])}")

        ok = True
        if dest.exists():
            print(f"    ↳ already exists, skipping download")
        else:
            ok = download_file(sound_url, dest)
            time.sleep(DELAY_SEC)

        if ok or dest.exists():
            collected.append({
                "id":          entry["id"],
                "displayName": entry["displayName"],
                "filename":    filename,
                "sourceUrl":   sound_url,
                "durationMs":  0,
                "actionVectorTags": entry["tags"],
                "useCase":     entry["useCase"],
                "origin":      "myinstants.com",
                "priority":    True,
            })
        else:
            skipped += 1
    print()

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
                    "priority":    False,
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
