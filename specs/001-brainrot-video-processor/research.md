# Research: PoMemeVideo – Brainrot Video Processor

**Phase**: 0 (Pre-design)  
**Date**: 2026-05-05  
**Status**: Complete — all unknowns resolved

---

## RES-001: Vertical Slice Architecture inside Onion Architecture

**Decision**: Organise `PoMemeVideo.Api` as VSA feature slices (Ingestion, Processing, MemeLibrary, Rendering, Output), where each slice owns its own command/query handler, validator, and endpoint registration. The Onion layer separation (`Domain` / `Application` / `Infrastructure`) is maintained across the solution by keeping entities and interfaces in `PoMemeVideo.Domain`, use-case orchestrators in `PoMemeVideo.Application`, and external I/O adapters in `PoMemeVideo.Infrastructure`. VSA slices in the API project import from the Application layer only.

**Rationale**: VSA provides feature cohesion (each slice is independently shippable) while Onion enforces the constitutional dependency rule (no outside-in references). This resolves the apparent tension between the two patterns.

**Alternatives considered**:
- Pure Onion with no VSA: Rejected — creates large flat folders that obscure feature boundaries in a multi-feature system.
- Pure VSA without Onion: Rejected — violates the constitutional mandate for strict layer separation.

---

## RES-002: Floyd-Steinberg Dithering on HTML5 Canvas (Client-Side)

**Decision**: Implement keyframe extraction and 1-bit green-scale dithering entirely in the browser using a `<canvas>` element and a JavaScript interop module (`canvas-dither.js`). The Blazor component (`DitheredKeyframeStrip.razor`) calls `IJSRuntime.InvokeVoidAsync` to trigger extraction and dithering at 3-second intervals from an `HTMLVideoElement`. The JS module reads pixel data via `canvas.getContext('2d').getImageData`, applies Floyd-Steinberg error diffusion to a single green channel, and writes back a 1-bit palette result.

**Rationale**: Client-side processing eliminates a server round-trip per keyframe, keeps the server memory footprint lean, and demonstrates the "local intelligence" philosophy stated in the brief. Floyd-Steinberg is the correct algorithm for the thermal-printer retro aesthetic described in the spec.

**Alternatives considered**:
- Server-side FFmpeg keyframe extraction + ImageSharp dithering: Rejected for preview step — introduces latency and memory pressure before the user has even confirmed their video.
- SkiaSharp WASM: Viable but adds ~10 MB to WASM bundle size for functionality achievable in ~80 lines of plain JS.

---

## RES-003: Direct-to-Blob Upload via SAS Tokens

**Decision**: The server generates a time-limited SAS token (Write permission, 15-minute expiry, limited to a single blob path: `sessions/{sessionId}/source.{ext}`) via `BlobServiceClient` with `DefaultAzureCredential`. The Blazor client uses the `Azure.Storage.Blobs` JS SDK (loaded from CDN) to upload the video file directly to Blob Storage, bypassing the API. After upload completes, the client calls `POST /api/ingestion/sessions` with the session ID and blob path to trigger server-side processing.

**Rationale**: Eliminates streaming a potentially 500 MB file through the API server, reducing memory pressure and preventing App Service request timeout issues. Managed Identity on the server generates the SAS, so no storage account key is ever exposed to the client.

**Alternatives considered**:
- Chunked multipart upload through API: Rejected — introduces unnecessary memory pressure and complicates the API surface.
- Presigned URL via Azure Function: Rejected — additional infrastructure not justified at this scale.

---

## RES-004: AI Hybrid — Azure AI Vision + Gemma 4 via Ollama + Hybrid Connection

**Decision**: Split AI workload into two tiers:
1. **Azure AI Vision** (`VideoIndexer` or `Analyze` endpoint): Sends the uploaded video (via Blob SAS URL) for temporal label extraction. Returns timestamped labels (e.g., `{t: 4.2, label: "Person falling"}`) as the raw trigger candidates.
2. **Gemma 4 (Ollama on MSI laptop, `localhost:11434`)**: Receives the label list as a structured prompt and returns the Director's Script JSON — sound pairings, visual effects, rationale, irony flags. The Azure App Service Hybrid Connection (`PoMemeVideo-HybridConn`) bridges the cloud API to `localhost:11434` securely without a VPN or public endpoint exposure.

**Mock path** (`UseMockAI: true`): `MockAiVisionService` returns a pre-baked label list; `MockDirectorService` returns a static Director's Script JSON.

**Rationale**: Azure AI Vision is optimised for temporal video analysis (scene segmentation, action detection) and runs fully managed. Gemma 4 handles the creative "improvisation" step — choosing which meme sound fits, deciding irony vs. accuracy — at local inference speed with no per-token Azure billing. Hybrid Connections provide a secure tunnel without requiring the MSI laptop to have a public IP.

**Alternatives considered**:
- Pure Azure OpenAI GPT-4o Vision for everything: Rejected — per-token cost scales badly when the Director needs to evaluate 200+ sounds per video run.
- Azure Container Apps with Ollama sidecar: Viable but adds infrastructure complexity; Hybrid Connection reuses the App Service plan already in PoShared RG.

---

## RES-005: System.Numerics.Tensors Cosine Similarity for Sound Matching

**Decision**: At API startup, load all 200+ `SoundAsset` records from Azure Table Storage into `IMemoryCache`. Represent each asset's `ActionVector` tag set as a normalised float vector (bag-of-words over a fixed 64-dimension vocabulary). Use `TensorPrimitives.CosineSimilarity` (SIMD-accelerated via hardware intrinsics) to score each candidate sound against the AI Vision label vector at engine time. Top-3 candidates are passed to Gemma 4 for final selection and rationale generation.

