---
description: "Task list for PoMemeVideo – Brainrot Video Processor"
---

# Tasks: PoMemeVideo – Brainrot Video Processor

**Input**: Design documents from `/specs/001-brainrot-video-processor/`  
**Prerequisites**: plan.md ✅ | spec.md ✅ | research.md ✅ | data-model.md ✅ | contracts/ ✅

**Organization**: Tasks grouped by user story. Each phase is an independently testable increment.

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no shared dependencies)
- **[Story]**: User story ownership (US1–US5)
- Exact file paths included in every task description

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Solution scaffold, build configuration, Docker, VS Code workflow

- [X] T001 Create `PoMemeVideo.sln` and all six `.csproj` files (`PoMemeVideo.Api`, `PoMemeVideo.Domain`, `PoMemeVideo.Application`, `PoMemeVideo.Infrastructure`, `PoMemeVideo.Shared`, `PoMemeVideo.Client`) with correct project references per `plan.md` dependency graph
- [X] T002 [P] Create `global.json` at repo root pinning .NET 10 SDK
- [X] T003 [P] Create `Directory.Build.props` at repo root with `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` and `<Nullable>enable</Nullable>`
- [X] T004 [P] Create `Directory.Packages.props` at repo root with Central Package Management entries for all NuGet packages (ASP.NET Core 10, SignalR, Azure SDK, Serilog, OpenTelemetry, Radzen, xUnit, Testcontainers, Playwright)
- [X] T005 [P] Create `.gitignore` covering `.vs/`, `.vscode/`, `bin/`, `obj/`, `*.user`, `appsettings.Development.json`, and all standard .NET + Node artefacts
- [X] T006 Create `Dockerfile` targeting Linux with `mcr.microsoft.com/dotnet/aspnet:10.0` base, FFmpeg installed via `apt-get`, and `PoMemeVideo.Api` as entry point
- [X] T007 [P] Create `.vscode/launch.json` and `.vscode/tasks.json` — F5 task kills existing .NET processes, starts server on `https://localhost:5001`, opens Edge
- [X] T008 [P] Create `LLMDOCS/` folder with placeholder `architecture.md`, `api-surface.md`, `key-decisions.md`
- [X] T009 [P] Add `docker-compose.yml` at repo root starting Azurite on ports 10000–10002

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain model, shared DTOs, logging, Azure storage base, auth scaffold, SignalR hub skeleton, `/health`, `/diag`, OpenAPI

**⚠️ CRITICAL**: No user story work begins until this phase is complete.

