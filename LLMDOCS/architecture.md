# PoMemeVideo – Architecture Overview

## Solution Layout

```
PoMemeVideo.sln
├── src/
│   ├── PoMemeVideo.Api           # ASP.NET Core host — VSA feature slices, SignalR hub
│   ├── PoMemeVideo.Domain        # Onion innermost — entities, value objects, interfaces
│   ├── PoMemeVideo.Application   # Onion middle — commands, services, use cases
│   ├── PoMemeVideo.Infrastructure# Onion outermost — Azure, FFmpeg, AI, Mock adapters
│   ├── PoMemeVideo.Shared        # Cross-cutting DTOs and enums (client + server)
│   └── client/
│       └── PoMemeVideo.Client    # Blazor WASM SPA (3-stage wizard)
└── tests/
    ├── PoMemeVideo.UnitTests      # xUnit — Domain + Application layer
    ├── PoMemeVideo.IntegrationTests # xUnit + Testcontainers (Azurite)
    └── PoMemeVideo.E2ETests       # Playwright TypeScript (headed in dev)
```

## Architecture Pattern

**Onion Architecture + Vertical Slice Architecture (VSA)**

Dependency direction (outermost → innermost):
`Infrastructure` → `Application` → `Domain`  
`Api` (host) → `Application` + `Infrastructure`  
`Shared` — no dependencies; consumed by all layers

VSA feature slices within `PoMemeVideo.Api/Features/`:
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
