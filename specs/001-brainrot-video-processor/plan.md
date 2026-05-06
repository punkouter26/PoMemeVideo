# Implementation Plan: PoMemeVideo – Brainrot Video Processor

**Branch**: `001-brainrot-video-processor` | **Date**: 2026-05-05 | **Spec**: [spec.md](spec.md)  
**Input**: Feature specification from `/specs/001-brainrot-video-processor/spec.md`

## Summary

PoMemeVideo is a three-stage "Magic Button" web application that ingests a user-supplied video, applies AI-driven semantic trigger detection to build a meme soundtrack (Director's Script), renders the final video with original audio stripped and meme sounds embedded, and delivers a downloadable MP4 + JSON artefact — all within 60 seconds for videos up to 60 seconds long.

The architecture is a **cloud-hybrid** system: a Blazor WASM frontend performs client-side keyframe extraction and dithering, uploads media directly to Azure Blob Storage via SAS tokens, then coordinates with a .NET 10 ASP.NET Core API backend. The backend implements **Onion Architecture** with **Vertical Slice Architecture (VSA)** feature organisation. AI inference combines Azure AI Vision (temporal label extraction) with a local Gemma 4 model (via Ollama on MSI hardware, bridged through Azure App Service Hybrid Connections). Video rendering uses FFmpeg inside a custom Docker image. Real-time streaming to the client uses SignalR.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (pinned via `global.json`)  
**Primary Dependencies**: ASP.NET Core 10, Blazor WASM, SignalR, Azure OpenAI SDK (GPT-4o Vision for semantic trigger detection — FR-007), Ollama HTTP client (Gemma 4 for Director improvisation), Azure SDK (Blob, Table), FFmpeg (Docker-baked), System.Numerics.Tensors, Radzen Blazor, Serilog, OpenTelemetry, Playwright (E2E), xUnit + Testcontainers (integration)  
**Storage**: Azure Table Storage (session/job tracking, sound catalogue metadata) + Azure Blob Storage (video uploads, processed output, sound assets) — both in PoMemeVideo resource group; Azurite in Docker for local dev  
**Testing**: xUnit (unit + integration), Testcontainers/Azurite (integration), Playwright/TypeScript headed (E2E)  
**Target Platform**: Azure App Service Linux (custom Docker image) + Blazor WASM in browser (PC-first, landscape)  
**Project Type**: Cloud-hybrid web service + WASM SPA (3-stage wizard)  
**Performance Goals**: End-to-end processing ≤ 60 s for ≤ 60-second video; `/health` response ≤ 500 ms; Hardware Monitor updates ≥ 1/s; System Audit Box event latency ≤ 500 ms  
**Constraints**: Max video upload 500 MB; ≤ 43.8 min downtime/month (99.9% SLA); ANON collision rate < 1-in-1M; FFmpeg processes bounded via `System.Threading.Channels`; no secrets in `appsettings.json`  
**Scale/Scope**: ≥ 50 concurrent sessions without streaming degradation; 200+ pre-curated meme sound assets; 3 Blazor wizard pages; PC-first (mobile out of scope v1)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Verify the following gates against the PoMemeVideo Constitution (`.specify/memory/constitution.md`):

| # | Gate | Status |
|---|------|--------|
| I | Solution/project uses `PoMemeVideo` prefix; namespace matches; `global.json` pins .NET 10 | ✅ Planned — `PoMemeVideo.sln`, `global.json` with .NET 10 SDK in repo root |
| II | Onion Architecture layers (Domain/Application/Infrastructure) physically separated; Blazor WASM client; Radzen UI; C# 14; SOLID/GoF pattern comments present | ✅ Planned — VSA slices inside Onion shell; Radzen for data grids/forms; C# 14 targeted |
| III | `Directory.Build.props` has `TreatWarningsAsErrors` + `Nullable`; `PoShared.csproj` exists; server hosts WASM on 5000/5001; `wwwroot` only in client | ✅ Planned — root props files; `PoMemeVideo.Shared` project; fixed ports |
| IV | OpenAPI (Scalar) enabled; `.http` files provided; `/diag` + `/health` endpoints implemented; `F5` kills prior processes | ✅ Planned — Scalar middleware; `.http` files per slice; health/diag endpoints; VS Code launch task |
| V | All secrets in Azure Key Vault (PoShared); Managed Identity used; Table Storage in app's own RG; App Service Plan from PoShared RG | ✅ Planned — `DefaultAzureCredential`; SAS token generation server-side; Key Vault for all secrets |
| VI | ANON login button present (dev only); random suffix on ANON; email shown in navbar; Microsoft OAuth in dev+prod | ✅ Planned — ANON login path in `Development` env guard; Microsoft OAuth via MSAL |
| VII | Unit tests cover Domain/Application; Integration tests use Testcontainers; E2E via Playwright (headed in dev); Azurite in Docker for local storage | ✅ Planned — xUnit unit tests; Testcontainers + Azurite integration tests; Playwright E2E headed |
| VIII | Serilog → File+Console+AppInsights; OpenTelemetry enabled; logs include UserId/SessionId/Environment/CorrelationId/Exception; dev UI shows stack traces | ✅ Planned — Serilog sinks configured; OTEL global; structured log enrichers |
| IX | No dead code; comments only on complex logic + SOLID/GoF; feature flags in appSettings; `/LLMDOCS` maintained; ambiguity stop-rule applied | ✅ Planned — feature flags for AI/mock mode; `UseMockAI` toggle; `/LLMDOCS` folder |
| X | "MOCK DATA" banner shown when mock mode active; mock mode controlled by feature flag | ✅ Planned — `UseMockAI: true/false` in `appsettings.Development.json`; Blazor banner component |

**Constitution Check Result: PASSED** — all 10 gates have a compliant plan. No violations to justify.

## Project Structure

### Documentation (this feature)

```text
specs/001-brainrot-video-processor/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── api-endpoints.md
│   └── signalr-hub.md
└── tasks.md             # Phase 2 output (speckit.tasks command)
```

### Source Code (repository root)

```text
PoMemeVideo.sln
global.json                          # Pins .NET 10 SDK
Directory.Build.props                # TreatWarningsAsErrors, Nullable
Directory.Packages.props             # Central Package Management
.gitignore                           # .vs/, .vscode/, bin/, obj/, etc.
Dockerfile                           # Custom Linux image with FFmpeg baked in
.vscode/
  launch.json                        # F5: kill .NET → start server → open Edge
  tasks.json

src/
├── PoMemeVideo.Api/                 # ASP.NET Core host + VSA feature slices
│   ├── Features/
│   │   ├── Ingestion/               # Ingestion slice (SAS token, session create)
│   │   ├── Processing/              # Engine slice (AI orchestration, token-bucket)
│   │   ├── MemeLibrary/             # Sound catalogue slice (Table + Blob Storage)
│   │   ├── Rendering/               # FFmpeg render slice (Channels queue)
│   │   └── Output/                  # Reveal/download slice
│   ├── Hubs/
│   │   └── EngineHub.cs             # SignalR hub (Director's Log, Script, HW monitor)
│   ├── Pages/
│   │   └── Diag.cshtml              # /diag page (masked secrets, connection status)
│   ├── Endpoints/
│   │   └── HealthEndpoint.cs        # /health JSON endpoint
│   └── Program.cs
│
├── PoMemeVideo.Domain/              # Onion: Domain layer — entities, value objects, interfaces
│   ├── Entities/
│   │   ├── VideoSession.cs
│   │   ├── Keyframe.cs
│   │   ├── DirectorScript.cs
│   │   ├── ScriptEntry.cs
│   │   ├── SoundAsset.cs
│   │   └── UserIdentity.cs
│   ├── ValueObjects/
│   │   └── ActionVector.cs
│   └── Interfaces/
│       ├── IVideoSessionRepository.cs
│       ├── ISoundAssetRepository.cs
│       ├── IUserIdentityRepository.cs
│       ├── IAiVisionService.cs
│       ├── IDirectorService.cs
│       ├── IVideoRenderService.cs
│       └── IEngineNotifier.cs          # Decouples Application from SignalR (DI principle)
│
├── PoMemeVideo.Application/         # Onion: Application layer — use cases, orchestration
│   ├── Ingestion/
│   │   └── IngestVideoCommand.cs
│   ├── Processing/
│   │   ├── RunEngineCommand.cs
│   │   └── TokenBucketTimingService.cs
│   ├── MemeLibrary/
│   │   └── SemanticMatchingService.cs  # System.Numerics.Tensors cosine similarity
│   └── Rendering/
│       └── RenderVideoCommand.cs
│
├── PoMemeVideo.Infrastructure/      # Onion: Infrastructure layer — external I/O
│   ├── AzureStorage/
│   │   ├── VideoSessionTableRepository.cs
│   │   ├── SoundAssetTableRepository.cs
│   │   └── BlobStorageService.cs
│   ├── AzureOpenAi/
│   │   └── AzureOpenAiVisionService.cs  # GPT-4o Vision endpoint (FR-007)
│   ├── Ollama/
│   │   └── OllamaDirectorService.cs    # Gemma 4 via Hybrid Connection
│   ├── FFmpeg/
│   │   └── FFmpegRenderService.cs      # Bounded channels queue
│   └── Mock/
│       ├── MockAiVisionService.cs
│       └── MockDirectorService.cs
│
└── PoMemeVideo.Shared/              # PoShared: DTOs shared by client + server
    ├── Models/
    │   ├── VideoSessionDto.cs
    │   ├── ScriptEntryDto.cs
    │   ├── SoundAssetDto.cs
    │   └── UserIdentityDto.cs
    └── Enums/
        ├── SessionStatus.cs
        └── VisualEffectType.cs

src/client/
└── PoMemeVideo.Client/              # Blazor WASM project
    ├── Pages/
    │   ├── Source.razor             # Stage 1: drop zone, keyframe strip, toggle
    │   ├── Engine.razor             # Stage 2: Director's Log, Script, HW monitor, Audit Box
    │   └── Reveal.razor             # Stage 3: CRT player, JSON panel, downloads
    ├── Components/
    │   ├── AsciiDropZone.razor
    │   ├── DitheredKeyframeStrip.razor  # HTML5 Canvas + Floyd-Steinberg (JS interop)
    │   ├── CrtMonitorFrame.razor
    │   ├── ScanlineOverlay.razor
    │   ├── DirectorLogFeed.razor
    │   ├── DirectorScriptFeed.razor
    │   ├── HardwareMonitor.razor
    │   ├── SystemAuditBox.razor
    │   ├── MockDataBanner.razor
    │   └── NavBar.razor             # Shows email / ANON LOGGED IN
    ├── Services/
    │   ├── EngineHubClient.cs       # SignalR hub client
    │   └── BlobUploadService.cs     # Direct SAS upload to Blob Storage
    ├── wwwroot/
    │   ├── js/
    │   │   ├── canvas-dither.js     # Floyd-Steinberg dithering on HTML5 Canvas
    │   │   └── glitch-transition.js # System glitch transition animation
    │   └── css/
    │       └── retro-terminal.css   # Matrix Green, scanlines, CRT bulge, ASCII borders
    └── Program.cs

tests/
├── PoMemeVideo.UnitTests/           # xUnit — Domain + Application layer coverage
├── PoMemeVideo.IntegrationTests/    # xUnit + Testcontainers (Azurite, API endpoints)
└── PoMemeVideo.E2ETests/            # Playwright TypeScript — 3-stage wizard (headed in dev)

LLMDOCS/
├── architecture.md
├── api-surface.md
└── key-decisions.md
```

**Structure Decision**: Web application pattern (client + server) with Onion Architecture server and Blazor WASM client. VSA feature slices sit inside the Onion's Application + Infrastructure layers. `PoMemeVideo.Shared` is the constitutional PoShared project. The Docker image bakes in FFmpeg for server-side rendering.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|--------------------------------------|
| Ollama local model via Azure Hybrid Connection | Gemma 4 provides the core Director improvisation logic with lower per-token cost than pure Azure OpenAI for high-frequency token generation during engine runs | Pure Azure OpenAI GPT-4o would increase per-session API cost significantly at 200+ sound-matching decisions per video |
| Custom Docker image (FFmpeg baked in) | FFmpeg is required for audio replacement and deep-fry video filters; Azure App Service Linux does not provide FFmpeg natively | Using a third-party FFmpeg API service would introduce an additional external dependency, latency, and data egress cost |
| `System.Numerics.Tensors` SIMD cosine similarity | 200+ sound assets need sub-millisecond semantic matching at engine runtime without an additional ML inference round-trip | LINQ-based Euclidean distance over 200 records is fast enough at this scale but `Tensors` future-proofs for library growth and is already a constitutional-level design goal |
