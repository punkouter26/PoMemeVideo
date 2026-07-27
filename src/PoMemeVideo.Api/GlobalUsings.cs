// Infrastructure and the shared kernel are globally visible. Feature slices are NOT:
// a slice reaches a sibling only through PoMemeVideo.Shared.Contracts, resolved via DI.
// The composition root (Configuration/) imports concrete slice types explicitly.
global using PoMemeVideo.Api.Common;
global using PoMemeVideo.Shared;
global using PoMemeVideo.Shared.Contracts;
global using PoMemeVideo.Shared.Domain;
