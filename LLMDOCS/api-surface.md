# PoMemeVideo – API Surface

> Auto-maintained. Update when endpoint signatures change.

## Ingestion

| Method | Path | Auth | Response | Notes |
|--------|------|------|----------|-------|
| `POST` | `/api/ingestion/sas` | None | `200 { sessionId, sasUrl, expiresAt }` | Creates VideoSession; generates 15-min SAS Write token scoped to `sessions/{id}/source.{ext}` |
| `POST` | `/api/ingestion/sessions` | None | `201` | Confirms blob upload; body: `{ sessionId, blobPath, videoDurationSeconds, aggressiveVisuals }` |
| `GET` | `/api/ingestion/sessions/{sessionId}` | None | `200 VideoSessionDto \| 404` | Returns current session status and metadata |

**Error shapes**: `400 { error: "INVALID_EXTENSION", allowedExtensions }` | `400 { error: "FILE_TOO_LARGE", maxBytes }`

## Processing

| Method | Path | Auth | Response | Notes |
|--------|------|------|----------|-------|
| `POST` | `/api/processing/sessions/{sessionId}/initiate` | None | `202 Accepted` | Validates session is `Ingesting`; dispatches `RunEngineCommand` as background Task; real-time updates via SignalR |
| `POST` | `/api/processing/sessions/{sessionId}/browser-director-result` | None | `204 \| 404` | Receives `BrowserDirectorResultDto` from Transformers.js inference in browser |

## Meme Library

| Method | Path | Auth | Response | Notes |
|--------|------|------|----------|-------|
| `GET` | `/api/memelibrary/sounds` | None | `200 SoundAssetDto[]` | Query params: `?tags=boom,impact&limit=20`; results from in-memory cache |

## Output

| Method | Path | Auth | Response | Notes |
|--------|------|------|----------|-------|
| `GET` | `/api/output/sessions/{id}/script` | None | `200 DirectorScriptDto` | Loads `DirectorScript` from Table Storage; returns full `entries[]` |
| `GET` | `/api/output/sessions/{id}/download/video` | None | `200 video/mp4` | Streams MP4 from Blob; `Content-Disposition: attachment; filename="pomemevideo-{id}.mp4"` |
| `GET` | `/api/output/sessions/{id}/stream/video` | None | `200 video/mp4` | Inline stream for `<video>` element; no `Content-Disposition`; range-processing enabled |
| `GET` | `/api/output/sessions/{id}/download/script` | None | `200 application/json` | Streams Director's Script JSON; `Content-Disposition: attachment; filename="director-script-{id}.json"` |
| `DELETE` | `/api/output/sessions/{id}` | None | `204` | Wipe Buffer — deletes VideoSession + DirectorScript rows + all blobs under `sessions/{id}/` |
| `GET` | `/api/output/sessions/{id}/results` | None | `200 { session, script, videoStreamUrl }` | Convenience endpoint for Results page |

## Auth

| Method | Path | Auth | Response | Notes |
|--------|------|------|----------|-------|
| `POST` | `/auth/anon` | None | `200 { identityId, displayName, identityType }` | **Dev-only**; generates `ANON######`; creates UserIdentity in Table Storage; writes signed session cookie |
| `GET` | `/api/auth/me` | None | `200 { displayName, email }` | Returns current user claims; returns `{ null, null }` for guests |
| `GET` | `/auth/login/microsoft` | None | `302` | Challenges OIDC if Azure AD configured, else redirects to `/login` |
| `GET` | `/auth/callback` | None | `302 /` | OAuth callback redirect |
| `POST` | `/auth/logout` | None | `302 /` | Signs out OIDC + Cookie; revokes session |

## Admin

| Method | Path | Auth | Response | Notes |
|--------|------|------|----------|-------|
| `DELETE` | `/api/admin/data` | None | `200 { cleared, message }` | Wipes ALL session blobs + VideoSessions + DirectorScripts tables; Sound library preserved |

## AI Model Control

| Method | Path | Auth | Response | Notes |
|--------|------|------|----------|-------|
| `GET` | `/api/config` | None | `200 { useMockAI, isDevelopment }` | Feature flags from `appsettings`; consumed by `MockDataBanner.razor` and `Login.razor` |

## Infrastructure

| Method | Path | Auth | Response | Notes |
|--------|------|------|----------|-------|
| `GET` | `/health` | None | `200 Healthy \| 503 Degraded` | Checks Azure Table Storage, Blob Storage, AI Vision, Ollama |
| `GET` | `/diag` | None | HTML | Connection statuses + masked config values (first3***last3); Razor Page |
| `GET` | `/scalar` | None | HTML | Scalar OpenAPI UI |

## SignalR Hub — `/hubs/engine`

| Direction | Method | Payload | Notes |
|-----------|--------|---------|-------|
| Client → Server | `JoinSession(sessionId)` | `string` | Joins SignalR group for session |
| Client → Server | `LeaveSession(sessionId)` | `string` | Leaves group |
| Server → Client | `DirectorLogEntry` | `string` | Human-readable reasoning line |
| Server → Client | `DirectorScriptEntry` | `ScriptEntryDto` | One script entry as generated |
| Server → Client | `AuditEntry` | `string` | Conflict/fallback audit event |
| Server → Client | `HardwareMetrics` | `(double latencyMs, double cpuPct)` | Emitted every 1 s during inference |
| Server → Client | `ProcessingComplete` | `string sessionId` | Navigates client to `/reveal/{id}` |
| Server → Client | `ProcessingError` | `string message` | Displays error in Engine page |

## CLI Verbs

| Command | Description |
|---------|-------------|
| `dotnet run -- seed-sounds [--seeds-dir <path>]` | Seeds 200+ SoundAsset records from `tools/meme-sounds/`; idempotent |

