// Test projects sit outside the slice boundary, so they import both the shared kernel
// (contracts + domain) and the concrete slice types they exercise directly.
global using PoMemeVideo.Api.Common;
global using PoMemeVideo.Api.Features.Auth;
global using PoMemeVideo.Api.Features.Ingestion;
global using PoMemeVideo.Api.Features.MemeLibrary;
global using PoMemeVideo.Api.Features.Output;
global using PoMemeVideo.Api.Features.Processing;
global using PoMemeVideo.Shared;
global using PoMemeVideo.Shared.Contracts;
global using PoMemeVideo.Shared.Domain;