- [X] T010 Create all Domain entities in `src/PoMemeVideo.Domain/Entities/`: `VideoSession.cs`, `UserIdentity.cs`, `DirectorScript.cs`, `ScriptEntry.cs`, `SoundAsset.cs` — with properties matching `data-model.md`; include `// GoF: Entity` comment on each
- [X] T011 [P] Create Domain value objects in `src/PoMemeVideo.Domain/ValueObjects/`: `ActionVector.cs` (record with `Tags` array and `ToEmbedding(string[] vocabulary)` method); include `// GoF: Value Object pattern`; **do not** add `VisualEffectType` here — it lives in `PoMemeVideo.Shared/Enums/` to be accessible by both client and server without Domain coupling
- [X] T012 Create Domain interfaces in `src/PoMemeVideo.Domain/Interfaces/`: `IVideoSessionRepository.cs`, `ISoundAssetRepository.cs`, `IUserIdentityRepository.cs` (Create, GetById), `IAiVisionService.cs`, `IDirectorService.cs`, `IVideoRenderService.cs`, `IEngineNotifier.cs` (methods: `DirectorLogAsync`, `DirectorScriptAsync`, `AuditAsync`, `HardwareMetricsAsync`, `CompleteAsync`, `ErrorAsync` — all scoped to `sessionId`); include `// SOLID: Dependency Inversion — Application layer depends on IEngineNotifier, never on IHubContext<EngineHub>` in the interface file
- [X] T013 [P] Create all shared DTOs in `src/PoMemeVideo.Shared/Models/`: `VideoSessionDto.cs`, `ScriptEntryDto.cs`, `SoundAssetDto.cs`, `UserIdentityDto.cs`; and `src/PoMemeVideo.Shared/Enums/`: `SessionStatus.cs` (`Ingesting`, `Processing`, `Complete`, `Error`), `VisualEffectType.cs` (`None`, `DeepFry`, `SnapZoom`, `MotionBlur`, `Overlay`), `PlacementType.cs` (`Triggered`, `Fallback`, `Conflict-Winner`) — these are the single canonical definitions shared by Domain, Application, and Client
- [X] T014 Configure Serilog in `src/PoMemeVideo.Api/Program.cs` with File, Console, and AppInsights sinks; enrich all logs with `UserId`, `SessionId`, `Environment`, `CorrelationId`, and full `Exception` objects; enable dev-mode stack trace exposure via `UseDeveloperExceptionPage()`
- [X] T015 [P] Configure OpenTelemetry globally in `src/PoMemeVideo.Api/Program.cs` targeting PoShared App Insights instance; add tracing for ASP.NET Core, HttpClient, and SignalR
- [X] T016 Create `AzureTableClientFactory.cs` in `src/PoMemeVideo.Infrastructure/AzureStorage/` using `DefaultAzureCredential` for production and connection string for local Azurite; register as singleton
- [X] T017 [P] Create `BlobServiceClientFactory.cs` in `src/PoMemeVideo.Infrastructure/AzureStorage/` using `DefaultAzureCredential` in Azure and `UseDevelopmentStorage=true` locally; register as singleton
- [X] T017b Create `BlobStorageService.cs` in `src/PoMemeVideo.Infrastructure/AzureStorage/` — wraps `BlobServiceClient` with three operations: `StreamBlobAsync(string path)` → `Stream`, `ListBlobsByPrefixAsync(string prefix)` → `IAsyncEnumerable<string>`, `DeleteBlobsByPrefixAsync(string prefix)` → void; register as singleton; required by T063 (download video), T065 (Wipe Buffer delete); include `// SOLID: Single Responsibility — all blob I/O isolated here`
- [X] T018 Create `FeatureFlags.cs` config class and register `IOptions<FeatureFlags>` in `src/PoMemeVideo.Api/Program.cs`; add `"FeatureFlags": { "UseMockAI": false }` to `appsettings.json` and `appsettings.Development.json`
- [X] T019 Register `Microsoft.Identity.Web` and stub `AnonAuthHandler` (guarded by `env.IsDevelopment()`) in `src/PoMemeVideo.Api/Program.cs`; add `POST /auth/anon`, `GET /auth/login/microsoft`, `GET /auth/callback`, `POST /auth/logout` route registrations
- [X] T020 Scaffold `EngineHub.cs` in `src/PoMemeVideo.Api/Hubs/` with `JoinSession` and `LeaveSession` client→server methods and group management; map hub at `/hubs/engine` in `Program.cs`
- [X] T021 Implement `/health` endpoint in `src/PoMemeVideo.Api/Endpoints/HealthEndpoint.cs` returning JSON with status checks for Azure Table Storage, Blob Storage, Azure AI Vision, and Ollama Gemma 4 (returns `200 Healthy` or `503 Degraded`)
- [X] T022 [P] Implement `/diag` Razor page in `src/PoMemeVideo.Api/Pages/Diag.cshtml` displaying all external connection statuses and configuration key names; mask middle characters of sensitive values (pattern: first 3 + `***` + last 3)
- [X] T023 [P] Configure Scalar OpenAPI middleware in `src/PoMemeVideo.Api/Program.cs` at `/scalar`
- [X] T024 [P] Add `ConnectionStrings` and `AzureAiVision`, `AzureOpenAI`, `Ollama` sections to `appsettings.json` (empty values); populate `appsettings.Development.json` with `UseDevelopmentStorage=true` for Azurite
- [X] T024b Implement `GET /api/config` endpoint in `src/PoMemeVideo.Api/Features/Config/ConfigEndpoints.cs` — returns `{ "useMockAI": bool, "isDevelopment": bool }` from `IOptions<FeatureFlags>` and `IHostEnvironment`; consumed by `MockDataBanner.razor` and `Login.razor` to conditionally show banner/ANON button without polling session endpoints; no auth required

**Checkpoint**: Foundation complete — all five user stories can now be implemented.

---

## Phase 3: User Story 1 – Video Ingestion & Keyframe Preview (Priority: P1) 🎯 MVP

**Goal**: User drops a video → dithered green keyframe strip appears → Aggressive Visuals toggle visible → ready to Initiate.

**Independent Test**: Drop a test MP4; confirm dithered keyframe strip renders with correct frame count (⌊duration/3⌋), Matrix Green 1-bit palette, and Aggressive Visuals toggle responds.

