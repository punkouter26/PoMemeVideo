"""
Meme Sounds Downloader — Extended (300 more sounds)
Fetches an additional 300 meme sounds from myinstants.com API and appends them
to the existing tools/meme-sounds/sounds-metadata.json.

Enhancements over the original script:
  - Richer actionVectorTags with 50+ keyword categories
  - Detailed timing hints (clipPosition, intensity, durationHint)
  - Avoids duplicates by checking existing sourceUrls
  - Polite crawl delay with exponential back-off on failure

Usage:
    python tools/download-more-meme-sounds.py
"""

import json
import os
import re
import time
import urllib.request
import urllib.error
from pathlib import Path

OUTPUT_DIR  = Path(__file__).parent / "meme-sounds"
META_FILE   = OUTPUT_DIR / "sounds-metadata.json"
API_BASE    = "https://www.myinstants.com/api/v1/instants/?format=json&page={page}"
TARGET_NEW  = 300
DELAY_SEC   = 0.35   # polite crawl delay
MAX_RETRIES = 3

# ── Enhanced action-vector tag inference ─────────────────────────────────────
# Maps keywords found in the sound name → meme-action tags used by the AI director.
# More granular than the original — gives the video analyzer richer context.
KEYWORD_TAGS: list[tuple[list[str], list[str]]] = [
    # Impact / hits
    (["boom", "vine boom", "explosion", "blast", "cannon", "thud", "slam"],
     ["impact", "boom", "sudden", "high-energy"]),
    (["punch", "hit", "smack", "slap", "bonk", "whack"],
     ["impact", "action", "sudden", "physical"]),
    (["crash", "shatter", "break", "snap", "crack", "crunch"],
     ["impact", "sudden", "fail", "physical"]),
    (["stomp", "stomp stomp"],
     ["impact", "action", "motion"]),

    # Reactions / voice
    (["bruh", "bro", "brah"],
     ["reaction", "fail", "disapproval", "relatable"]),
    (["oof"],
     ["fail", "impact", "sudden", "relatable"]),
    (["nope", "nah", "no no no", "not today", "absolutely not"],
     ["reaction", "disapproval", "fail"]),
    (["yes", "yep", "yeah", "yay", "alright", "okay", "ok"],
     ["reaction", "win", "positive"]),
    (["what", "wut", "huh"],
     ["reaction", "surprise", "confused"]),
    (["wait", "hold on", "pause"],
     ["reaction", "sudden", "pause"]),
    (["oh no", "uh oh", "oh snap", "oh boy", "oh man"],
     ["reaction", "fail", "suspense", "relatable"]),
    (["wow", "woah", "whoa", "damn", "omg"],
     ["reaction", "surprise", "sudden"]),
    (["scream", "ahh", "ahhh", "shriek", "yell", "screaming"],
     ["surprise", "reaction", "sudden", "high-energy"]),
    (["gasp", "shocked", "disbelief"],
     ["surprise", "reaction", "sudden"]),
    (["sigh", "exhale"],
     ["fail", "sad", "relatable", "tired"]),
    (["moan", "groan", "ugh"],
     ["fail", "relatable", "disapproval"]),

    # Comedy / funny
    (["laugh", "lol", "haha", "giggle", "cackle", "chuckle", "snicker", "hehe"],
     ["laugh", "funny", "happy", "reaction"]),
    (["fart", "toot", "flatulence", "brap"],
     ["funny", "fail", "gross", "relatable"]),
    (["troll", "rickroll", "never gonna", "trolled", "got em", "gotcha"],
     ["funny", "troll", "reaction"]),
    (["meme", "dank", "poggers", "pog", "kek"],
     ["funny", "troll", "reaction", "gaming"]),
    (["silly", "goofy", "wacky", "quirky", "random"],
     ["funny", "relatable"]),
    (["comedy", "joke", "punchline"],
     ["funny", "laugh"]),

    # Fail / loss
    (["fail", "loss", "loser", "oops", "wrong", "error", "mistake"],
     ["fail", "sad", "sudden"]),
    (["sad", "crying", "cry", "tears", "weep", "sob"],
     ["sad", "fail", "emotional"]),
    (["violin", "dramatic", "dramatic music"],
     ["sad", "fail", "dramatic"]),
    (["game over", "you died", "death sound", "defeat"],
     ["fail", "sudden", "sad", "gaming"]),
    (["lose", "lost", "elimination", "eliminated"],
     ["fail", "sad", "gaming"]),
    (["noooo", "no!", "why", "curse"],
     ["fail", "emotional", "dramatic"]),

    # Win / celebration
    (["win", "victory", "success", "champion", "winner", "level up"],
     ["win", "celebration", "happy", "high-energy"]),
    (["achievement", "unlocked", "trophy", "medal"],
     ["win", "notification", "celebration"]),
    (["airhorn", "air horn", "horn", "honk", "trumpet"],
     ["impact", "sudden", "win", "high-energy"]),
    (["clap", "applause", "crowd", "cheering", "cheer"],
     ["win", "celebration", "reaction"]),
    (["fanfare", "jingle", "victory music"],
     ["win", "celebration", "music"]),
    (["money", "cash", "coins", "cha-ching", "ka-ching", "payday"],
     ["win", "celebration", "funny"]),
    (["poggers", "lets go", "let's go", "goat", "goated"],
     ["win", "celebration", "gaming", "high-energy"]),

    # Surprise / jump scare
    (["jump scare", "jumpscare", "scare", "boo"],
     ["surprise", "sudden", "high-energy", "impact"]),
    (["plot twist", "twist", "reveal", "unexpected"],
     ["surprise", "sudden", "dramatic"]),
    (["shocking", "holy", "no way", "unbelievable"],
     ["surprise", "reaction", "sudden"]),

    # Suspense / drama
    (["suspense", "tension", "ominous", "dark", "eerie"],
     ["suspense", "dramatic"]),
    (["drum", "roll", "snare", "rimshot", "ba dum tss", "ba dum"],
     ["dramatic", "suspense", "funny", "punchline"]),
    (["dun dun", "dun dun dun", "dundundunn"],
     ["dramatic", "suspense", "surprise"]),
    (["countdown", "tick tock", "timer", "clock"],
     ["suspense", "dramatic", "tension"]),
    (["evil", "villain", "sinister"],
     ["dramatic", "suspense", "funny"]),
    (["thunder", "lightning", "storm"],
     ["dramatic", "sudden", "impact"]),

    # Anime / gaming culture
    (["anime", "nani", "nani?", "sugoi", "kawaii", "senpai", "weeaboo"],
     ["reaction", "anime", "surprise", "funny"]),
    (["gg", "ez", "noob", "pwned", "rekt", "owned", "ratio"],
     ["gaming", "win", "reaction", "disapproval"]),
    (["respawn", "spawn", "game", "press f", "f in chat"],
     ["gaming", "fail", "reaction"]),
    (["boss", "boss fight", "boss music", "final boss"],
     ["gaming", "dramatic", "high-energy"]),
    (["level up", "xp", "rank up"],
     ["gaming", "win", "notification"]),
    (["ping", "lag", "connection"],
     ["gaming", "fail", "notification"]),
    (["among us", "sus", "impostor", "crewmate"],
     ["gaming", "funny", "troll"]),
    (["minecraft", "creeper", "ssss"],
     ["gaming", "funny", "sudden"]),
    (["fortnite", "roblox"],
     ["gaming", "funny", "relatable"]),

    # Notifications / tech
    (["discord", "notification", "ping", "ding", "bell", "chime"],
     ["notification", "sudden", "relatable"]),
    (["windows", "xp", "startup", "shutdown", "error sound"],
     ["notification", "funny", "nostalgic"]),
    (["iphone", "apple", "samsung", "text message", "sms"],
     ["notification", "sudden", "relatable"]),
    (["email", "inbox", "message"],
     ["notification", "relatable"]),

    # Animals
    (["cat", "meow", "nyan", "kitten"],
     ["funny", "animal", "reaction"]),
    (["dog", "woof", "bark", "doggo", "puppy"],
     ["funny", "animal", "reaction"]),
    (["frog", "pepe", "kermit"],
     ["funny", "animal", "troll"]),
    (["chicken", "cock-a-doodle"],
     ["funny", "animal"]),
    (["cow", "moo"],
     ["funny", "animal"]),
    (["monkey", "chimp"],
     ["funny", "animal", "reaction"]),
    (["goat", "goat scream"],
     ["funny", "animal", "sudden"]),
    (["bird", "tweet", "chirp"],
     ["funny", "animal", "soft"]),

    # Sounds / SFX
    (["pop", "bubble", "ping"],
     ["funny", "soft", "pop", "sudden"]),
    (["glass", "glass break", "shattering"],
     ["impact", "sudden", "dramatic"]),
    (["click", "button", "press"],
     ["notification", "soft", "subtle"]),
    (["beep", "boop", "blip"],
     ["notification", "funny", "soft"]),
    (["whoosh", "swish", "swoosh", "woosh"],
     ["action", "motion", "transition"]),
    (["slide", "slidein", "transition"],
     ["transition", "motion"]),
    (["zoom", "zoomin", "in"],
     ["action", "motion", "transition"]),
    (["rewind", "reverse", "flashback"],
     ["transition", "nostalgic"]),
    (["record scratch", "vinyl scratch", "scratch"],
     ["sudden", "transition", "funny"]),
    (["static", "glitch", "distortion"],
     ["sudden", "dramatic", "transition"]),

    # Movie/TV references
    (["star wars", "lightsaber", "jedi", "sith", "force"],
     ["movie", "action", "dramatic"]),
    (["batman", "superhero"],
     ["movie", "action", "funny"]),
    (["inception", "bwah", "bwaam", "braaam"],
     ["dramatic", "suspense", "impact", "movie"]),
    (["mission impossible"],
     ["suspense", "dramatic", "movie"]),
    (["jaws", "dun dun", "shark"],
     ["suspense", "dramatic", "movie"]),
    (["terminator", "i'll be back"],
     ["movie", "action", "funny"]),

    # Specific popular memes
    (["spongebob", "patrick"],
     ["funny", "relatable", "nostalgic"]),
    (["shrek"],
     ["funny", "troll", "nostalgic"]),
    (["minion", "minions"],
     ["funny", "relatable", "nostalgic"]),
    (["john cena", "and his name is"],
     ["sudden", "funny", "impact"]),
    (["to be continued", "roundabout"],
     ["sudden", "transition", "funny"]),
    (["emotional damage"],
     ["funny", "fail", "relatable"]),
    (["metal pipe", "metal pipe falling"],
     ["sudden", "funny", "impact"]),
    (["rizz", "sigma", "alpha"],
     ["funny", "relatable", "win"]),
    (["skibidi", "ohio", "gyatt", "rizz"],
     ["funny", "troll", "relatable", "brainrot"]),
    (["hawk tuah", "tuah"],
     ["funny", "troll", "reaction"]),
    (["mewing", "looksmaxx"],
     ["funny", "troll", "relatable"]),
    (["w rizz", "no cap", "bussin", "lowkey"],
     ["funny", "relatable", "troll"]),
    (["jumpscare", "creepy", "horror"],
     ["surprise", "sudden", "high-energy", "impact"]),

    # Music / background
    (["music", "song", "theme", "bgm", "ost", "soundtrack", "melody"],
     ["music", "background"]),
    (["bass", "bass boost", "bass drop"],
     ["music", "impact", "high-energy"]),
    (["earrape", "loud", "distorted"],
     ["sudden", "high-energy", "impact"]),
    (["lofi", "chill", "ambient", "relaxing"],
     ["music", "background", "soft"]),

    # Misc
    (["run", "chase", "escape", "flee"],
     ["action", "motion", "high-energy"]),
    (["power", "charge", "power up", "super", "ultra"],
     ["win", "dramatic", "action", "high-energy"]),
    (["silence", "quiet", "awkward pause", "crickets"],
     ["awkward", "dramatic", "funny"]),
    (["whistle", "wolf whistle"],
     ["funny", "reaction"]),
    (["slurp", "gulp", "drinking"],
     ["funny", "relatable"]),
    (["nom", "eating", "crunch eat"],
     ["funny", "relatable"]),
]


