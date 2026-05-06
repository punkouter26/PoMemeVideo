# SignalR Hub Contract: EngineHub

**Version**: 1.0.0  
**Date**: 2026-05-05  
**Hub URL (local)**: `https://localhost:5001/hubs/engine`  
**Client library**: `Microsoft.AspNetCore.SignalR.Client` (Blazor WASM)  
**Group strategy**: Each client joins group `session-{sessionId}` on connect. All server→client messages are scoped to the session group.

---

## Client → Server Methods

### JoinSession

Called immediately after the Engine page loads and before `POST /api/processing/.../initiate`.

```csharp
await hubConnection.InvokeAsync("JoinSession", sessionId);
```

**Parameters**: `sessionId` (string, GUID)  
**Effect**: Server adds connection to group `session-{sessionId}`.

---

### LeaveSession

Called when the Reveal page loads (processing complete) or on Wipe Buffer.

```csharp
await hubConnection.InvokeAsync("LeaveSession", sessionId);
```

---

## Server → Client Methods

All methods are scoped to the `session-{sessionId}` group.

### DirectorLogEntry

Human-readable reasoning line for the Director's Log terminal feed (right panel).

```csharp
// Server sends:
await Clients.Group($"session-{sessionId}").SendAsync("DirectorLogEntry", message);

// Client receives:
hubConnection.On<string>("DirectorLogEntry", message =>
{
    // Append to scrolling terminal feed
});
```

**Payload**: `message` (string)  
**Example values**:
```
SCANNING... t=00:04.2
ACTION DETECTED: [SUDDEN_TRIP]
SEARCHING SOUND LIBRARY... 3 candidates found
SELECTED: Vine Boom (accuracy=0.93, ironic=false)
VISUAL EFFECT ASSIGNED: DeepFry (intensity=0.85)
TOKEN BUCKET: next window opens at t=00:06.2
```

---

### DirectorScriptEntry

Raw JSON script entry typed out to the Director's Script feed (left panel).

```csharp
// Server sends:
await Clients.Group($"session-{sessionId}").SendAsync("DirectorScriptEntry", entry);

// Client receives:
hubConnection.On<ScriptEntryDto>("DirectorScriptEntry", entry =>
{
    // Append JSON block to rapid-fire script feed
});
```

**Payload**: `ScriptEntryDto`

```json
{
  "entryId": "uuid",
  "timestampMs": 4200,
  "soundId": "uuid",
  "actionVectorTags": ["impact", "fail"],
  "selectionRationale": "Sudden downward motion at 4.2s matched 'thud/fail' vector.",
  "isIronic": false,
  "visualEffect": "DeepFry",
  "effectIntensity": 0.85,
  "overlayAssetId": null,
  "placementType": "Triggered"
}
```

---

### AuditEntry

System Audit Box event — conflict resolutions, fallback placements, dice-roll decisions.

```csharp
// Server sends:
await Clients.Group($"session-{sessionId}").SendAsync("AuditEntry", message);

// Client receives:
hubConnection.On<string>("AuditEntry", message =>
{
    // Append to System Audit Box console
});
```

**Payload**: `message` (string)  
**Example values**:
```
[CONFLICT] t=05.1 vs t=05.8 — gap=700ms < 2000ms minimum.
  Candidates: [Vine Boom score=0.93] vs [Metal Pipe score=0.71]
  RESOLVED: Vine Boom selected (higher score). Metal Pipe dropped.
[FALLBACK] No trigger detected for 10.0s. Placing ambient sound at t=14.2.
  Selected: Crickets (ambient vector match).
```

---

### HardwareMetrics

Real-time hardware performance update for the Hardware Monitor dashboard. Emitted every 1 second during active inference.

```csharp
// Server sends:
await Clients.Group($"session-{sessionId}").SendAsync(
    "HardwareMetrics", inferenceLatencyMs, cpuLoadPercent);

// Client receives:
hubConnection.On<double, double>("HardwareMetrics",
    (inferenceLatencyMs, cpuLoadPercent) =>
{
    // Update Hardware Monitor dials
});
```

**Parameters**:
- `inferenceLatencyMs` (double) — time for the last AI inference call to complete, in milliseconds.
- `cpuLoadPercent` (double) — server CPU load as a percentage (0.0–100.0).

---

### ProcessingComplete

Signals the client that the engine and render are done. Client transitions to the Reveal page.

```csharp
await Clients.Group($"session-{sessionId}").SendAsync("ProcessingComplete", sessionId);
```

**Payload**: `sessionId` (string)  
**Client action**: Trigger glitch-transition animation, then navigate to `/reveal/{sessionId}`.

---

### ProcessingError

Signals an unrecoverable error during processing.

```csharp
await Clients.Group($"session-{sessionId}").SendAsync("ProcessingError", errorMessage);
```

**Payload**: `errorMessage` (string)  
**Client action**: Display ASCII-styled error in the Engine page and show a "Wipe Buffer" recovery option.

---

## Connection Lifecycle

```
Client loads Engine page
    │
    ├─ hubConnection.StartAsync()
    ├─ hubConnection.InvokeAsync("JoinSession", sessionId)
    ├─ Register all On<> handlers
    │
    └─ POST /api/processing/{sessionId}/initiate
           │
           Server emits: DirectorLogEntry, DirectorScriptEntry, AuditEntry, HardwareMetrics
           ...
           Server emits: ProcessingComplete
           │
    Client navigates to Reveal page
    │
    ├─ hubConnection.InvokeAsync("LeaveSession", sessionId)
    └─ hubConnection.StopAsync()
```

---

## Reconnection Policy

The Blazor client configures `HubConnectionBuilder` with `.WithAutomaticReconnect(new[] { 0, 2000, 5000, 10000 })`. On reconnection, the client re-invokes `JoinSession` to re-enter the session group. The server re-sends the last 10 buffered `DirectorLogEntry` messages (stored in a `Channel<string>` ring buffer) to resync the feed.
