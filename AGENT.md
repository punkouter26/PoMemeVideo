# PoMemeVideo — AI Agent Context

> **Read this file first.** It is the single entry point for any AI coding agent working in this repository.

---

## 1. What This Project Does

**PoMemeVideo** is a web application that ingests a video, uses AI to generate a meme-style "director script," overlays synchronized sound assets, and renders the output via FFmpeg — all delivered through a Blazor WASM front-end hosted by an ASP.NET Core API.

Core user flow:
1. User uploads a video (Source page)
2. AI Vision analyses frames → AI Director generates a `DirectorScript` (scene + sound cues)
3. FFmpeg renders final video with audio overlays
4. User views results and downloads from Results/Reveal pages

---

## 2. Solution Structure

```
PoMemeVideo.slnx          ← Solution root (new .NET 10 .slnx XML format; tools that consume .sln must be updated)
global.json               ← Pins .NET 10 SDK
Directory.Build.props     ← Nullable + TreatWarningsAsErrors + MinVer versioning
Directory.Packages.props  ← Centralized Package Management (CPM)

src/
  PoMemeVideo.Api/            ← Single server project (VSA). Hosts Blazor WASM + all slices.
    Features/<Slice>/         ← One folder per slice: endpoint + command/handler + entity
                                + interface + repository + services co-located.
                                Slices: Admin, Auth, Config, Ingestion, MemeLibrary,
                                Output (rendering), Processing (AI director + providers).
    Common/                   ← Cross-cutting kernel: storage client factories,
                                GlobalExceptionHandler.
    Configuration/, Endpoints/, Hubs/, Pages/  ← host wiring, health, SignalR, Diag.
  PoMemeVideo.Shared/         ← Shared kernel. Referenced by both Api and Client.
    Domain/                   ← Cross-slice entities + strongly-typed ids
                                (SessionId/UserId/SoundId/EntryId).
    Contracts/                ← Cross-slice interfaces (repositories + services).
                                A slice reaches a sibling ONLY through these.
    Models/, Enums/           ← Wire DTOs + enums shared with the WASM client.
  PoMemeVideo.Client/         ← Blazor WASM front-end (hosted by Api on 7000/5001).

tests/
  PoMemeVideo.UnitTests/          ← xUnit, pure unit tests, no I/O
  PoMemeVideo.IntegrationTests/   ← Testcontainers (Azurite), real storage
                                Infrastructure/ — `[Collection("Integration")]`
                                with a `TestcontainersCleanupFixture` so any
                                leaked Testcontainer is reaped at collection
                                teardown (see scripts/cleanup-testcontainers.ps1)
  PoMemeVideo.E2EAPI/             ← C# pure-API emulation (Test env, GUEST bypass)
  PoMemeVideo.E2EUI/              ← C# Playwright browser tests (E2E_BASE_URL-driven)

scripts/                    ← setup.ps1 (winget/Docker/Azurite/az login + tool clones), seed/model helpers
```

**Vertical Slice Architecture:** the layer projects (Domain/Application/Infrastructure)
were collapsed into `PoMemeVideo.Api`. Code is organized by feature slice, not by
technical layer. Slice types live in `PoMemeVideo.Api.Features.<Slice>`; cross-cutting
types in `PoMemeVideo.Api.Common`.

**Slices are autonomous — they must not reference each other.** A slice depends only on
`PoMemeVideo.Shared.Contracts` and resolves the implementation from DI. `GlobalUsings.cs`
deliberately imports *only* `Api.Common` + the `Shared` kernel; it does **not** import sibling
slice namespaces. The composition root (`Configuration/`) is the one place allowed to see every
concrete slice type, and it imports them explicitly.

Cross-cutting helpers that every slice needs (e.g. `UserIdentityResolution`, which answers
"who is calling?") live in `Api.Common`, not in a slice.

Three projects only: **Api / Client / Shared.**

---

## 3. Key Domain Entities (`PoMemeVideo.Shared.Domain`)