def infer_tags(name: str) -> list[str]:
    name_lower = name.lower()
    tags: set[str] = set()
    for keywords, tag_list in KEYWORD_TAGS:
        if any(kw in name_lower for kw in keywords):
            tags.update(tag_list)
    return sorted(tags) if tags else ["reaction", "funny"]


def infer_clip_position(tags: list[str]) -> str:
    """Suggest where in a video clip this sound works best."""
    tag_set = set(tags)
    if "transition" in tag_set:
        return "cut-point"           # right at an edit / transition
    if "high-energy" in tag_set and "impact" in tag_set:
        return "peak-moment"         # biggest moment in the clip
    if "suspense" in tag_set and "dramatic" in tag_set:
        return "build-up"            # before the reveal
    if "win" in tag_set or "celebration" in tag_set:
        return "climax-or-resolution"
    if "fail" in tag_set and "sudden" in tag_set:
        return "fail-moment"
    if "notification" in tag_set:
        return "any"
    if "music" in tag_set or "background" in tag_set:
        return "throughout"
    return "any"


def infer_intensity(tags: list[str]) -> str:
    """Signal volume/intensity level for the video mixer."""
    tag_set = set(tags)
    if "high-energy" in tag_set or "earrape" in tag_set:
        return "high"
    if "soft" in tag_set or "background" in tag_set or "music" in tag_set:
        return "low"
    return "medium"


