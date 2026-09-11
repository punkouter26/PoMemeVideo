# scripts

Six files, all of them load-bearing. Run from the repository root.

| File | What it is |
|---|---|
| `setup.ps1` | The one bootstrap entrypoint — winget prerequisites, ports 7000/5001, Azurite via docker compose, mock-key check, sound seeding, agent tooling, `az login` status |
| `check-azurite.py` | Verifies the local Azurite container answers on Blob, Queue and Table |
| `check-test-budgets.ps1` | Enforces per-suite test quotas; `ci.yml` fails the build on a violation |
| `requirements.txt` | Python deps for `check-azurite.py` |
| `meme-sounds/sounds-metadata.json` | The sound library manifest — id, display name, filename, `sourceUrl`, tags |
| `meme-sounds/*.mp3` | Gitignored. Fetched on demand by the seeder, never committed |

## Prerequisites

- .NET 10 SDK, Docker Desktop, PowerShell 7+
- Python 3.10+ and `pip install -r scripts/requirements.txt` (only for `check-azurite.py`)

## Bootstrap

```powershell
pwsh -File scripts/setup.ps1

pwsh -File scripts/setup.ps1 -SkipWinget   # project bootstrap only
pwsh -File scripts/setup.ps1 -SkipSeed     # skip the sound library
```

## Seeding the sound library

Seeding is a verb on the app, not a script:

```bash
dotnet run --project src/PoMemeVideo.Api -- seed-sounds
```

It reads `meme-sounds/sounds-metadata.json`, uploads each `.mp3` to the `sounds` blob container
and writes a `SoundAssets` table row. **Any clip missing from disk is downloaded from its
`sourceUrl` directly into blob storage**, so a bare clone needs no separate download step — this
is what replaced the old `download-meme-sounds.py` + `seed-meme-sounds.py` pair. Re-runs are
idempotent: a row is skipped only when its blob is present, the right size, and its `BlobUrl`
already points at our own container.

Target a real storage account by setting the connection string rather than passing a flag:

```bash
ConnectionStrings__AzureStorage="DefaultEndpointsProtocol=https;AccountName=…" \
  dotnet run --project src/PoMemeVideo.Api -- seed-sounds
```

`--seeds-dir <path>` overrides metadata discovery if you are running from an unusual directory.

## Verifying local storage

```bash
python scripts/check-azurite.py
```
