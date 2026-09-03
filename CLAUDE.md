# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

**`AGENT.MD` is the architectural source of truth** and is far more detailed than this file (storage
schema, AI provider matrix, Azure deployment specifics, API surface). Read it for anything not
covered here, and keep it current when you change architecture.

## Commands

```bash
# Build (solution uses the .NET 10 .slnx format — tools expecting .sln will fail)
dotnet build PoMemeVideo.slnx -c Release

# Test
dotnet test tests/PoMemeVideo.UnitTests/PoMemeVideo.UnitTests.csproj -c Release
dotnet test tests/PoMemeVideo.IntegrationTests/PoMemeVideo.IntegrationTests.csproj -c Release
dotnet test tests/PoMemeVideo.E2EAPI/PoMemeVideo.E2EAPI.csproj -c Release

# Single test / class
dotnet test tests/PoMemeVideo.UnitTests/PoMemeVideo.UnitTests.csproj --filter "FullyQualifiedName~SemanticMatchingServiceTests"
dotnet test tests/PoMemeVideo.UnitTests/PoMemeVideo.UnitTests.csproj --filter "FullyQualifiedName~GetTopCandidatesAsync_ReturnsHighestScoringFirst"

# Run (Api hosts the Blazor WASM client — do not run the Client project separately)
dotnet run --project src/PoMemeVideo.Api          # http profile → http://localhost:7000
dotnet run --project src/PoMemeVideo.Api --launch-profile https   # → https://localhost:5001

# Local storage + seeding (Development points at Azurite)
docker compose up -d   # Azurite on 10000/10001/10002
python scripts/seed-meme-sounds.py                 # or: dotnet run --project src/PoMemeVideo.Api -- seed-sounds
python scripts/check-azurite.py

# New machine bootstrap
pwsh -File scripts/setup.ps1        # or: python scripts/setup-new-machine.py
```

There is no separate lint step — `TreatWarningsAsErrors` in `Directory.Build.props` means **the build
is the lint**. A clean build must report `0 Warning(s), 0 Error(s)`.

`ASPNETCORE_ENVIRONMENT` set as an env var is **overridden by `launchSettings.json`**. To run in a
non-Development environment locally you must pass `--no-launch-profile`:

```bash
ASPNETCORE_ENVIRONMENT=Staging dotnet run --project src/PoMemeVideo.Api --no-launch-profile --urls "http://localhost:5280"
```

`PoMemeVideo.E2EUI` (Playwright) is driven by env vars against an already-running instance and is
skipped when unset: `E2E_BASE_URL=http://localhost:7000` (plus `HEADED=1` for a visible browser).

## Architecture

Three projects only — **Api / Client / Shared**. The Api hosts the WASM client same-origin (no CORS
for app traffic; the Blob CORS rules that *are* configured at startup exist only for browser
direct-upload to storage).

### Vertical slices are autonomous — this is the load-bearing rule

Code is organized by feature slice (`Api/Features/<Slice>/`), not by technical layer. **A slice must
not reference a sibling slice.** It depends only on `PoMemeVideo.Shared.Contracts` and resolves the
implementation from DI.

This is enforced by discipline, not the compiler, in two places:

- `Api/GlobalUsings.cs` deliberately imports **only** `Api.Common` + the `Shared` kernel. Adding a
  `global using PoMemeVideo.Api.Features.*` there silently re-opens cross-slice coupling everywhere.
- `Configuration/` is the **composition root** — the one place allowed to see every concrete slice
  type, which it imports explicitly per-file.

When a slice needs something another slice owns, add an interface to `Shared/Contracts` and register
the implementation in `ServiceRegistrationExtensions`. Cross-cutting helpers every slice needs (e.g.
`UserIdentityResolution` — "who is calling?") go in `Api/Common`, never in a slice.

### Strongly-typed IDs wrap an existing database and wire contract

`SessionId` / `UserId` / `SoundId` / `EntryId` (`Shared/Domain/StronglyTypedIds.cs`) are
`readonly record struct`s that exist because `GetByIdAsync(Guid sessionId, Guid userId)` let
transposed arguments compile and silently return null.

The invariant that makes them safe to have introduced over live data: **each serialises as a bare
GUID string and `ToString()` returns the raw GUID.** Table Storage PartitionKey/RowKey and the HTTP
JSON payloads are byte-identical to a raw `Guid`. Breaking that orphans every stored row.
`StronglyTypedIdTests` pins this — do not "simplify" the JSON converters or `ToString()`.

Convention: **strong types inside, primitives at the edges.** Domain entities, repository and
service contracts use the ID types; wire DTOs in `Shared/Models` intentionally keep raw `Guid`.
Minimal-API route parameters bind the strong type directly via `IParsable<T>`.

