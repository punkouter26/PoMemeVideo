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
  PoMemeVideo.Domain/         ← Entities, interfaces, value objects (no dependencies)
  PoMemeVideo.Application/    ← Use-cases: Ingestion, Processing, Rendering, MemeLibrary
  PoMemeVideo.Infrastructure/ ← Azure Storage, AI providers, FFmpeg, Mock services
  PoMemeVideo.Api/            ← ASP.NET Core host; serves Blazor WASM; all endpoints
  PoMemeVideo.Shared/         ← DTOs + enums shared between WASM and API
  client/PoMemeVideo.Client/  ← Blazor WASM front-end (hosted by Api on 5000/5001)

tests/
  PoMemeVideo.UnitTests/          ← xUnit, pure unit tests, no I/O
  PoMemeVideo.IntegrationTests/   ← Testcontainers (Azurite), real storage
  PoMemeVideo.E2ETests/           ← Playwright (TypeScript)

SCRIPTS/                    ← setup.ps1, seed scripts, model download helpers
infra/                      ← Azure Bicep / azd deployment
```

**Onion Architecture rule:** dependencies point inward only.
`Api → Application → Domain`. `Infrastructure → Application/Domain`. `Shared` is referenced by all.

---

## 3. Key Domain Entities (`PoMemeVideo.Domain`)

| Entity | Purpose |
|---|---|
| `VideoSession` | Lifecycle of one render job (Ingesting → Rendering → Complete) |
| `UserIdentity` | Authenticated user record (MS OAuth or GUEST######) |
| `DirectorScript` | AI-generated script: list of `ScriptEntry` cue objects |
| `ScriptEntry` | Single scene cue: timestamp, sound asset reference, text overlay |
| `SoundAsset` | Metadata for a meme sound effect stored in Blob Storage |

---

## 4. AI Provider Switching (`PoMemeVideo.Infrastructure`)

The app supports three AI back-ends selected at runtime via `RuntimeAiSettings`:

| Provider | Class | When used |
|---|---|---|
| **Azure OpenAI** | `AzureOpenAiDirectorService` | Default cloud path |
| **AI Foundry** | `AiFoundryDirectorService` | Azure AI Foundry endpoint |
| **Ollama** | `OllamaDirectorService` | Local only (`localhost:11434`) |
| **Browser LLM** | `BrowserLlmDirectorService` | WebGPU-capable browser |
| **Mock** | `MockDirectorService` (IMockable) | Tests / CI |

`SwitchingDirectorService` dispatches to the correct provider.  
When **any mock** is active the top nav must display **"USING MOCK DATA"**.

---

## 5. Storage Strategy

| Environment | Table Storage | Blob Storage |
|---|---|---|
| Local (Docker) | Azurite `UseDevelopmentStorage=true` | Azurite |
| Azure Prod | Azure Table Storage (Key Vault connection) | Azure Blob Storage |

`AzureTableClientFactory` and `BlobServiceClientFactory` read `ConnectionStrings:AzureTableStorage` / `AzureBlobStorage` and switch automatically.

Table repositories: `VideoSessionTableRepository`, `UserIdentityTableRepository`, `SoundAssetTableRepository`, `DirectorScriptTableRepository`

---

## 6. Authentication

- **Microsoft OAuth** (OIDC via `Microsoft.Identity.Web`) — both Dev and Prod
- **GUEST mode** (Dev/Test only):
  - Format: `GUEST` + 8 random digits (e.g. `GUEST35367543`)
  - Persisted in `LocalStorage` (survives refresh, used by E2E tests)
  - Button is **hidden and disabled** in Production (`ASPNETCORE_ENVIRONMENT != Development`)
- Nav bar always shows authenticated email/name + **LOG OUT** button on the right
- No AOT — build is standard Blazor WASM

---

## 7. API Surface

- **Ports:** HTTP `5000`, HTTPS `5001` (fixed, see `launchSettings.json`)
- **OpenAPI:** Scalar UI at `/scalar`
- **Health check:** `GET /health` → JSON
- **Diagnostics:** `GET /diag` → masked keys + connection status (dev + prod, hidden from nav)
- **SignalR:** render progress hub at `/hubs/render`
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

- **Serilog:** Console + File + Application Insights sinks
- **Enrichers:** `UserId`, `SessionId`, `CorrelationId` on every log entry
- **OpenTelemetry:** traces exported to Application Insights in PoShared resource group
- **Activity sources:** `PoMemeVideo.*`

---

## 10. Front-End (Blazor WASM)

- Hosted by `PoMemeVideo.Api` (same origin, ports 5000/5001)
- UI library: **Radzen** for complex controls
- Pages: `Login`, `Source`, `Engine`, `Results`, `Reveal`, `MemeLibrary`, `NotFound`
- AI model selector on Home/Engine page — grouped by: **Remote** (Azure OpenAI / AI Foundry) | **Browser** (WebGPU) | **Local** (Ollama)
- Mobile-first: CSS `clamp()` + `auto-fit`, left-aligned top nav bar
- Web Audio API for sound previews

---

## 11. Testing

| Layer | Stack | Notes |
|---|---|---|
| Unit | xUnit, no I/O | Uses mock/stub AI, no containers |
| Integration | xUnit + Testcontainers (Azurite) | Real table/blob storage; `test` env config |
| E2E | Playwright (TypeScript) | Uses GUEST auth; `test` env; run `npm test` from `tests/PoMemeVideo.E2ETests/` |

**Rule:** Integration and E2E tests use `test` environment (non-AI mock data).  
Local dev uses `dev` environment (real AI calls).

---

## 12. Coding Conventions

- C# 14, `LangVersion: preview`
- All warnings are errors (`<TreatWarningsAsErrors>true`)
- Nullable reference types enabled everywhere
- SOLID + GoF patterns; use `// GoF: <Pattern>` comment at class level
- Use `<remarks>` XML tags to explain **why** a pattern was chosen
- Zero-waste policy: delete unused files immediately; no dead code
- `<PackageReference>` versions managed exclusively in `Directory.Packages.props`
- Assembly versioning via **MinVer** (git tags)

---

## 13. Scripts & Tooling (`SCRIPTS/`)

| Script | Purpose |
|---|---|
| `setup.ps1` | Bootstrap new machine: Winget, Docker, `az login` check |
| `setup-new-machine.py` | Python alternative bootstrap |
| `seed-meme-sounds.py` | Populate Azurite/Azure with sound assets |
| `download-models.py` | Pull Ollama models |
| `check-azurite.py` | Verify local Azurite connectivity |

---

## 14. Azure Deployment

- **Resource group:** `Punkouter26` (shared: `PoShared`)
- **Identity:** Managed Identity for Key Vault access
- **IaC:** Bicep under `infra/`; deploy with `azd up`
- **Fallback App Service Plan:** check `PoShared` resource group if default plan fails
- `azure.yaml` is the azd manifest

---

## 15. What AI Agents Should NOT Do

- Do **not** enable AOT compilation (`PublishAot = true`)
- Do **not** add `wwwroot` to any project other than `PoMemeVideo.Client`
- Do **not** hardcode connection strings — always use config/Key Vault
- Do **not** add navigation links to `/health` or `/diag`
- Do **not** show the GUEST login button when `ASPNETCORE_ENVIRONMENT == Production`
- Do **not** break the Onion Architecture dependency direction
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
