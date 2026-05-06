# PoMemeVideo – API Surface

> Auto-maintained. Update when endpoint signatures change.

## Ingestion

| Method | Path | Auth | Notes |
|--------|------|------|-------|
| POST | `/api/ingestion/sas` | Required | Returns `{ sessionId, sasUrl, expiresAt }` |
| POST | `/api/ingestion/sessions` | Required | Confirms blob upload; body: `{ sessionId, blobPath, videoDurationSeconds, aggressiveVisuals }` |
| GET | `/api/ingestion/sessions/{sessionId}` | Required | Returns `VideoSessionDto` |

## Processing

| Method | Path | Auth | Notes |
|--------|------|------|-------|
| POST | `/api/processing/sessions/{sessionId}/initiate` | Required | Returns `202 Accepted`; dispatches engine as background Task |

## Meme Library

| Method | Path | Auth | Notes |
|--------|------|------|-------|
| GET | `/api/memelibrary/sounds` | Required | Supports `?tags=&limit=`; returns `SoundAssetDto[]` |

## Output

| Method | Path | Auth | Notes |
|--------|------|------|-------|
| GET | `/api/output/sessions/{id}/script` | Required | Returns `DirectorScriptDto` |
| GET | `/api/output/sessions/{id}/download/video` | Required | Streams MP4; `Content-Disposition: attachment` |
| GET | `/api/output/sessions/{id}/download/script` | Required | Streams JSON; `Content-Disposition: attachment` |
| DELETE | `/api/output/sessions/{id}` | Required | Wipe Buffer — deletes session, script, all blobs |

## Auth

| Method | Path | Auth | Notes |
|--------|------|------|-------|
| POST | `/auth/anon` | None | Dev-only; returns signed session cookie |
| GET | `/auth/login/microsoft` | None | Redirects to Microsoft OAuth |
| GET | `/auth/callback` | None | OAuth callback handler |
| POST | `/auth/logout` | Required | Revokes session |

## Infrastructure

| Method | Path | Auth | Notes |
|--------|------|------|-------|
| GET | `/health` | None | JSON; `200 Healthy` or `503 Degraded` |
| GET | `/diag` | None (dev-only) | Masked secrets + connection status |
| GET | `/api/config` | None | Returns `{ useMockAI, isDevelopment }` |
| GET | `/scalar` | None | OpenAPI Scalar UI |
