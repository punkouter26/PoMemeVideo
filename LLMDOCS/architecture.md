# PoMemeVideo – Architecture Overview

> Auto-maintained. Update when structural changes are made.

## Solution Layout

```
PoMemeVideo.slnx
Directory.Build.props              # TreatWarningsAsErrors, Nullable
Directory.Packages.props           # Central Package Management
global.json                        # Pins .NET 10 SDK
Dockerfile                         # Linux + FFmpeg baked in
docker-compose.yml                 # Azurite on ports 10000-10002
├── src/
│   ├── PoMemeVideo.Api/           # ASP.NET Core 10 host — VSA feature slices, SignalR hub, Razor Pages
│   │   ├── Features/
│   │   │   ├── Admin/             # ClearAllData (DELETE /api/admin/data), SeedSoundsCommand
│   │   │   ├── Auth/              # AnonAuthHandler, GET /api/auth/me, Auth.http
│   │   │   ├── Config/            # GET /api/config → { useMockAI, isDevelopment }
│   │   │   ├── Ingestion/         # POST /api/ingestion/sas, POST+GET /api/ingestion/sessions
│   │   │   ├── MemeLibrary/       # GET /api/memelibrary/sounds
│   │   │   ├── Output/            # GET script/download/stream, DELETE wipe-buffer
│   │   │   └── Processing/        # POST /api/processing/sessions/{id}/initiate
│   │   ├── Endpoints/             # HealthEndpoint.cs → GET /health
│   │   ├── Hubs/                  # EngineHub.cs → SignalR /hubs/engine
│   │   └── Pages/                 # Diag.cshtml → GET /diag
│   ├── PoMemeVideo.Domain/        # Onion innermost — no external dependencies
│   │   ├── Entities/              # VideoSession, UserIdentity, DirectorScript, ScriptEntry, SoundAsset
│   │   ├── Interfaces/            # IVideoSessionRepository, ISoundAssetRepository, IUserIdentityRepository,
│   │   │                          # IAiVisionService, IDirectorService, IVideoRenderService, IEngineNotifier
│   │   └── ValueObjects/          # ActionVector (bag-of-words embedding via ToEmbedding(string[] vocabulary))
│   ├── PoMemeVideo.Application/   # Onion middle — use cases and services; no I/O
│   │   ├── Ingestion/             # IngestVideoCommand (validate → create VideoSession)
│   │   ├── MemeLibrary/           # SemanticMatchingService (SIMD cosine similarity)
│   │   ├── Processing/            # RunEngineCommand, TokenBucketTimingService
│   │   └── Rendering/             # RenderVideoCommand
│   ├── PoMemeVideo.Infrastructure/ # Onion outermost — all external I/O
│   │   ├── AzureOpenAi/           # AzureOpenAiVisionService (GPT-4o Vision)
│   │   ├── AzureStorage/          # TableClientFactory, BlobServiceClientFactory, BlobStorageService,
│   │   │                          # VideoSessionTableRepository, SoundAssetTableRepository,
│   │   │                          # DirectorScriptTableRepository, UserIdentityTableRepository
│   │   ├── BrowserLlm/            # BrowserLLMDirectorService (Transformers.js callback)
│   │   ├── FFmpeg/                # FFmpegRenderService (bounded Channel<RenderJob>)
│   │   ├── Mock/                  # MockAiVisionService, MockDirectorService
│   │   ├── Ollama/                # OllamaDirectorService (Gemma 4 via localhost:11434)
│   │   ├── RuntimeAiSettings.cs   # Runtime-switchable AI provider selection
│   │   └── SwitchingDirectorService.cs # Delegates to active IDirectorService implementation
│   ├── PoMemeVideo.Shared/        # No dependencies — consumed by Domain, Application, Infrastructure, Client
│   │   ├── Enums/                 # SessionStatus, VisualEffectType, PlacementType
│   │   └── Models/                # VideoSessionDto, ScriptEntryDto, SoundAssetDto, UserIdentityDto
│   └── client/
│       └── PoMemeVideo.Client/    # Blazor WASM SPA — hosted by PoMemeVideo.Api
│           ├── Pages/             # Source.razor, Engine.razor, Reveal.razor, Results.razor, Login.razor
│           ├── Components/        # AsciiDropZone, DitheredKeyframeStrip, DirectorLogFeed, DirectorScriptFeed,
│           │                      # SystemAuditBox, HardwareMonitor, CrtMonitorFrame, ScanlineOverlay,
│           │                      # NavBar, MockDataBanner
│           ├── Services/          # BlobUploadService, EngineHubClient
│           └── wwwroot/
│               ├── js/            # canvas-dither.js, glitch-transition.js
│               └── css/           # retro-terminal.css (Matrix Green aesthetic)
└── tests/
    ├── PoMemeVideo.UnitTests/     # xUnit — Domain + Application; no I/O
    │   ├── Ingestion/             # IngestVideoCommandTests
    │   ├── MemeLibrary/           # SemanticMatchingServiceTests
    │   ├── Processing/            # TokenBucketTimingServiceTests
    │   ├── Rendering/             # RenderVideoCommandTests
    │   └── Auth/                  # UserIdentityTests
    ├── PoMemeVideo.IntegrationTests/ # xUnit + Testcontainers (Azurite)
    │   ├── Ingestion/             # IngestionEndpointsTests
    │   ├── Processing/            # ProcessingEndpointsTests
    │   └── Auth/                  # AnonAuthTests
    └── PoMemeVideo.E2ETests/      # Playwright TypeScript (headless; server auto-started)
        └── tests/
            ├── 01-health.spec.ts
            ├── 02-spa.spec.ts
            ├── 03-ingestion.spec.ts
            ├── 04-memelibrary.spec.ts
            ├── 05-source-page.spec.ts  # US1 keyframe strip + drop zone
            └── 06-engine-page.spec.ts  # US2 Engine page + SignalR feeds
```

