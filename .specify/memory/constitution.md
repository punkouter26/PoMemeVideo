<!--
SYNC IMPACT REPORT
==================
Version change: (template) → 1.0.0
Modified principles: N/A (initial constitution fill from template)
Added sections: All 10 principles + Infrastructure, Security, Testing, Observability, Engineering sections
Removed sections: All placeholder tokens replaced
Templates requiring updates:
  ✅ .specify/memory/constitution.md (this file — written now)
  ✅ .specify/templates/plan-template.md (Constitution Check gates updated)
  ✅ .specify/templates/spec-template.md (no structural changes required)
  ✅ .specify/templates/tasks-template.md (no structural changes required)
Follow-up TODOs:
  - TODO(RATIFICATION_DATE): Set to 2026-05-05 (today, initial ratification)
-->

# PoMemeVideo Constitution

## Core Principles

### I. Project Identity & Naming (NON-NEGOTIABLE)

- The solution MUST use the mandatory `Po***` prefix (e.g., `PoMemeVideo`).
  The `.sln` file name MUST match the `<title>` element in any manifest/config.
- `PoMemeVideo` is the master prefix applied to: all namespaces, Azure Resource
  Group names, and .NET Aspire resource identifiers.
- A `global.json` MUST exist at the repository root and pin the project to the
  latest stable release of .NET 10.
- No project, namespace, or Azure resource may omit the `Po` prefix prefix.

**Rationale**: Consistent prefixing prevents naming collisions across shared
Azure subscriptions and makes all artifacts immediately identifiable as part
of this solution family.

### II. Core Architecture & Frameworks (NON-NEGOTIABLE)

- **Server-side** MUST follow strict Onion Architecture with clear physical
  layer separation: `Domain` → `Application` → `Infrastructure`. No layer may
  reference a layer outside-in. Reference architecture:
  https://blog.anilgurau.com/step-by-step-approach-to-use-onion-architecture-in-net
- **Client-side** MUST be Blazor WASM, kept intentionally simple. Complex UI
  controls (data grids, forms) MUST use Radzen UI components.
- All server-side C# MUST target C# 14 features where appropriate.
- SOLID principles and GoF design patterns MUST be applied. Every
  non-trivial application of a SOLID or GoF pattern MUST include a code
  comment identifying the pattern (e.g., `// GoF: Repository Pattern`).
- Business logic MUST reside in the Domain and Application layers — never in
  Infrastructure or the Blazor client.

**Rationale**: Strict layering enforces testability and portability. Pattern
comments accelerate LLM-assisted development and code reviews.

### III. Project Structure & Configuration (NON-NEGOTIABLE)

- `.props` files and `Directory.Packages.props` (Central Package Management)
  MUST reside in the repository root.
- `Directory.Build.props` MUST enable `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`
  and `<Nullable>enable</Nullable>` globally.
- A `PoShared.csproj` MUST exist to hold all classes shared between the
  Blazor WASM client and the API server.
- The Blazor WASM project MUST be hosted within the server project. The server
  MUST listen on HTTP: 5000 and HTTPS: 5001 (fixed, non-negotiable for local
  development).
- The `wwwroot` folder MUST exist only in the client (WASM) project; it MUST
  be deleted from the server project if present.
- `.gitignore` MUST cover: `.vs/`, `.vscode/`, `bin/`, `obj/`, and all
  standard .NET artifacts. No generated or build output may be committed.

**Rationale**: Centralized configuration prevents drift; fixed ports eliminate
developer friction and ensure reproducible launch scripts.

### IV. API & Backend Standards

- Local development ports are fixed: HTTP `5000`, HTTPS `5001`. No overrides.
- OpenAPI MUST be enabled via Scalar. `.http` files MUST be provided for all
  primary API functions to support debugging without external tooling.
- A `/diag` page MUST display the status of all external connections (DB,
  APIs, Key Vault) and configuration keys in use. Middle characters of
  sensitive values MUST be masked (e.g., `abc***xyz`).