def infer_use_case(name: str, tags: list[str]) -> str:
    """Rich human-readable hint for the AI director."""
    tag_set = set(tags)
    name_l  = name.lower()

    # Priority order — most specific first
    if "brainrot" in tag_set:
        return "Perfect for Gen-Z brainrot edits; layer under relatable or absurd on-screen moments."
    if "earrape" in tag_set or ("high-energy" in tag_set and "impact" in tag_set and "sudden" in tag_set):
        return "Extreme-impact sound — use sparingly at the most shocking or highest-energy frame."
    if "transition" in tag_set:
        return "Layer exactly on a hard cut or scene transition for a comedic or dramatic punctuation."
    if "boom" in tag_set and "impact" in tag_set:
        return "Use on sudden impact moments, hard cuts, or when something hits hard on screen."
    if "record scratch" in name_l or "vinyl scratch" in name_l:
        return "Classic 'wait, what?' freeze-frame moment. Cut action on this sound, then hold."
    if "suspense" in tag_set and "dramatic" in tag_set:
        return "Build-up sound — play during slow-motion reveals, zooms-in, or tension before a punchline."
    if "countdown" in name_l or "tick" in name_l:
        return "Countdown or timer tension; use before a reveal or time-based comedic pay-off."
    if "punchline" in tag_set or ("drum" in tag_set and "funny" in tag_set):
        return "Rimshot / ba-dum-tss; fire exactly one beat after the joke lands on screen."
    if "fail" in tag_set and "sad" in tag_set and "sudden" in tag_set:
        return "Use the instant a character or situation fails — on the exact frame of the mistake."
    if "fail" in tag_set and "sad" in tag_set:
        return "Use when something goes wrong, a character fails, or a dramatic loss occurs."
    if "win" in tag_set and "high-energy" in tag_set:
        return "Trigger on victories, clutch moments, or when a character achieves something great."
    if "win" in tag_set or "celebration" in tag_set:
        return "Use on victories, successful outcomes, or triumphant reveals."
    if "jump scare" in name_l or ("surprise" in tag_set and "high-energy" in tag_set):
        return "Jump-scare punctuation — snap onto a sudden face-cut, zoom, or scene change."
    if "surprise" in tag_set or "sudden" in tag_set:
        return "Use on unexpected reveals, sudden cuts, or abrupt transitions."
    if "sad" in tag_set or "emotional" in tag_set:
        return "Use during emotional or dramatic scenes to amplify the mood."
    if "laugh" in tag_set or ("funny" in tag_set and "reaction" in tag_set):
        return "Use during comedic moments, unexpected events, or reaction shots."
    if "gaming" in tag_set and "fail" in tag_set:
        return "Gaming fail moment — sync to the on-screen death, mistake, or elimination."
    if "gaming" in tag_set and "win" in tag_set:
        return "Gaming win — use on clutch plays, level-ups, or victory screens."
    if "gaming" in tag_set:
        return "Gaming culture reference; use when the subject relates to video games or esports."
    if "anime" in tag_set:
        return "Anime reaction; use on absurd, over-the-top, or unexpectedly dramatic moments."
    if "animal" in tag_set:
        return "Animal meme sound; layer over cute, chaotic, or unexpectedly cute on-screen moments."
    if "notification" in tag_set:
        return "Use on screen notifications, pop-ups, or as a comedic punchline sound."
    if "music" in tag_set or "background" in tag_set:
        return "Background track suitable for montage sequences, time-lapses, or filler moments."
    if "nostalgic" in tag_set:
        return "Nostalgia hit — use when the on-screen content references the past or childhood."
    if "relatable" in tag_set:
        return "Relatable everyday reaction; drop in over mundane or universally recognizable moments."
    if "awkward" in tag_set:
        return "Awkward silence or pause; use right after an uncomfortable or cringe-worthy moment."
    if "action" in tag_set or "motion" in tag_set:
        return "Use during fast-paced action sequences, montages, or chase scenes."
    if "troll" in tag_set:
        return "Troll/rick-roll style; use when the video unexpectedly subverts expectations."
    if "disapproval" in tag_set:
        return "Disapproval reaction — overlay when a character does something questionable or wrong."
    return "General-purpose meme reaction sound; use at comedic or surprising moments."