## Architecture Pattern

**Onion Architecture + Vertical Slice Architecture (VSA)**

Dependency direction (outermost → innermost):
`Infrastructure` → `Application` → `Domain`  
`Api` (host) → `Application` + `Infrastructure`  
`Shared` — no dependencies; consumed by all layers

VSA feature slices within `PoMemeVideo.Api/Features/`:

```
Features/
├── Admin/       ← DELETE /api/admin/data, dotnet run -- seed-sounds
├── Auth/        ← POST /auth/anon (dev), GET /api/auth/me
├── Config/      ← GET /api/config
├── Ingestion/   ← POST /api/ingestion/sas, POST/GET /api/ingestion/sessions/{id}
├── MemeLibrary/ ← GET /api/memelibrary/sounds
├── Output/      ← GET script/download/stream/video, DELETE wipe-buffer
└── Processing/  ← POST /api/processing/sessions/{id}/initiate
```

## Key Data Flows

### US1 — Video Ingestion
```
Browser: drop MP4 → validate (ext + size) → POST /api/ingestion/sas
  API: IngestVideoCommand → create VideoSession → return SAS URL
Browser: upload MP4 directly to Azurite Blob Storage via SAS
Browser: POST /api/ingestion/sessions (confirm blob path)
  API: update session status → Ingesting
Browser: JS dithering → DitheredKeyframeStrip renders
```

### US2 — Engine Processing
```
Browser: POST /api/processing/sessions/{id}/initiate → 202 Accepted
  API: RunEngineCommand (background Task)
    → IAiVisionService (Azure OpenAI GPT-4o / Mock) → timestamped labels
    → SemanticMatchingService (SIMD cosine) → top-3 candidates per label
    → TokenBucketTimingService → PlacementType decision per entry
    → IDirectorService (Ollama/AzureOpenAI/Mock) → DirectorScript with rationale
    → IEngineNotifier → SignalR EngineHub → client feeds
    → RenderVideoCommand → FFmpegRenderService → output.mp4 in Blob
    → IEngineNotifier.CompleteAsync → ProcessingComplete → client navigates to /reveal
```

### US3 — Reveal & Download
```
Browser /reveal/{id}:
  → glitch-transition.js (1.2 s CSS flicker)
  → GET /api/output/sessions/{id}/stream/video → CRT-framed <video autoplay>
  → GET /api/output/sessions/{id}/script → DirectorScriptFeed (pre-populated)
  → [ Download MP4 ] → GET /api/output/sessions/{id}/download/video
  → [ Download JSON ] → GET /api/output/sessions/{id}/download/script
  → [ WIPE BUFFER ] → DELETE /api/output/sessions/{id} → navigate to /
```

## Port Map (Local Dev)

| Service | Port |
|---------|------|
| PoMemeVideo.Api (HTTP) | 8000 |
| Azurite Blob | 10000 |
| Azurite Queue | 10001 |
| Azurite Table | 10002 |
| Ollama (Gemma 4) | 11434 |

## Patterns Reference

| Pattern | Where Applied |
|---------|---------------|
| GoF: Repository | `*TableRepository.cs` — all Azure Table Storage adapters |
| GoF: Command | `IngestVideoCommand`, `RunEngineCommand`, `RenderVideoCommand` |
| GoF: Observer | `EngineHubClient.cs` — SignalR event streams |
| GoF: Adapter | `AzureOpenAiVisionService`, `OllamaDirectorService` |
| GoF: Strategy | `TokenBucketTimingService` — timing algorithm |
| GoF: Template Method | `FFmpegRenderService` — filter_complex construction |
| SOLID: SRP | Each command, service, and repository owns one responsibility |
| SOLID: DIP | Application layer depends on Domain interfaces only |
| SOLID: OCP | New AI providers plug in via `IAiVisionService`/`IDirectorService` |
- `Ingestion/` — SAS token generation, session creation
- `Processing/` — Engine orchestration, AI dispatch
- `MemeLibrary/` — Sound catalogue query
- `Output/` — Render download, Wipe Buffer
- `Auth/` — ANON handler, OAuth routes
- `Config/` — Feature flags endpoint (`GET /api/config`)

## Key Technical Decisions

See `key-decisions.md` for full rationale.

| Component | Choice |
|-----------|--------|
| AI trigger detection | Azure OpenAI GPT-4o Vision (FR-007) |
| Director improvisation | Gemma 4 via Ollama (Hybrid Connection) |
| Sound matching | `System.Numerics.Tensors` SIMD cosine similarity |
| Audio/video render | FFmpeg in Docker (baked into image) |
| Real-time streaming | SignalR `EngineHub` |
| Local storage | Azurite in Docker (Blob 10000, Table 10002) |
| Auth | Microsoft OAuth (prod) + ANON handler (dev-only) |
