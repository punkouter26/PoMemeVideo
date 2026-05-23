# PoMemeVideo Architecture

This document is the repository-level architecture and naming reference for humans and LLM tooling.

## Identity and Naming

- Solution name: PoMemeVideo.
- Master prefix: PoMemeVideo for app naming; PoShared/Punkouter26 for shared Azure tenancy resources.
- Namespace root: PoMemeVideo.*
- Secret prefix for app-specific entries: PoMemeVideo--

## Architectural Shape

The solution follows Onion Architecture with project boundaries under src/:

- src/PoMemeVideo.Domain: entities, value objects, interfaces.
- src/PoMemeVideo.Application: use-cases and orchestration.
- src/PoMemeVideo.Infrastructure: external systems (storage, AI providers, FFmpeg, integrations).
- src/PoMemeVideo.Api: web host, endpoints, auth, diagnostics.
- src/client/PoMemeVideo.Client: Blazor WebAssembly front-end.
- src/PoMemeVideo.Shared: contracts and cross-layer shared models.

## Standards Snapshot

- .NET SDK pinned in global.json to net10 toolchain.
- Central package management via Directory.Packages.props.
- Global strictness from Directory.Build.props: nullable + warnings as errors.
- Git-tag driven versioning via MinVer package reference from Directory.Build.props.
- OpenAPI exposed with Scalar UI.
- Health and diagnostics surfaces:
  - /health for machine checks
  - /diag for development diagnostics

## Key Conventions

- Development guest identity format is GUEST######.
- Guest identity should survive refresh and E2E runs via SessionStorage.
- When mock services are active, the top navigation displays USING MOCK DATA.
- Local development prefers fixed API ports 5000/5001.
- Scripts and machine-bootstrap helpers live under SCRIPTS/.

## Known Tech Debt

- Some historical docs still reference ANON identity naming and should be migrated to GUEST terminology.
- Not all legacy automation flows are wired to SCRIPTS/setup.ps1 yet; Python bootstrap remains available.
- Additional XML <remarks> coverage for pattern-level rationale should continue across complex services.

## References

- LLMDOCS/architecture.md
- LLMDOCS/api-surface.md
- LLMDOCS/key-decisions.md
