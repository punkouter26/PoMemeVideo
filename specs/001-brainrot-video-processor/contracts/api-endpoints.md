# API Endpoints Contract: PoMemeVideo

**Version**: 1.0.0  
**Date**: 2026-05-05  
**Base URL (local)**: `https://localhost:5001`  
**OpenAPI UI**: `https://localhost:5001/scalar`  
**Auth**: Bearer token (Microsoft OAuth) or session cookie (ANON). All endpoints except `/health` and `/auth/*` require authentication.

---

## Ingestion Slice

### POST /api/ingestion/sas

Generate a time-limited SAS token for direct-to-Blob client upload.

**Request body**:
```json
{
  "fileName": "my-video.mp4",
  "fileSizeBytes": 104857600
}
```

**Validation**:
- `fileSizeBytes` must be ≤ 524,288,000 (500 MB). Returns `400` with ASCII-styled error body if exceeded.
- `fileName` extension must be one of: `.mp4`, `.mov`, `.avi`, `.webm`.

**Response `200`**:
```json
{
  "sessionId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "sasUrl": "https://pomemevideo.blob.core.windows.net/videos/sessions/3fa85f64-.../source.mp4?sv=...&sig=...",
  "expiresAt": "2026-05-05T14:15:00Z"
}
```

**Response `400`**:
```json
{
  "error": "FILE_TOO_LARGE",
  "message": "Video exceeds maximum size of 500 MB.",
  "maxBytes": 524288000,
  "receivedBytes": 600000000
}
```

---

### POST /api/ingestion/sessions

Confirm upload complete and create the VideoSession record. Called by client after SAS upload succeeds.

**Request body**:
```json
{
  "sessionId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "blobPath": "sessions/3fa85f64-.../source.mp4",
  "videoDurationSeconds": 42.5,
  "aggressiveVisuals": true
}
```

**Response `201`**:
```json
{
  "sessionId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "status": "Ingesting"
}
```

---

### GET /api/ingestion/sessions/{sessionId}

Get current VideoSession status and metadata.

**Response `200`**:
```json
{
  "sessionId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "status": "Complete",
  "aggressiveVisuals": true,
  "videoDurationSeconds": 42.5,
  "createdAt": "2026-05-05T14:00:00Z",
  "completedAt": "2026-05-05T14:00:55Z",
  "outputBlobPath": "sessions/3fa85f64-.../output.mp4",
  "errorMessage": null
}
```

---

## Processing Slice

### POST /api/processing/sessions/{sessionId}/initiate

Start the AI analysis and Director's Script generation pipeline. Client must be connected to the SignalR `EngineHub` before calling this endpoint.

**Request body**: empty  
**Response `202`**:
```json
{
  "sessionId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "status": "Processing"
}
```

**Response `409`** (session already processing or complete):
```json
{
  "error": "INVALID_SESSION_STATE",
  "currentStatus": "Complete"
}
```

---

## Meme Library Slice

### GET /api/memelibrary/sounds

List sound assets with optional action vector filter (used for debugging and `/LLMDOCS` tooling).

**Query params**: `?tags=fail,thud&limit=20`  
**Response `200`**:
```json
{
  "sounds": [
    {
      "soundId": "a1b2c3d4-...",
      "displayName": "Vine Boom",
      "durationMs": 850,
      "actionVectorTags": ["boom", "impact", "fail"],
      "blobUrl": "https://pomemevideo.blob.core.windows.net/sounds/a1b2c3d4.mp3"
    }
  ],
  "totalCount": 200
}
```

---

## Rendering / Output Slice

### GET /api/output/sessions/{sessionId}/script

Return the finalised Director's Script JSON for the Reveal page panel.

**Response `200`**:
```json
{
  "sessionId": "3fa85f64-...",
  "generatedAt": "2026-05-05T14:00:52Z",
  "totalSoundCount": 9,
  "averageDensitySeconds": 4.7,
  "entries": [
    {
      "entryId": "...",
      "timestampMs": 4200,
      "soundId": "a1b2c3d4-...",
      "actionVectorTags": ["impact", "fail"],
      "selectionRationale": "Sudden downward motion at 4.2s matched 'thud/fail' vector. Vine Boom selected for maximum comedic impact.",
      "isIronic": false,
      "visualEffect": "DeepFry",
      "effectIntensity": 0.85,
      "overlayAssetId": null,
      "placementType": "Triggered"
    }
  ]
}
```

---

### GET /api/output/sessions/{sessionId}/download/video

Stream the rendered MP4. Sets `Content-Disposition: attachment; filename="pomemevideo-{sessionId}.mp4"`.

**Response**: Binary MP4 stream with `Content-Type: video/mp4`.

---

### GET /api/output/sessions/{sessionId}/download/script

Download the Director's Script JSON file.

**Response**: JSON file with `Content-Disposition: attachment; filename="director-script-{sessionId}.json"`.

---

### DELETE /api/output/sessions/{sessionId}

Wipe Buffer — delete all session state (Table Storage records + Blob assets).

**Response `204`**: No content.

---

## System Endpoints

### GET /health

Returns JSON health report for all external dependencies.

**Response `200`**:
```json
{
  "status": "Healthy",
  "timestamp": "2026-05-05T14:00:00Z",
  "checks": {
    "azureTableStorage": { "status": "Healthy", "latencyMs": 12 },
    "azureBlobStorage":  { "status": "Healthy", "latencyMs": 8 },
    "azureAiVision":     { "status": "Healthy", "latencyMs": 210 },
    "ollamaGemma4":      { "status": "Healthy", "latencyMs": 45 }
  }
}
```

**Response `503`** (any dependency degraded):
```json
{
  "status": "Degraded",
  "checks": {
    "ollamaGemma4": { "status": "Unhealthy", "error": "Connection refused at localhost:11434" }
  }
}
```

---

## Authentication Endpoints

### POST /auth/anon *(Development only)*

Create an ANON session identity.

**Response `200`** (sets session cookie):
```json
{
  "displayName": "ANON463443",
  "identityType": "ANON"
}
```

### GET /auth/login/microsoft

Redirect to Microsoft OAuth login flow.

### GET /auth/callback

OAuth callback endpoint (handled by `Microsoft.Identity.Web`).

### POST /auth/logout

Clear session / revoke token.

---

## HTTP Error Shape (all endpoints)

```json
{
  "error": "ERROR_CODE",
  "message": "Human-readable description.",
  "traceId": "00-abc123-def456-00"
}
```