| Entity | Purpose |
|---|---|
| `VideoSession` | Lifecycle of one render job (Ingesting → Rendering → Complete) |
| `UserIdentity` | Authenticated user record (MS OAuth or GUEST######) |
| `DirectorScript` | AI-generated script: list of `ScriptEntry` cue objects |
| `ScriptEntry` | Single scene cue: timestamp, sound asset reference, text overlay |
| `SoundAsset` | Metadata for a meme sound effect stored in Blob Storage |

**Identifiers are strongly typed** (`readonly record struct`): `SessionId`, `UserId`, `SoundId`,
`EntryId`. Each serialises as a bare GUID string and implements `IParsable<T>`, so the HTTP wire
format and the Table Storage PartitionKey/RowKey are byte-identical to a raw `Guid` — but
transposing `(sessionId, userId)` is now a compile error instead of a silent null. Wire DTOs in
`Shared/Models` intentionally keep raw `Guid`: strong types inside, primitives at the edges.

---

## 4. AI Provider Switching (`Api/Features/Processing/`)

The app supports three AI back-ends selected at runtime via `RuntimeAiSettings`, plus a mock:

| Provider | Class | When used |
|---|---|---|
| **Browser LLM** | `BrowserLLMDirectorService` | WebGPU-capable browser; Development default |
| **AI Foundry** | `AiFoundryDirectorService` | Azure AI Foundry endpoint; Production default |
| **Azure OpenAI** | `AzureOpenAiDirectorService` | Azure OpenAI resource |
| **Mock** | `MockDirectorService` (IMockable) | Tests / CI |

`SwitchingDirectorService` dispatches to the correct provider, defaulting to AI Foundry for any
unrecognised `Provider` value — the setting is runtime-mutable via `PUT /api/config/ai-model`, so
an unknown value must still render a video rather than throw.
When **any mock** is active the top nav must display **"USING MOCK DATA"**.

**Browser LLM round-trip.** The server serialises the inference payload, pushes it to the browser
over SignalR, and awaits a `TaskCompletionSource` keyed by session id; the anonymous
`POST /api/processing/sessions/{id}/browser-director-result` endpoint resolves it. The wait times
out at 90 s, and `RunEngineCommand` degrades to deterministic fallback entries rather than failing
the session. Weights live under `MODEL/<model-id>/` — `python scripts/download-models.py`.

**Removed provider.** `Ollama` was deleted: it required a daemon on `localhost:11434` that
production does not have. It is absent from `RuntimeAiSettings.ValidProviders`, so a persisted
settings file written by an older build cannot re-enable it, and `SwitchingDirectorService`
routes it to the AI Foundry fallback.

---

## 5. Storage Strategy

**Contract: Azure Table Storage** (classic `Microsoft.Storage` Table API — *not* Cosmos DB
Table API). The zero-waste choice: cheap, serverless, sufficient for session/script/sound
metadata. Account: **`stpomemevideo`** (lowercase, Po-scoped). Because the account is
dedicated to this app, tables are unprefixed (`VideoSessions`, `DirectorScripts`,
`SoundAssets`, `UserIdentities`) — the **account name is the `Po{Solution}` identity
boundary**, so per-table prefixes would be redundant.

| Environment | Table Storage | Blob Storage |
|---|---|---|
| Local (Docker) | Azurite `UseDevelopmentStorage=true` | Azurite |
| Azure Prod | `stpomemevideo` (connection string from Key Vault) | `stpomemevideo` |

`AzureTableClientFactory` and `BlobServiceClientFactory` read `ConnectionStrings:AzureTableStorage` / `AzureBlobStorage` and switch automatically.

**Data Protection** keys persist to the `dataprotection` blob container (prod) so the BFF's
encrypted auth cookies survive container restarts/redeploys.

**Identity:** the app uses a **system-assigned managed identity** with `get`/`list` on
`kv-poshared` secrets. (No user-assigned identity / `AZURE_CLIENT_ID`.)

Table repositories: `VideoSessionTableRepository`, `UserIdentityTableRepository`, `SoundAssetTableRepository`, `DirectorScriptTableRepository`

---

## 6. Authentication

- **Microsoft OAuth** (OIDC via `Microsoft.Identity.Web`) — both Dev and Prod
- **GUEST mode** (Dev/Test only):
  - Format: `GUEST` + 8 random digits (e.g. `GUEST35367543`)
  - Persisted in the auth **cookie** issued by `SignInAsync` (survives refresh, used by E2E tests)
  - Button is **hidden and disabled** in Production (`ASPNETCORE_ENVIRONMENT != Development`)
- Nav bar always shows authenticated email/name + **LOG OUT** button on the right
- No AOT — build is standard Blazor WASM
- **Forced login:** the client `App.razor` wraps routing in `CascadingAuthenticationState` + `AuthorizeRouteView`; `_Imports.razor` applies a global `[Authorize]`, and `Login.razor` is `[AllowAnonymous]`. Anonymous users are redirected to `/login`. Auth state is sourced from `ApiAuthenticationStateProvider` (queries `/api/auth/me`).
- **GUEST bypass** is mapped in **Development and Test** environments only; registering it in Production throws `InvalidOperationException`.
- **Deny-by-default:** `AddAuthorization` sets a `FallbackPolicy` requiring an authenticated user.
  Any endpoint without explicit authorization metadata is protected, so forgetting
  `RequireAuthorization()` no longer silently leaves an endpoint open. Endpoints that must stay
  public opt out with `AllowAnonymous()`: `/health`, `/health/live`, `/diag`, `/api/config`,
  the `/auth/*` family, the sound-stream endpoint, `/hubs/engine`, and the WASM shell
  (`MapStaticAssets`, `MapFallbackToFile`). Get the live list with
  `grep -rn AllowAnonymous src/PoMemeVideo.Api`.
  **When adding an endpoint that must be public, you must say so explicitly.**
- **`FakeAuthHandler`** (`Features/Auth`) authenticates from `X-Fake-User` / `X-Fake-Roles` for
  integration and E2E suites. Registered outside Production only, and its constructor throws
  `InvalidOperationException` if it is ever built in Production.
- Entra ID uses the `/common` endpoint (`AzureAd:TenantId = "common"`) for multi-tenant sign-in.

---

## 7. API Surface

- **Ports:** HTTP `7000`, HTTPS `5001` (fixed, see `launchSettings.json`)
- **OpenAPI:** Scalar UI at `/scalar`
- **Health check:** `GET /health` → JSON
- **Diagnostics:** `GET /diag` → masked keys + connection status (dev + prod, hidden from nav)
- **SignalR:** engine progress hub at `/hubs/engine` (`EngineHub`, `AllowAnonymous`)
- **Endpoints folder:** `src/PoMemeVideo.Api/Endpoints/`
- HTTP test file: `PoMemeVideo.Api.http`

---

## 8. Configuration & Secrets

Priority (highest → lowest):
1. Azure Key Vault (`PoMemeVideo--` prefixed secrets, via `PrefixKeyVaultSecretManager`)
2. `appsettings.Development.json` (local dev overrides, gitignored)
3. `appsettings.json` (safe defaults, all empty strings)
4. Environment variables

Key Vault naming: `PoMemeVideo--AzureOpenAI--Key` maps to `AzureOpenAI:Key`.  
Shared secrets (no prefix): `AzureAd--TenantId` → `AzureAd:TenantId`.

---

## 9. Observability

**Correlation:** `X-Session-ID` and `X-Correlation-ID` are propagated end to end. The WASM client
stamps them (`CorrelationHeaderHandler`), the API enriches Serilog with them and echoes
`X-Correlation-ID` on every response, and `CorrelationPropagationHandler` forwards them on outbound
HTTP (AI Foundry, Azure OpenAI). High-frequency paths use `[LoggerMessage]` source generators.

- **Serilog:** Console always; rolling File in Development only (App Service already captures
  stdout, and 30 days of retained logs ate into the F1 plan's 1 GB quota); Application Insights
  when `ApplicationInsights:ConnectionString` is set.
- **Enrichers:** `UserId`, `SessionId`, `CorrelationId` on every log entry
- **OpenTelemetry:** tracing is registered **only when an exporter will actually receive the
  spans** — an explicit `OpenTelemetry:Endpoint`, or the Aspire dashboard default in Development.
  Registering it unconditionally meant prod sampled, allocated and dropped every span, which is
  real CPU against a 60 CPU-min/day quota for no telemetry.
- **Activity sources:** `PoMemeVideo.*`

There is no `Microsoft.ApplicationInsights.AspNetCore` package reference: it was never registered
(`AddApplicationInsightsTelemetry` appears nowhere), so it shipped bytes for nothing. Application
Insights is fed through the Serilog sink alone.

---

## 10. Front-End (Blazor WASM)

- Hosted by `PoMemeVideo.Api` (same origin, ports 7000/5001)
- UI: **Native retro-terminal controls** — no external component library
- Pages: `Login`, `Source`, `Engine`, `Results`, `Reveal`, `MemeLibrary`, `NotFound`
- AI model selector on the Source page — grouped **Remote · AI Foundry** deployments and **Browser · WebGPU** local models
- Mobile-first: CSS `clamp()` + `auto-fit`, left-aligned top nav bar
- Web Audio API for sound previews
- **No inline styles.** Component styling lives in scoped `*.razor.css`; design tokens are CSS
  custom properties in `wwwroot/css/retro-terminal.tokens.css`. `index.html` must keep the
  `PoMemeVideo.Client.styles.css` link — without it every scoped rule silently does nothing.
  Utility classes that get splatted onto child components (e.g. `.file-input-full` on `<InputFile>`)
  live in the global `retro-terminal.components.css`, because a child component's rendered
  element does not carry the parent's scope id.
- Long lists use `<Virtualize>` (the sound library grows with every seed run).

---

## 11. Testing

| Layer | Stack | Notes |
|---|---|---|
| Unit | xUnit, no I/O | Uses mock/stub AI, no containers |
| Integration | xUnit + Testcontainers (Azurite) | Real table/blob storage; `test` env config. Containers are cleaned at collection teardown via `TestcontainersCleanupFixture` (see §13). |
| API E2E | xUnit + `WebApplicationFactory` | `tests/PoMemeVideo.E2EAPI/`; full HTTP stack against Azurite |
| UI E2E | xUnit + `Microsoft.Playwright` (C#, not TypeScript) | `tests/PoMemeVideo.E2EUI/`; drives an **already-running** instance. Every test self-skips unless `E2E_BASE_URL` is set (`HEADED=1` for a visible browser). |

**AI test interception:** in Test environments (or when `UseMockAI` is set) `AiInterception`
routes the Azure OpenAI SDK through `AiInterceptionHandler`, a `DelegatingHandler` that answers
chat-completion calls locally. The real client, serialisation and retry path still execute, but no
tokens are spent. It is hard-disabled in Production.

**Rule:** Integration and E2E tests use `test` environment (non-AI mock data).  
Local dev uses `dev` environment (real AI calls).

---

## 12. Coding Conventions

- C# 14, `LangVersion: preview`
- **Strongly-typed ids over `Guid`** — see §3. Wire DTOs keep raw `Guid`.
- **No magic strings for storage**: table/container names come from `StorageNames`.
- All warnings are errors (`<TreatWarningsAsErrors>true`)
- Nullable reference types enabled everywhere
- SOLID + GoF patterns; use `// GoF: <Pattern>` comment at class level
- Use `<remarks>` XML tags to explain **why** a pattern was chosen
- Zero-waste policy: delete unused files immediately; no dead code
- `<PackageReference>` versions managed exclusively in `Directory.Packages.props`
- Assembly versioning via **MinVer** (git tags)

---

## 13. Scripts & Tooling (`scripts/`)

| Script | Purpose |
|---|---|
| `setup.ps1` | Bootstrap new machine: Winget, Docker, `az login` check |
| `setup-new-machine.py` | Python alternative bootstrap |
| `seed-meme-sounds.py` | Populate Azurite/Azure with sound assets |
| `download-models.py` | Pull ONNX weights for the BrowserLLM provider into `MODEL/` |
| `check-azurite.py` | Verify local Azurite connectivity |
| `cleanup-testcontainers.ps1` | Idempotent — removes any Docker container matching `*-test-*-{16-32hex}` (Testcontainers' default name pattern). Preserves `pomemevideo-azurite` (dev compose). Wire into `dotnet test` pre/post or let `TestcontainersCleanupFixture` invoke it at collection teardown. |

---

## 14. Azure Deployment (current truth)

- **App RG:** `PoMemeVideo` (web app + storage); shared **plan RG** `PoShared`.
- **Plan:** `asp-PoShared-f1` (**F1 Linux**, free tier in **westus2**). Renders count against a
  hard **60 CPU-min/day** quota — once it trips, the app returns 403 until UTC midnight. There is
  no Always On (cold start after ~20 min idle) and 1 GB storage. The deploy workflow ships a
  **gpl** static ffmpeg in `publish/ffmpeg/` so the app can encode without a system install. It
  tracks BtbN's rolling `n8.1-latest` build rather than a dated `autobuild-*` tag: those tags are
  pruned after about two weeks, and a pin to one is a deploy with an expiry date (it duly expired
  and took every deploy down until 2026-09-03). A rolling tag admits no SHA-256 pin, so the
  workflow verifies the binary behaviourally instead — libx264 present, plus a real one-frame
  h264 encode read back with ffprobe.
- **Identity:** system-assigned MI with get/list on `kv-poshared` (no UAMI, no `AZURE_CLIENT_ID`).
- **Secrets vault:** `kv-poshared` (access-policy, not RBAC); app-specific prefix `PoMemeVideo--`.
- **CI/CD:** `deploy-pomemevideo.yml` publishes framework-dependent, bundles ffmpeg, ZIP-deploys
  via `az webapp deploy`. No tests run in the deploy workflow (by rule) — tests live in `ci.yml`,
  which is split in two tiers: build + test-budget + **unit tests** on every push and PR, and the
  integration / API-E2E / UI-E2E jobs `workflow_dispatch`-only (each needs an Azurite service
  container, and the UI one a published app and a browser). Run CI manually for the full suite
  after touching storage, auth or the render pipeline. `azure.yaml` is a legacy azd manifest;
  there is no `infra/` Bicep dir. **Do not** set `WEBSITE_RUN_FROM_PACKAGE=1` — the read-only mount
  blocks the ffmpeg bit-fixup at startup.
- **Bumping back to B1:** the `Dockerfile` is still valid and still installs ffmpeg; restore the
  container deploy, flip `linuxFxVersion` to `DOCKER|…`, re-add the `DOCKER_REGISTRY_SERVER_*`
  app settings.

---

## 15. What AI Agents Should NOT Do

- Do **not** enable AOT compilation (`PublishAot = true`)
- Do **not** add `wwwroot` to any project other than `PoMemeVideo.Client`
- Do **not** hardcode connection strings — always use config/Key Vault
- Do **not** add navigation links to `/health` or `/diag`
- Do **not** show the GUEST login button when `ASPNETCORE_ENVIRONMENT == Production`
- Do **not** reintroduce technical-layer projects/folders — keep code in feature slices (VSA)
- Do **not** add NuGet package versions directly in `.csproj` files (use CPM)

---

## 16. Relevant Skills Available

These global Copilot skills can be invoked by name:

| Phase | Skill | Trigger phrase |
|---|---|---|
| Day 1 | `acquire-codebase-knowledge` | "map this codebase" |
| Design | `architecture-blueprint-generator` | "design the architecture" |
| Design | `folder-structure-blueprint-generator` | "plan the folder structure" |
| Build | `dotnet-best-practices` | "review this C# class" |
| Build | `dotnet-design-pattern-review` | "review this service" |
| Build | `autoresearch` | "optimize [measurable thing]" |
| Security | `security-review` | "security audit" / "OWASP scan" |
| Deploy | `appinsights-instrumentation` | "wire up telemetry" |
| Deploy | `azure-deployment-preflight` | "pre-flight check before azd up" |
| Operate | `azure-resource-health-diagnose` | "something broke in Azure" |
| Docs | `create-readme` | "write the README" |
| Docs | `repo-story-time` | "summarize this release" |