- [X] T025 [US1] Implement `VideoSessionTableRepository.cs` in `src/PoMemeVideo.Infrastructure/AzureStorage/` implementing `IVideoSessionRepository` (Create, GetById, UpdateStatus, Delete); register in `Program.cs`
- [X] T026 [P] [US1] Implement `IngestVideoCommand.cs` in `src/PoMemeVideo.Application/Ingestion/` — validates file extension (mp4/mov/avi/webm) and size (≤500 MB), creates `VideoSession` entity, returns session ID; include `// SOLID: Single Responsibility — validation separated from persistence`
- [X] T027 [US1] Implement `POST /api/ingestion/sas` endpoint in `src/PoMemeVideo.Api/Features/Ingestion/IngestionEndpoints.cs` — calls `IngestVideoCommand`, generates time-limited SAS token (Write, 15-min, scoped to `sessions/{sessionId}/source.{ext}`); returns `{ sessionId, sasUrl, expiresAt }`
- [X] T028 [P] [US1] Implement `POST /api/ingestion/sessions` endpoint in `src/PoMemeVideo.Api/Features/Ingestion/IngestionEndpoints.cs` — accepts `{ sessionId, blobPath, videoDurationSeconds, aggressiveVisuals }`, updates session to `Ingesting` status, returns `201`
- [X] T029 [P] [US1] Implement `GET /api/ingestion/sessions/{sessionId}` endpoint in `src/PoMemeVideo.Api/Features/Ingestion/IngestionEndpoints.cs` — returns `VideoSessionDto` with current status
- [X] T030 [P] [US1] Create `canvas-dither.js` in `src/client/PoMemeVideo.Client/wwwroot/js/` implementing Floyd-Steinberg error-diffusion dithering on an `HTMLVideoElement` via `<canvas>` — reads pixel data, reduces to 1-bit green channel (`#00FF41`/`#000000`), returns array of base64 PNG data URLs at 3-second intervals
- [X] T031 [P] [US1] Implement `AsciiDropZone.razor` in `src/client/PoMemeVideo.Client/Components/` — renders ASCII double-line bordered drop zone, handles `ondragover`/`ondrop`, validates file client-side (extension + size ≤500 MB), displays retro-styled error message on failure, emits `OnFileAccepted` event with file reference
- [X] T032 [US1] Implement `BlobUploadService.cs` in `src/client/PoMemeVideo.Client/Services/` — accepts SAS URL and `IBrowserFile`, uploads directly to Azure Blob Storage with progress reporting, returns upload completion status
- [X] T033 [US1] Implement `DitheredKeyframeStrip.razor` in `src/client/PoMemeVideo.Client/Components/` — calls `canvas-dither.js` via `IJSRuntime.InvokeAsync`, renders horizontal strip of `<img>` elements from returned data URLs inside ASCII-bordered container
- [X] T034 [US1] Implement `Source.razor` in `src/client/PoMemeVideo.Client/Pages/` — orchestrates: `AsciiDropZone` → `BlobUploadService` (SAS from API) → `DitheredKeyframeStrip` → Aggressive Visuals toggle → "INITIATE" button navigating to `/engine/{sessionId}`
- [X] T035 [P] [US1] Write xUnit unit tests for `IngestVideoCommand` (valid extensions pass, `.exe` fails, 501 MB fails, 499 MB passes) in `tests/PoMemeVideo.UnitTests/Ingestion/`
- [X] T036 [P] [US1] Write xUnit integration tests for `POST /api/ingestion/sas` and `POST /api/ingestion/sessions` against Azurite via Testcontainers in `tests/PoMemeVideo.IntegrationTests/Ingestion/`
- [X] T037 [P] [US1] Create `Ingestion.http` in `src/PoMemeVideo.Api/Features/Ingestion/` with requests for SAS generation and session confirmation

---

## Phase 4: User Story 2 – AI-Directed Meme Sound & Visual Mapping (Priority: P2)

**Goal**: "INITIATE" pressed → Engine Page streams Director's Log + Director's Script in real time → SignalR delivers audit events and hardware metrics → Director's Script JSON built with token-bucket constraints respected.

**Independent Test**: Run engine on 30-second test video with `UseMockAI: true`; assert ≥3 `ScriptEntry` items, all timestamps respect 2s gap, ≥1 ironic pairing, all four SignalR feeds visible in UI.

