# PoMemeVideo – Key Technical Decisions

> Abbreviated from `specs/001-brainrot-video-processor/research.md`. Update that file for full rationale.

| ID | Decision | Rationale | Status |
|----|----------|-----------|--------|
| RES-001 | VSA feature slices inside Onion Architecture | Single-responsibility per feature; no cross-slice coupling; each slice is independently shippable | ✅ Implemented |
| RES-002 | Floyd-Steinberg dithering on HTML5 Canvas (client-side JS, `canvas-dither.js`) | Server-side image processing adds latency; browser canvas dithering is instant with no API roundtrip | ✅ Implemented |
| RES-003 | Direct-to-Blob SAS upload (browser → Azure, bypassing API) | Eliminates API memory pressure for 500 MB uploads; SAS is time-limited (15 min) and scoped to one blob path | ✅ Implemented |
| RES-004 | Azure OpenAI GPT-4o Vision for semantic trigger detection | Multimodal vision + language in one call; keyframes sent as base64 `image_url` content parts; JSON response schema enforced | ✅ Implemented |
| RES-005 | `System.Numerics.Tensors` SIMD cosine similarity for sound matching | Sub-millisecond matching for 200+ sound vectors in-process; no external ML roundtrip; vocabulary built at startup from all tags | ✅ Implemented |
| RES-006 | Token-Bucket Timing: 2 s min gap, 10 s max fallback, conflict resolution by cosine score | Prevents sonic chaos; guarantees minimum density; every decision is auditable via `PlacementType` enum | ✅ Implemented |
| RES-007 | FFmpeg with `System.Threading.Channels` bounded render queue | Native FFmpeg filter graph handles all AV operations; channel capacity = `Environment.ProcessorCount` prevents CPU saturation | ✅ Implemented |
| RES-008 | Single SignalR `EngineHub` with four server→client message types | One hub = simpler client reconnect handling; group-per-session prevents cross-session message leakage | ✅ Implemented |
| RES-009 | `Microsoft.Identity.Web` OAuth (prod) + `AnonAuthHandler` (dev-only cookie) | Consistent auth path in all environments; ANON mode enables CI/E2E automation without Azure AD credentials | ✅ Implemented |
| RES-010 | Pure CSS/SVG retro terminal aesthetic (`retro-terminal.css`, `glitch-transition.js`) | All UI effects are free at runtime; CRT bulge via SVG displacement filter; scanlines via CSS `repeating-linear-gradient` | ✅ Implemented |

## Additional Decisions Made During Implementation

| ID | Decision | Rationale |
|----|----------|-----------|
| IMPL-001 | `/api/output/sessions/{id}/stream/video` added alongside `/download/video` | `Content-Disposition: attachment` blocks browser audio decoding in `<video>` elements; stream endpoint omits that header |
| IMPL-002 | ANON cookie auth with `SameAsRequest` secure policy in dev | Allows non-HTTPS localhost development without browser cookie rejection |
| IMPL-003 | `dotnet run -- seed-sounds` CLI verb short-circuits before web host construction | Seeding runs without Azure Key Vault auth; uses `appsettings.Development.json` connection string |
| IMPL-004 | `SoundAsset` embedding vectors computed from global vocabulary at seed time | Ensures all vectors have identical dimensionality for cosine similarity; vocabulary is deterministic (sorted unique tags) |
| IMPL-005 | `BrowserLLMDirectorService` added as third director option alongside Ollama/AzureOpenAI | Enables local inference via Transformers.js in the browser when Ollama is not available |
| IMPL-006 | Azurite CORS configured at startup for `*` origins (dev only) | Enables direct browser-to-Azurite SAS uploads without CORS errors during local development |