def slugify_filename(name: str, slug: str) -> str:
    safe = re.sub(r"[^a-z0-9_-]", "", slug.lower().replace(" ", "-"))
    return safe[:60] or "sound"


def fetch_json(url: str) -> dict:
    for attempt in range(MAX_RETRIES):
        try:
            req = urllib.request.Request(
                url,
                headers={"User-Agent": "Mozilla/5.0 (MemeSoundSeeder/1.0)"},
            )
            with urllib.request.urlopen(req, timeout=20) as r:
                return json.loads(r.read())
        except Exception as e:
            if attempt == MAX_RETRIES - 1:
                raise
            wait = 1.5 ** attempt
            print(f"    Retry {attempt + 1}/{MAX_RETRIES} after {wait:.1f}s — {e}")
            time.sleep(wait)
    return {}


def download_file(url: str, dest: Path) -> bool:
    for attempt in range(MAX_RETRIES):
        try:
            req = urllib.request.Request(
                url,
                headers={"User-Agent": "Mozilla/5.0 (MemeSoundSeeder/1.0)"},
            )
            with urllib.request.urlopen(req, timeout=25) as r, open(dest, "wb") as f:
                f.write(r.read())
            return True
        except Exception as e:
            if attempt == MAX_RETRIES - 1:
                print(f"    ✗ Download failed after {MAX_RETRIES} attempts: {e}")
                return False
            time.sleep(0.8 * (attempt + 1))
    return False