- [ ] T038 [US2] Implement `SoundAssetTableRepository.cs` in `src/PoMemeVideo.Infrastructure/AzureStorage/` — loads all `SoundAsset` records from Table Storage into `IMemoryCache` at startup (`LoadAllAsync()`), 24-hour sliding cache refresh; register in `Program.cs`; include `// GoF: Repository Pattern`
- [ ] T039 [P] [US2] Implement `MockAiVisionService.cs` in `src/PoMemeVideo.Infrastructure/Mock/` implementing `IAiVisionService` — returns pre-baked list of timestamped action labels (e.g., `[{t: 4.2, label: "Person falling"}, {t: 8.1, label: "Confused expression"}]`)
- [ ] T040 [P] [US2] Implement `MockDirectorService.cs` in `src/PoMemeVideo.Infrastructure/Mock/` implementing `IDirectorService` — returns static `DirectorScript` JSON with 5 `ScriptEntry` items demonstrating Triggered, Fallback, and Conflict-Winner placement types
- [ ] T041 [US2] Implement `AzureOpenAiVisionService.cs` in `src/PoMemeVideo.Infrastructure/AzureOpenAi/` implementing `IAiVisionService` — uses `OpenAIClient` (Azure OpenAI SDK) with model `gpt-4o` (vision-capable); sends video keyframe images as base64-encoded `image_url` content parts in a chat completion request; system prompt instructs the model to return a JSON array of `{ "timestamp_seconds": number, "label": string }` objects identifying semantic trigger events (falls, surprise, confusion, sudden motion); parses and deserializes response into `(double TimestampSeconds, string Label)[]`; endpoint and key sourced from Key Vault (`PoMemeVideo-AzureOpenAI-Endpoint`, `PoMemeVideo-AzureOpenAI-Key`); `DefaultAzureCredential` used in Azure, API key fallback in local dev; include `// GoF: Adapter Pattern — wraps Azure OpenAI SDK to IAiVisionService domain interface`
- [ ] T042 [US2] Implement `OllamaDirectorService.cs` in `src/PoMemeVideo.Infrastructure/Ollama/` implementing `IDirectorService` — constructs structured prompt from AI Vision labels + top-3 sound candidates, sends to `http://localhost:11434/api/generate` (Gemma 4), parses returned JSON into `ScriptEntry[]` with rationale and isIronic fields; include `// GoF: Adapter Pattern`
- [ ] T043 [US2] Implement `SemanticMatchingService.cs` in `src/PoMemeVideo.Application/MemeLibrary/` — loads cached `SoundAsset` embedding vectors, uses `TensorPrimitives.CosineSimilarity` (SIMD) to score each sound against the action label vector, returns top-3 candidates; include `// SOLID: Single Responsibility — matching isolated from orchestration`
- [ ] T044 [US2] Implement `TokenBucketTimingService.cs` in `src/PoMemeVideo.Application/Processing/` — stateful per-session service enforcing 2,000 ms minimum gap, 10,000 ms maximum gap fallback, conflict resolution by cosine score, fallback sound selection; all decisions return `PlacementType` (Triggered/Fallback/Conflict-Winner) and `auditMessage` string; include `// GoF: Strategy Pattern — timing algorithm encapsulated`
- [ ] T045 [US2] Implement `RunEngineCommand.cs` in `src/PoMemeVideo.Application/Processing/` — orchestrates: update session → call `IAiVisionService` → for each label call `SemanticMatchingService` → apply `TokenBucketTimingService` → call `IDirectorService` for top-3 candidates → build `DirectorScript` → persist to Table Storage → stream each `ScriptEntry` via `IEngineNotifier` interface; include `// SOLID: Open/Closed — new AI providers plug in via IAiVisionService without modifying command`
- [ ] T046 [US2] Implement `POST /api/processing/sessions/{sessionId}/initiate` in `src/PoMemeVideo.Api/Features/Processing/ProcessingEndpoints.cs` — validates session is in `Ingesting` status, dispatches `RunEngineCommand` as background `Task`, returns `202 Accepted`; include `// GoF: Command Pattern`
- [ ] T047 [P] [US2] Implement `GET /api/memelibrary/sounds` in `src/PoMemeVideo.Api/Features/MemeLibrary/MemeLibraryEndpoints.cs` — supports `?tags=&limit=` query params, returns paginated `SoundAssetDto[]` from cache
- [ ] T048 [US2] Implement full `EngineHub.cs` in `src/PoMemeVideo.Api/Hubs/` — add all server→client methods: `DirectorLogEntry(string)`, `DirectorScriptEntry(ScriptEntryDto)`, `AuditEntry(string)`, `HardwareMetrics(double, double)`, `ProcessingComplete(string)`, `ProcessingError(string)`; implement `IEngineNotifier` interface backed by `IHubContext<EngineHub>`; emit `HardwareMetrics` every 1 second via `PeriodicTimer` during active inference
- [ ] T049 [US2] Implement `EngineHubClient.cs` in `src/client/PoMemeVideo.Client/Services/` — builds `HubConnection` to `/hubs/engine` with `.WithAutomaticReconnect(new[] {0,2000,5000,10000})`, registers all `On<>` handlers, exposes `JoinSessionAsync()`, `LeaveSessionAsync()`, and observable streams for each message type; include `// GoF: Observer Pattern`
- [ ] T050 [P] [US2] Implement `DirectorLogFeed.razor` in `src/client/PoMemeVideo.Client/Components/` — subscribes to `EngineHubClient` `DirectorLogEntry` stream, appends lines to an auto-scrolling `<pre>` terminal feed with Matrix Green text; max 500 buffered lines
- [ ] T051 [P] [US2] Implement `DirectorScriptFeed.razor` in `src/client/PoMemeVideo.Client/Components/` — subscribes to `DirectorScriptEntry` stream, renders each `ScriptEntryDto` as a syntax-highlighted JSON block typed out at rapid-fire speed via CSS animation; ASCII-bordered container
- [ ] T052 [P] [US2] Implement `SystemAuditBox.razor` in `src/client/PoMemeVideo.Client/Components/` — subscribes to `AuditEntry` stream, renders conflict resolution and fallback events in a fixed-height, scrollable console with ASCII border and Matrix Green `[CONFLICT]`/`[FALLBACK]` prefixes
- [ ] T053 [P] [US2] Implement `HardwareMonitor.razor` in `src/client/PoMemeVideo.Client/Components/` — subscribes to `HardwareMetrics` stream, displays inference latency (ms) and CPU load (%) as ASCII bar gauges updating ≥1/s
- [ ] T054 [US2] Implement `Engine.razor` in `src/client/PoMemeVideo.Client/Pages/` — four-panel layout: top-right `HardwareMonitor`, left `DirectorScriptFeed`, right `DirectorLogFeed`, bottom `SystemAuditBox`; calls `EngineHubClient.JoinSessionAsync()` on `OnInitializedAsync`, calls `POST /api/processing/{sessionId}/initiate`, navigates to `/reveal/{sessionId}` on `ProcessingComplete` event; shows `MockDataBanner` when `UseMockAI: true`
- [ ] T055 [P] [US2] Implement `MockDataBanner.razor` in `src/client/PoMemeVideo.Client/Components/` — calls `GET /api/config` (T024b) on `OnInitializedAsync`, shows prominent `⚠ MOCK DATA ⚠` banner when `useMockAI` is true; dismissed automatically when flag changes; shown on Engine and Reveal pages
- [ ] T056 [P] [US2] Write xUnit unit tests for `TokenBucketTimingService` in `tests/PoMemeVideo.UnitTests/Processing/`: min-gap enforcement, max-gap fallback trigger, conflict resolution picks highest-score candidate, `PlacementType` set correctly
- [ ] T057 [P] [US2] Write xUnit unit tests for `SemanticMatchingService` in `tests/PoMemeVideo.UnitTests/MemeLibrary/`: cosine similarity returns correct ranking, top-3 selection correct with known vectors
- [ ] T058 [P] [US2] Write xUnit integration tests for `POST /api/processing/*/initiate` with `MockAiVisionService` and `MockDirectorService` injected via Testcontainers in `tests/PoMemeVideo.IntegrationTests/Processing/` — asserts session transitions to `Processing` → `Complete`
- [ ] T059 [P] [US2] Create `Processing.http` and `MemeLibrary.http` files in `src/PoMemeVideo.Api/Features/Processing/` and `src/PoMemeVideo.Api/Features/MemeLibrary/`