- A `/health` endpoint MUST return a valid JSON response that pings and
  reports the status of all external connections.
- The VS Code `F5` launch task MUST kill any existing .NET processes before
  launching the server (or Aspire dashboard) and MUST open the app in Edge.

**Rationale**: Consistent tooling reduces onboarding time and ensures every
developer — human or LLM — can immediately verify the system is healthy.

### V. Infrastructure & Azure Deployment

- All secrets MUST be pulled from **Azure Key Vault** (PoShared). Secrets MUST
  NOT be stored in `appsettings.json` or any committed file.
- App-specific secrets (OAuth tokens, Storage connection strings) MUST be
  prefixed with the app name (e.g., `PoMemeVideo-`). Shared secrets MUST
  remain un-prefixed.
- **Cloud Identity**: Use Managed Identity within subscription
  `Punkouter26` (ID: `Bbb8dfbe-9169-432f-9b7a-fbf861b51037`).
- Target Azure App Services and Azure Table Storage as primary Azure resources,
  deployed in the app's own resource group.
- App Service Plans MUST be sourced from the **PoShared** resource group (not
  the app's resource group).
- Azure Table Storage MUST connect to a service in the application's specific
  resource group — not a shared one.

**Rationale**: Centralizing secrets in Key Vault and using Managed Identity
eliminates credential leakage. Isolating Table Storage per-app prevents
cross-app data contamination.

### VI. Authentication & Security (NON-NEGOTIABLE)

- If the application uses authentication, an **"ANON" login button** MUST be
  present on the login page for local development and testing.
  - ANON login generates a unique username with a random numeric suffix
    (e.g., `ANON463443`) on each login.
  - All data created under ANON (high scores, user-specific data, etc.) MUST
    be stored in the database under the ANON account.
  - When authenticated, the user's email MUST appear in the navigation bar.
    If ANON is logged in, display `ANON LOGGED IN`.
- Microsoft OAuth MUST be enabled in both `Development` and `Production`
  environments.
- The ANON login path is exclusively for testing and local development. It
  MUST NOT be exposed in production.
- No secret, credential, or API key may be hardcoded or committed to source
  control.

**Rationale**: ANON login enables E2E test automation without exposing real
credentials. Consistent OAuth in all environments prevents auth-gap bugs.

### VII. Mandatory Testing & Quality Assurance (NON-NEGOTIABLE)

- **Unit Tests (C#)**: MUST target Domain logic and Application Service layers.
- **Integration Tests (C#)**: MUST use Testcontainers (Azurite/SQL) to test
  API endpoints and repository patterns. No in-process fakes for storage.
- **E2E Tests (TypeScript/Playwright)**: MUST cover critical user paths in the
  Blazor UI. MUST run in **headed mode** in `Development` environment.
- Local development MUST use **Azurite running in Docker** for storage
  simulation. Local storage emulators (non-Docker) are prohibited.
- **AI Integration Testing**: Real AI service calls are permitted ONLY in
  `Development` mode (when the developer runs the app locally). Integration
  and E2E tests MUST use mock data that resembles real AI responses.
  When running the web app locally or on Azure as a real user, mock data MUST
  NOT be used — only real services.

**Rationale**: Testcontainers ensures infrastructure parity between test and
production. Isolating real AI calls prevents test cost explosion while
preserving real-world validation in dev.

### VIII. Observability & Debugging

- **Structured Logging**: Use **Serilog** to log to File, Console, and
  Azure App Insights. No unstructured `Console.WriteLine` logging in
  production code.
- **Telemetry**: Enable **OpenTelemetry** globally, aggregated to the PoShared
  App Insights instance.
- Every log entry MUST include as structured properties: `UserId`,
  `SessionId`, `Environment`, `CorrelationId`, and full `Exception` objects
  (not just message strings).
- In `Development` mode, the UI MUST surface specific error details and full
  stack traces to facilitate rapid debugging and LLM code review visibility.
  This MUST be suppressed in all other environments.

**Rationale**: Rich, structured telemetry enables fast root-cause analysis.
Dev-mode stack traces accelerate the human-LLM debugging loop.

### IX. Engineering Hygiene & LLM Workflow

- **Zero-Waste Policy**: Unused files, dead code, and obsolete assets MUST be
  deleted immediately. No commented-out code blocks left in committed files.
- **Code Documentation**: Comment ONLY on complex business logic and SOLID/GoF
  pattern applications. Do NOT comment self-explanatory code (standard
  constructors, simple getters, etc.).
- **Feature Flags**: External API integrations and experimental features MUST
  be controlled via `appsettings.json` toggles to allow behavior changes
  without code redeployment.
- **`/LLMDOCS` Folder**: MUST be maintained at the repository root. Update its
  files only when project structure or public API surfaces change significantly.
  This folder provides a quick-start reference for LLM coding assistants.
- **Ambiguity Stop-Rule**: If a task is unclear, implementation MUST STOP and
  produce a bulleted list of assumptions for human clarification before
  generating any code.

**Rationale**: A clean, well-documented codebase and strict LLM workflow rules
minimize context pollution and maximize effective AI-assisted development.

### X. User Experience & Data Transparency

- When the application is serving mock data (test/demo mode), a prominent
  **"MOCK DATA"** alert MUST be displayed at the top of the affected page(s).
- Mock data mode MUST be toggled exclusively via a feature flag in
  `appsettings.json` — never by environment detection alone.

**Rationale**: Transparent data-mode indicators prevent confusion between
test results and real production behavior.

## Infrastructure Standards

- **Azure Subscription**: `Punkouter26` (`Bbb8dfbe-9169-432f-9b7a-fbf861b51037`)
- **Primary compute**: Azure App Services (App Service Plans from PoShared RG)
- **Primary storage**: Azure Table Storage (per-app resource group)
- **Secret store**: Azure Key Vault (PoShared)
- **Identity**: Managed Identity (no service principal passwords)
- **Monitoring**: PoShared App Insights instance via OpenTelemetry
- **Local storage simulation**: Azurite in Docker (mandatory)

## Development Workflow

- **Branch strategy**: Feature branches per spec, auto-committed via
  `speckit.git.*` hooks before key workflow transitions.
- **Local launch**: `F5` in VS Code kills existing .NET processes → starts
  server on 5000/5001 → opens Edge.
- **OpenAPI**: Scalar UI enabled; `.http` files provided for all endpoints.
- **Constitution compliance**: Every PR MUST be verified against this
  constitution before merge. Any deviation requires an amendment with
  documented rationale and a version bump.
- **No placeholder tolerance**: No `[BRACKET_TOKEN]` artifacts may be
  committed to any template, spec, plan, or task file in a completed state.

## Governance

This constitution supersedes all other conventions, style guides, and informal
agreements within the PoMemeVideo project. Amendments MUST:

1. Update this file with a version bump following semantic versioning:
   - **MAJOR**: Removal or incompatible redefinition of a principle.
   - **MINOR**: Addition of a new principle or materially expanded guidance.
   - **PATCH**: Clarifications, wording fixes, non-semantic refinements.
2. Update the Sync Impact Report comment at the top of this file.
3. Propagate changes to all affected templates in `.specify/templates/`.
4. Be committed with message format:
   `docs: amend constitution to vX.Y.Z (<summary of change>)`

All code generation by LLMs and all human code reviews MUST verify compliance
with the principles in this constitution. Non-compliant code MUST be rejected
or corrected before merge.

See `/LLMDOCS` for runtime development guidance and codebase quick-reference.

**Version**: 1.0.0 | **Ratified**: 2026-05-05 | **Last Amended**: 2026-05-05