### Authorization is deny-by-default

`AddAuthorization` sets a `FallbackPolicy` requiring an authenticated user, so **any endpoint without
explicit authorization metadata is protected**. Forgetting `RequireAuthorization()` fails closed.

The consequence: a new endpoint that must be public needs an explicit `AllowAnonymous()`. The opt-outs
today cover `/health` + `/health/live`, `/diag` (via `MapRazorPages`), `/api/config`, the `/auth/*`
family, the sound-stream and browser-director-result callbacks, and the WASM shell
(`MapStaticAssets`, `MapFallbackToFile`) — get the current list with
`grep -rn AllowAnonymous src/PoMemeVideo.Api`. Omitting it on a monitoring or static-asset endpoint
turns it into a 302-to-login, which is easy to miss because a browser still renders something.

Auth layers, in order: Cookie is always the default scheme (OIDC as default would break the
dev/guest `SignInAsync` flow); `FakeAuthHandler` (`X-Fake-User` / `X-Fake-Roles`) for test suites,
registered outside Production and throwing if constructed in Production; a Development-only
middleware that assigns an ANON identity to most requests — which is why **everything looks
authenticated in Development**. Verify authorization changes under `Staging`, not `Development`.

### AI director provider switching

`IDirectorService` is fronted by `SwitchingDirectorService`, which dispatches at call time on
`RuntimeAiSettings.Provider` (`AzureOpenAI` | `AiFoundry` | `BrowserLLM`), mutable at runtime via
`/api/config/ai-model` without a restart. `AiFoundry` is the fallback for any unrecognised value,
because the provider is runtime-mutable and an unknown one must still render a video.

`BrowserLLM` is unusual: the server *asks the browser* to run inference over SignalR and awaits a
`TaskCompletionSource` that the anonymous `/browser-director-result` endpoint resolves. It needs
ONNX weights under `MODEL/` (`python scripts/download-models.py`); with none present the Source
page preselects the cloud path rather than letting the engine stall on an inference that can
never complete. It is the Development default and never the Production one.

The `Ollama` provider was removed — it required a daemon on `localhost:11434` that production
does not have. `RuntimeAiSettings.ValidProviders` rejects it, so a settings file persisted by an
older build cannot re-enable it.

In Test environments (or with `UseMockAI`), `AiInterception` routes the Azure OpenAI SDK through a
`DelegatingHandler` that answers locally — the real client, serialisation and retry path still run,
but no tokens are spent. Hard-disabled in Production.

### Client styling

No inline styles. Component styles live in scoped `*.razor.css`; design tokens are CSS custom
properties in `wwwroot/css/retro-terminal.tokens.css`.

Two non-obvious traps:
- `index.html` must keep the `PoMemeVideo.Client.styles.css` link. Without it **every scoped rule
  silently does nothing** — no error, styles just don't apply.
- A child component's rendered element does not carry the parent's scope id, so utility classes
  splatted onto components (e.g. `.file-input-full` on `<InputFile>`) must be **global** in
  `retro-terminal.components.css`, not scoped.

The three global stylesheets are linked individually from `index.html` in cascade order — tokens,
base, components. They are deliberately not chained with `@import`: nested imports serialise, since
each file must be parsed before the browser discovers the next.

The one permitted `style` attribute is a **CSS custom property carrying a per-render value**
(`style="--upload-progress: 42%"`); the rule that consumes it still lives in CSS.

## Project constraints

- **`Po{Name}` prefix** on all solutions, projects, root namespaces.
- **Centralized Package Management** — never put a `Version` in a `.csproj`; all versions live in
  `Directory.Packages.props`. `CentralPackageTransitivePinningEnabled` is on, so a `PackageVersion`
  entry also pins transitives (this is how vulnerable transitive packages get remediated; NuGet
  audit warnings become build errors).
- Directory depth stays shallow: max 2 levels within `src/`.
- No magic strings for storage — table/container names come from `Shared/StorageNames.cs`.
- `[LoggerMessage]` source generators on high-frequency paths; structured templates, never string
  interpolation, in log calls.
- `X-Session-ID` / `X-Correlation-ID` propagate client → API → outbound HTTP.

## Do not

- Enable AOT (`PublishAot`).
- Add `wwwroot` to any project other than `PoMemeVideo.Client`.
- Hardcode connection strings — config/Key Vault only.
- Add navigation links to `/health` or `/diag`.
- Show the GUEST login button when `ASPNETCORE_ENVIRONMENT == Production`.
- Reintroduce technical-layer projects/folders — keep code in feature slices.
- Add test steps to `deploy-pomemevideo.yml` — the deploy workflow builds and deploys only, by
  rule. Tests belong in `ci.yml`, which gates pushes and PRs against `master`.