---

## Phase 5: User Story 3 – Final Video Render & Download (Priority: P3)

**Goal**: Engine complete → glitch transition → Reveal Page shows CRT-framed video player with original audio stripped and meme soundtrack embedded → MP4 and JSON downloads work → Wipe Buffer resets to Source Page.

**Independent Test**: Process a test video end-to-end (mock AI); download MP4 → verify no original audio, meme sounds at correct timestamps; download JSON → verify matches on-screen script panel; click Wipe Buffer → Source Page in empty state.

- [ ] T060 [US3] Implement `FFmpegRenderService.cs` in `src/PoMemeVideo.Infrastructure/FFmpeg/` implementing `IVideoRenderService` — uses `System.Threading.Channels.Channel<RenderJob>` (capacity: `Environment.ProcessorCount`) to bound concurrency; builds `-filter_complex` string for: audio replacement (`-an` on input 0, `adelay`+`amix` for each `ScriptEntry` sound), deep-fry (`eq`+`unsharp`+`scale`), snap-zoom (`zoompan` 200–300%), motion blur (`minterpolate`), overlay (`movie`+`overlay`); outputs to `sessions/{sessionId}/output.mp4` in Blob Storage; include `// GoF: Template Method — filter_complex construction overridden per effect type`
- [ ] T061 [US3] Implement `RenderVideoCommand.cs` in `src/PoMemeVideo.Application/Rendering/` — queues `RenderJob` to `IVideoRenderService`, uploads output MP4 to Blob, updates `VideoSession.Status` to `Complete` with `OutputBlobPath`, signals `ProcessingComplete` via `IEngineNotifier`; include `// SOLID: Dependency Inversion — depends on IVideoRenderService abstraction`
- [ ] T062 [P] [US3] Implement `GET /api/output/sessions/{id}/script` in `src/PoMemeVideo.Api/Features/Output/OutputEndpoints.cs` — loads `DirectorScript` from Table Storage, returns `DirectorScriptDto` with full `entries[]`
- [ ] T063 [P] [US3] Implement `GET /api/output/sessions/{id}/download/video` in `src/PoMemeVideo.Api/Features/Output/OutputEndpoints.cs` — streams output MP4 from Blob Storage with `Content-Disposition: attachment; filename="pomemevideo-{sessionId}.mp4"`
- [ ] T064 [P] [US3] Implement `GET /api/output/sessions/{id}/download/script` in `src/PoMemeVideo.Api/Features/Output/OutputEndpoints.cs` — returns Director's Script as downloadable JSON file with `Content-Disposition: attachment; filename="director-script-{sessionId}.json"`
- [ ] T065 [P] [US3] Implement `DELETE /api/output/sessions/{id}` (Wipe Buffer) in `src/PoMemeVideo.Api/Features/Output/OutputEndpoints.cs` — deletes `VideoSession`, `DirectorScript` records from Table Storage, deletes all blobs under `sessions/{sessionId}/`, returns `204`
- [ ] T066 [P] [US3] Create `glitch-transition.js` in `src/client/PoMemeVideo.Client/wwwroot/js/` — exports `playGlitchTransition(onComplete)`: applies rapid class toggling on `<body>` for 1.2 seconds (flickering green text, screen flash), then calls `onComplete` callback to trigger Reveal page render
- [ ] T067 [US3] Implement `Reveal.razor` in `src/client/PoMemeVideo.Client/Pages/` — on load calls `glitch-transition.js` once, then renders: `CrtMonitorFrame` wrapping `<video>` player (autoplay), scrollable `DirectorScriptFeed` panel (readonly, pre-populated from `GET /api/output/*/script`), ASCII-styled MP4 download button (calls `GET /api/output/*/download/video`), ASCII-styled JSON download button, "WIPE BUFFER" button (calls `DELETE /api/output/*/sessions/{id}` then navigates to `/`); shows `MockDataBanner` when applicable
- [ ] T068 [P] [US3] Write xUnit unit tests for `RenderVideoCommand` in `tests/PoMemeVideo.UnitTests/Rendering/`: mock `IVideoRenderService` verifies `RenderJob` queued with correct `ScriptEntry` timestamps; session status transitions to `Complete`
- [ ] T069 [P] [US3] Write xUnit integration tests for all output endpoints against Azurite via Testcontainers in `tests/PoMemeVideo.IntegrationTests/Output/`: script retrieval matches seeded data, download headers correct, `DELETE` removes all records
- [ ] T070 [P] [US3] Create `Output.http` in `src/PoMemeVideo.Api/Features/Output/` with requests for script retrieval, video download, JSON download, and Wipe Buffer

