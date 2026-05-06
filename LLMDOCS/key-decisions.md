# PoMemeVideo – Key Technical Decisions

> Abbreviated from `specs/001-brainrot-video-processor/research.md`. Update that file for full rationale.

| ID | Decision | Rationale |
|----|----------|-----------|
| RES-001 | VSA feature slices inside Onion Architecture | Single-responsibility per feature; no cross-slice coupling |
| RES-002 | Floyd-Steinberg dithering on HTML5 Canvas (client-side JS) | Server-side image processing adds latency; browser canvas is instant |
| RES-003 | Direct-to-Blob SAS upload (client → Azure, bypassing API) | Eliminates API memory pressure for 500 MB uploads |
| RES-004 | Azure OpenAI GPT-4o Vision for semantic trigger detection | Multimodal vision + language in one call; zero FFmpeg frame extraction needed server-side |
| RES-005 | `System.Numerics.Tensors` SIMD cosine similarity | Sub-millisecond matching for 200+ sound vectors in-process; no external ML roundtrip |
| RES-006 | Token-Bucket Timing (2 s min, 10 s max, 5 s target) | Prevents sonic chaos; guarantees minimum density; auditable via PlacementType |
| RES-007 | FFmpeg with `System.Threading.Channels` bounded queue | Native FFmpeg filter graph handles all AV operations; Channels prevents CPU saturation |
| RES-008 | Single SignalR `EngineHub` for all real-time streams | One hub, four message types — simpler client reconnect handling |
| RES-009 | Microsoft OAuth (prod) + `AnonAuthHandler` (dev) | Consistent auth path in all environments; ANON enables E2E automation |
| RES-010 | CSS/SVG retro terminal aesthetic (no video post-processing) | UI effects are free at runtime; keeping them in CSS avoids FFmpeg complexity |