**Rationale**: 200 cosine similarity comparisons over 64-dimensional vectors completes in microseconds with SIMD. This pre-filters the candidate list before the more expensive LLM call, keeping the token budget low and the 60-second SLA intact.

**Alternatives considered**:
- Send all 200 sounds to Gemma 4 and let it choose: Rejected — token count per prompt would exceed Gemma 4's context window and blow the latency budget.
- Azure Cognitive Search vector index: Viable at larger scale but adds an additional Azure resource and round-trip latency for a 200-asset catalogue.

---

## RES-006: Token-Bucket Timing Algorithm

**Decision**: Implement `TokenBucketTimingService` in `PoMemeVideo.Application.Processing` as a stateful service scoped to a `VideoSession`. Rules:
- Minimum gap: 2,000 ms between consecutive `ScriptEntry` placements.
- Maximum gap: 10,000 ms (fallback fires if no trigger detected within the window).
- Average target: one event per 5,000 ms (soft target, not enforced per-event).
- Conflict resolution: when two triggers fall within 2 s, select the trigger with the higher cosine similarity score; log the rejected candidate to the System Audit Box with a "dice roll" rationale string.
- Fallback selection: pick the highest-scoring sound for the "ambient" action vector tag.

**Rationale**: The token-bucket constraints are directly specified in the feature spec (FR-011, FR-012, FR-013). Implementing as a dedicated service (Single Responsibility) makes the timing logic unit-testable in isolation.

---

## RES-007: FFmpeg Rendering with Bounded Channels

**Decision**: `FFmpegRenderService` in `Infrastructure.FFmpeg` executes FFmpeg via `System.Diagnostics.Process`. A `Channel<RenderJob>` (capacity: `Environment.ProcessorCount`) bounds concurrency. The FFmpeg command removes the original audio stream (`-an` or `-map 0:v`), mixes in meme sound files at their scheduled timestamps via `-filter_complex` `adelay` + `amix`, and optionally applies deep-fry (`eq`, `unsharp`, pixelation via `scale`+`scale` chain), snap-zoom (`zoompan`), motion blur (`minterpolate`), or overlay (`movie` filter + `overlay`) effects. Output is written to `sessions/{sessionId}/output.mp4` in Blob Storage.

**Rationale**: FFmpeg is the only practical cross-platform solution for audio timeline mixing and video filter application. Baking it into the Docker image removes the runtime installation dependency. Bounded channels protect App Service CPU from runaway concurrent renders.

**Alternatives considered**:
- Azure Media Services: Deprecated as of September 2023.
- HandBrake CLI: Lacks the precise `-filter_complex` audio timeline control needed for meme sound placement.

---

## RES-008: SignalR Hub Design

**Decision**: Single hub `EngineHub` with four server→client methods:
- `DirectorLogEntry(string message)` — human-readable reasoning line.
- `DirectorScriptEntry(ScriptEntryDto entry)` — JSON script entry as it is generated.
- `AuditEntry(string message)` — System Audit Box event (conflict resolutions, fallbacks).
- `HardwareMetrics(double inferenceLatencyMs, double cpuLoadPercent)` — emitted every 1 second during inference via a background timer.

Client subscribes to all four methods immediately on Engine page load, before signalling "Initiate" to the server. The server resolves the caller's connection ID from the `VideoSession.SessionId` (stored in `HubContext` group) to target messages to the correct client only.

**Rationale**: One hub with four strongly-typed methods is simpler to test and monitor than multiple hubs. Grouping by `SessionId` prevents cross-session message leakage.

---

## RES-009: Authentication — Microsoft OAuth + ANON (MSAL + custom middleware)

**Decision**: Use `Microsoft.Identity.Web` for Microsoft OAuth (app registration in `Punkouter26` tenant). In `Development` environment, register a custom `AnonAuthHandler` that intercepts the `ANON` login button click (a POST to `/auth/anon`), generates `ANON{Random.Shared.Next(100000, 999999)}`, creates a `ClaimsPrincipal` with `NameIdentifier` and `Email` claims, and writes a session cookie. The nav bar component reads `AuthenticationState` — if the name starts with `ANON`, display `ANON LOGGED IN`; otherwise display the email claim.

**Rationale**: `Microsoft.Identity.Web` is the canonical ASP.NET Core MSAL integration. The custom ANON handler avoids a full OIDC round-trip in dev without modifying any production code paths — it is excluded by an `if (env.IsDevelopment())` registration guard.

---

## RES-010: Retro Terminal CSS Strategy

**Decision**: Implement the full Matrix Green aesthetic as a CSS layer in `retro-terminal.css` (served from Blazor WASM `wwwroot/css/`). Key techniques:
- `font-family: 'Courier New', monospace` globally.
- Background: `#000`; primary text: `#00FF41`.
- Scanline overlay: CSS `repeating-linear-gradient` pseudo-element (`::after`) on `body`, with `animation: scanlines 8s linear infinite`.
- CRT spherical bulge: CSS `filter: url(#crt-bulge)` referencing an inline SVG `<feTurbulence>` + `<feDisplacementMap>` filter on the monitor frame component.
- ASCII double-line borders: Unicode box-drawing characters (`╔`, `═`, `╗`, `║`, `╚`, `╝`) in Blazor components, styled with monospace font.
- Glitch transition: JavaScript module (`glitch-transition.js`) applies rapid class toggling on the `<body>` for 1.2 seconds before the Reveal page renders.

**Rationale**: Pure CSS/SVG implementation keeps the WASM bundle lean and works across all modern browsers without additional JS libraries. The SVG displacement filter produces a convincing CRT barrel distortion without GPU shader requirements.