def load_existing_meta() -> tuple[list[dict], set[str]]:
    """Returns (existing_sounds, seen_source_urls)."""
    if not META_FILE.exists():
        return [], set()
    with open(META_FILE, encoding="utf-8") as f:
        data = json.load(f)
    sounds = data.get("sounds", [])
    seen   = {s["sourceUrl"] for s in sounds}
    return sounds, seen


def write_meta(all_sounds: list[dict]) -> None:
    meta = {
        "version":     "1.0",
        "generated":   __import__("datetime").datetime.utcnow().isoformat() + "Z",
        "totalSounds": len(all_sounds),
        "sounds":      all_sounds,
    }
    with open(META_FILE, "w", encoding="utf-8") as f:
        json.dump(meta, f, indent=2, ensure_ascii=False)


def main() -> None:
    import io, sys
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)

    existing_sounds, seen_urls = load_existing_meta()
    start_count = len(existing_sounds)
    print(f"Existing sounds: {start_count}")
    print(f"Target new:      {TARGET_NEW}")
    print(f"Output:          {OUTPUT_DIR}\n")

    new_sounds: list[dict] = []
    page      = 1
    skipped   = 0

    while len(new_sounds) < TARGET_NEW:
        print(f"  Fetching page {page}…")
        try:
            data = fetch_json(API_BASE.format(page=page))
        except Exception as e:
            print(f"  API error on page {page}: {e}")
            page += 1
            time.sleep(2)
            if page > 80:          # safety stop
                break
            continue

        results = data.get("results", [])
        if not results:
            print("  No more results from API.")
            break

        for item in results:
            if len(new_sounds) >= TARGET_NEW:
                break

            sound_url: str = item.get("sound", "")
            name: str      = item.get("name", "").strip()
            slug: str      = item.get("slug", "").strip()

            if not sound_url or sound_url in seen_urls:
                skipped += 1
                continue

            # Only keep MP3 / WAV / OGG
            if not re.search(r"\.(mp3|wav|ogg)$", sound_url, re.IGNORECASE):
                skipped += 1
                continue

            seen_urls.add(sound_url)
            ext      = Path(sound_url).suffix.lower()
            filename = slugify_filename(name, slug) + ext
            dest     = OUTPUT_DIR / filename

            tags         = infer_tags(name)
            use_case     = infer_use_case(name, tags)
            clip_pos     = infer_clip_position(tags)
            intensity    = infer_intensity(tags)

            idx = len(new_sounds) + 1
            print(
                f"  [{idx:>3}/{TARGET_NEW}] {name[:48]:<48}  "
                f"tags: {', '.join(tags[:3])}  pos: {clip_pos}"
            )

            ok = True
            if dest.exists():
                print(f"    ↳ already exists, skipping download")
            else:
                ok = download_file(sound_url, dest)

            if ok or dest.exists():
                new_sounds.append({
                    "id":          slug,
                    "displayName": name,
                    "filename":    filename,
                    "sourceUrl":   sound_url,
                    "durationMs":  0,
                    "actionVectorTags": tags,
                    "useCase":     use_case,
                    # Extended metadata for the AI video director
                    "clipPosition": clip_pos,
                    "intensity":    intensity,
                    "origin":       "myinstants.com",
                })
            else:
                skipped += 1

            time.sleep(DELAY_SEC)

        page += 1
        if not data.get("next"):
            print("  Reached last page of API.")
            break

    print(f"\n✓ Collected {len(new_sounds)} new sounds  (skipped {skipped})")

    all_sounds = existing_sounds + new_sounds
    write_meta(all_sounds)

    print(f"✓ Metadata written → {META_FILE}")
    print(f"  Total sounds in library: {len(all_sounds)}")


if __name__ == "__main__":
    main()