---

## Phase 6: User Story 4 – Retro Terminal UI & Real-Time Engine Dashboard (Priority: P4)

**Goal**: All three wizard pages render consistently in Matrix Green retro-terminal aesthetic with scanlines, CRT bulge, ASCII borders, and monospaced font. Engine Page Hardware Monitor and Audit Box functional.

**Independent Test**: Load Source, Engine, Reveal pages; confirm Matrix Green (#00FF41) text on black background, monospaced font, visible scanline animation, double-line ASCII borders on all panels, Hardware Monitor updating ≥1/s during processing.

- [ ] T071 [US4] Implement `retro-terminal.css` in `src/client/PoMemeVideo.Client/wwwroot/css/` — full Matrix Green aesthetic: `font-family: 'Courier New', monospace`; background `#000`; primary text `#00FF41`; `@keyframes scanlines` with `repeating-linear-gradient` pseudo-element on `body::after`; CRT barrel bulge via inline SVG `<feTurbulence>` + `<feDisplacementMap>` filter referenced by `.crt-frame`; utility classes `.ascii-border-double` (Unicode box-drawing: `╔═╗║╚╝`), `.flicker`, `.cursor-blink`
- [ ] T072 [P] [US4] Implement `ScanlineOverlay.razor` in `src/client/PoMemeVideo.Client/Components/` — renders a fixed-position `<div>` with `scanlines` CSS class active on all pages; registered in `MainLayout.razor`
- [ ] T073 [P] [US4] Implement `CrtMonitorFrame.razor` in `src/client/PoMemeVideo.Client/Components/` — renders a `<div class="crt-frame">` wrapper applying the SVG displacement filter for spherical-bulge effect; `ChildContent` render fragment
- [ ] T074 [US4] Apply `retro-terminal.css` classes and `ScanlineOverlay`/`CrtMonitorFrame` components across `Source.razor`, `Engine.razor`, `Reveal.razor` — all panels use `.ascii-border-double`, all buttons use monospaced ASCII-art styling, `CrtMonitorFrame` wraps the video player on Reveal; verify no full-color artefacts remain
- [ ] T075 [P] [US4] Implement `NavBar.razor` in `src/client/PoMemeVideo.Client/Components/` — reads `AuthenticationState`, displays user email when Microsoft OAuth; displays `ANON LOGGED IN` when ANON identity; styled in retro-terminal CSS with ASCII horizontal rule separator; registered in `MainLayout.razor`

---

## Phase 7: User Story 5 – ANON Authentication & User Identity (Priority: P5)

**Goal**: ANON button creates unique `ANON{6-digit}` identity (dev only); Microsoft OAuth login shows email in nav bar; all session data attributed to correct identity.

**Independent Test**: Click ANON twice in separate private windows; verify unique suffixes; verify `ANON LOGGED IN` in nav bar; sign in with Microsoft OAuth; verify email in nav bar.

- [ ] T076 [US5] Implement `UserIdentityTableRepository.cs` in `src/PoMemeVideo.Infrastructure/AzureStorage/` implementing `IUserIdentityRepository` (Create, GetById); register in `Program.cs`; include `// GoF: Repository Pattern`
- [ ] T077 [US5] Implement `AnonAuthHandler.cs` in `src/PoMemeVideo.Api/Features/Auth/` — handles `POST /auth/anon` (dev-only route, guarded by `env.IsDevelopment()`): generates `ANON{Random.Shared.Next(100000, 999999)}`, creates `UserIdentity` record via repository, creates `ClaimsPrincipal` with `NameIdentifier` and `Email` claims, writes signed session cookie; include `// SOLID: Single Responsibility — ANON identity creation isolated`
- [ ] T078 [P] [US5] Configure `Microsoft.Identity.Web` full OAuth flow in `src/PoMemeVideo.Api/Program.cs`: app registration client ID/secret sourced from Key Vault (`PoMemeVideo-MicrosoftOAuth-ClientId`, `PoMemeVideo-MicrosoftOAuth-ClientSecret`); cookie auth scheme; `GET /auth/login/microsoft` redirect; `GET /auth/callback`; `POST /auth/logout` revoke
- [ ] T079 [US5] Create `Login.razor` in `src/client/PoMemeVideo.Client/Pages/` — renders Matrix Green ASCII-bordered login panel; calls `GET /api/config` (T024b) on load; shows "ANON" button only when `isDevelopment: true`; shows "SIGN IN WITH MICROSOFT" button always; both buttons route through their respective auth endpoints
- [ ] T080 [P] [US5] Write xUnit unit tests for ANON suffix uniqueness (10,000 iterations, assert collision rate < 0.001%) and `UserIdentity` domain invariants (ANON pattern `^ANON\d{6}$`, Microsoft must be valid email) in `tests/PoMemeVideo.UnitTests/Auth/`
- [ ] T081 [P] [US5] Write xUnit integration tests for `POST /auth/anon` in `tests/PoMemeVideo.IntegrationTests/Auth/`: response sets session cookie, `displayName` matches `ANON\d{6}` pattern, `UserIdentity` persisted to Azurite Table Storage
- [ ] T081b [P] [US5] Create `Auth.http` in `src/PoMemeVideo.Api/Features/Auth/` with requests for: `POST /auth/anon`, `GET /auth/login/microsoft`, `GET /auth/callback`, `POST /auth/logout`, `GET /api/config` — satisfies Constitution Principle IV requirement for `.http` files on all primary API functions

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: E2E test suite, sound library seeding, LLMDOCS, App Service health-check config, dev error transparency, final constitution review

- [ ] T082 Implement sound library seeding CLI in `src/PoMemeVideo.Api/` — `dotnet run -- seed-sounds` command imports 200+ `SoundAsset` records (metadata → Azurite Table Storage, audio files → Azurite Blob Storage) from a `seeds/sounds/` folder; idempotent (skip existing); logs progress to console
- [ ] T083 [P] Populate `LLMDOCS/architecture.md` (solution diagram, layer descriptions, VSA slice list), `LLMDOCS/api-surface.md` (all endpoint signatures), `LLMDOCS/key-decisions.md` (RES-001 through RES-010 from `research.md` as concise bullets)
- [ ] T084 [P] Write Playwright E2E tests for US1 in `tests/PoMemeVideo.E2ETests/`: drop valid MP4 → assert keyframe strip has correct count → assert Matrix Green palette → toggle Aggressive Visuals → assert state persists
- [ ] T085 [P] Write Playwright E2E tests for US2 in `tests/PoMemeVideo.E2ETests/`: click INITIATE → assert Engine Page loads → assert DirectorLogFeed scrolls → assert DirectorScriptFeed populates → assert HardwareMonitor updates → assert Audit Box visible
- [ ] T086 [P] Write Playwright E2E tests for US3 in `tests/PoMemeVideo.E2ETests/`: assert glitch transition plays → assert Reveal Page loads → click MP4 download → verify file downloaded → click JSON download → verify file downloaded → click Wipe Buffer → assert Source Page empty
- [ ] T087 [P] Write Playwright E2E tests for US5 in `tests/PoMemeVideo.E2ETests/`: click ANON → assert `ANON LOGGED IN` in nav bar → click ANON in second session → assert different suffix; click Microsoft OAuth → assert email in nav bar (use test Microsoft account)
- [ ] T088a [P] Write xUnit integration test asserting `GET /health` responds in ≤ 500 ms under normal conditions (SC-007) — use `Stopwatch` around `HttpClient.GetAsync`, assert `ElapsedMilliseconds < 500`; add to `tests/PoMemeVideo.IntegrationTests/Health/`
- [ ] T088b [P] Create k6 load test script `tests/k6/load-test.js` targeting `POST /api/processing/sessions/{id}/initiate` with 50 virtual users for 60 seconds (SC-005 — ≥50 concurrent sessions without streaming degradation); assert p95 response time < 1000 ms and zero 5xx errors; document run command in `LLMDOCS/architecture.md`
- [ ] T088c [P] Add Playwright E2E timing assertion for SC-001: from file drop completion to `ProcessingComplete` SignalR event ≤ 60 000 ms for a 60-second test video; add to `tests/PoMemeVideo.E2ETests/` as `E2ETimingTests.ts`
- [ ] T088 Add Azure App Service health-check configuration to `Dockerfile` (`HEALTHCHECK CMD curl -f https://localhost:5001/health || exit 1`) and document App Service platform health-check setup pointing at `/health` in `LLMDOCS/architecture.md` (FR-031)
- [ ] T089 [P] Configure `ProblemDetails` middleware in `src/PoMemeVideo.Api/Program.cs` — expose full stack traces and exception details in `Development` environment only; suppress in `Production`/`Staging`
- [ ] T090 [P] Final constitution compliance check — verify all 10 gates against the implemented code structure; update `LLMDOCS/` with any discovered gaps; update `plan.md` Constitution Check table with final ✅ status

---

## Dependencies

```
Phase 1 (Setup) → Phase 2 (Foundational)
Phase 2 → Phase 3 (US1) → Phase 4 (US2) → Phase 5 (US3)
Phase 2 → Phase 6 (US4) [can run in parallel with US1 CSS work]
Phase 2 → Phase 7 (US5) [can run in parallel with US1–US3]
Phase 3 + Phase 4 + Phase 5 → Phase 8 (Polish — E2E requires full pipeline)
```

## Parallel Execution Examples

**After Phase 2 completes**, the following can run concurrently:
- T025–T029 (US1 server) in parallel with T030–T033 (US1 client components)
- T039–T040 (Mock services) in parallel with T041–T042 (real AI services)
- T071 (CSS) in parallel with T038 (sound repo) in parallel with T076 (user identity repo)
- T056–T057 (unit tests) in parallel with T046–T047 (API endpoints)

## Implementation Strategy

**MVP Scope** (deliver first): Phase 1 + Phase 2 + Phase 3 (US1 complete)
- Proves ingestion pipeline works end-to-end
- Demonstrates the retro aesthetic in the drop zone
- Validates Azure storage connectivity

**Increment 2**: Phase 4 (US2) with `UseMockAI: true`
- Full Engine Page experience without real AI cost
- All SignalR streams verified

**Increment 3**: Phase 5 (US3)
- Complete downloadable artefact delivered

**Full release**: Phase 6 + Phase 7 + Phase 8 + real AI integration (`UseMockAI: false`)
