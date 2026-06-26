// VSA cross-slice access: feature slices and the shared kernel are globally
// visible so handlers can reference domain types from sibling slices without
// per-file using noise. The folder layout (Features/<Slice>) is the boundary.
global using PoMemeVideo.Api.Common;
global using PoMemeVideo.Api.Features.Admin;
global using PoMemeVideo.Api.Features.Auth;
global using PoMemeVideo.Api.Features.Config;
global using PoMemeVideo.Api.Features.Ingestion;
global using PoMemeVideo.Api.Features.MemeLibrary;
global using PoMemeVideo.Api.Features.Output;
global using PoMemeVideo.Api.Features.Processing;
