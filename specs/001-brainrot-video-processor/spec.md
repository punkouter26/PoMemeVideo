# Feature Specification: PoMemeVideo – Brainrot Video Processor

**Feature Branch**: `001-brainrot-video-processor`  
**Created**: 2026-05-05  
**Status**: Draft  
**Input**: Full product brief describing the PoMemeVideo "Magic Button" application

---

## Clarifications

### Session 2026-05-05

- Q: Which AI provider powers the Semantic Trigger Detection engine? → A: Azure OpenAI (GPT-4o Vision / multimodal)
- Q: Which real-time streaming protocol delivers the Director's Log and Director's Script to the Blazor client? → A: SignalR
- Q: What is the maximum supported video file size? → A: 500 MB
- Q: Where is the 200+ meme sound library stored? → A: Azure Blob Storage in the PoMemeVideo resource group (metadata in Azure Table Storage, audio files in Blob Storage)
- Q: What is the target service availability / uptime? → A: 99.9% monthly uptime

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 – Video Ingestion & Keyframe Preview (Priority: P1)

A user drags and drops a local video file onto the Source Page. The system immediately validates the file, extracts keyframes every three seconds, applies a 1-bit dithered green-scale processing effect to each frame, and displays them in a horizontal strip inside an ASCII-bordered preview zone. A single toggle labelled "Aggressive Visuals" is visible below the strip. The user reviews the keyframes to confirm correct content before proceeding.

**Why this priority**: Without successful ingestion and preview, the entire pipeline cannot function. This is the entry point to all value. Delivering this alone proves the system can accept user content and produce the retro visual aesthetic.

**Independent Test**: Upload a sample video; verify that (a) the file is accepted, (b) the correct number of dithered keyframes appears (⌊duration / 3⌋ frames), (c) each frame renders in the Matrix Green 1-bit palette, and (d) the "Aggressive Visuals" toggle is present and responsive.

**Acceptance Scenarios**:

1. **Given** the Source Page is loaded, **When** a user drops a valid video file (MP4, MOV, AVI, WebM) onto the drop zone, **Then** the system accepts the file, displays a scanning progress indicator in the terminal log style, and renders the dithered keyframe strip within 5 seconds for videos up to 60 seconds long.
2. **Given** keyframes are displayed, **When** the user inspects the preview strip, **Then** every frame is rendered in 1-bit, Matrix Green palette with no full-color artifacts.
3. **Given** an invalid or corrupt file is dropped, **When** validation fails, **Then** the system displays an ASCII-styled error message in the drop zone without crashing and invites retry.
4. **Given** a very large video (> 10 minutes), **When** ingested, **Then** the system warns the user about expected processing time while still accepting the file.

---

### User Story 2 – AI-Directed Meme Sound & Visual Mapping (Priority: P2)

After pressing "Initiate," the user watches the Engine Page in real time. The system analyses the video for semantic visual triggers (falls, surprised expressions, sudden movements, confused looks, etc.), matches each trigger to one or more sounds from a 200+ sound library using a Semantic Matching Engine, and applies timing constraints via a Token-Bucket algorithm (minimum 2 s gap, maximum 10 s gap, average target of one sound per 5 s). When "Aggressive Visuals" was enabled, the system independently improvises visual effects (deep-fry, snap-zoom, motion blur, overlays) at the same trigger timestamps. All decisions are streamed live as a JSON "Director's Script" and a human-readable "Director's Log."

**Why this priority**: This is the core differentiator. Delivering this story produces actual "brainrot" output even if no fancy UI is attached, proving the intelligence layer works.

**Independent Test**: Run the engine on a 30-second test video with known action events; assert that (a) the Director's Script JSON contains at least 3 sound entries, (b) all timestamps respect the token-bucket constraints, (c) at least one entry demonstrates ironic pairing (e.g., orchestral swell on trivial action), and (d) the live streams appear in the UI during processing.

**Acceptance Scenarios**:

1. **Given** a video with at least one detectable trigger event, **When** the engine runs, **Then** the Director's Script assigns a sound to every identified trigger, each with a valid timestamp, sound ID, action vector, and selection rationale field.
2. **Given** two triggers occur within 2 seconds of each other, **When** the token-bucket algorithm evaluates them, **Then** only one sound is scheduled; the skipped trigger is logged in the System Audit Box with the conflict resolution note.
3. **Given** the engine has no clear sound match for a trigger, **When** it resolves the ambiguity, **Then** it logs the "dice roll" reasoning in the System Audit Box and still produces a valid output.
4. **Given** "Aggressive Visuals" is enabled, **When** a sound is placed, **Then** an independent visual effect (deep-fry, snap-zoom, motion blur, or overlay) is also scheduled at that timestamp, and the pairing is recorded in the Director's Script.
5. **Given** the video has no detectable trigger for 10+ seconds, **When** the token-bucket timeout fires, **Then** the system places a sound anyway and logs it as a "fallback" event.

---

### User Story 3 – Final Video Render & Download (Priority: P3)

After the engine completes, a "system glitch" transition animation plays and the Reveal Page displays the finished video in a CRT monitor graphic frame. The original audio track is fully replaced by the meme soundtrack. The Director's Script JSON is shown in a scrollable panel. The user can download the final MP4 and the JSON metadata, or "Wipe Buffer" to start over.

**Why this priority**: Delivering a downloadable result converts the processing work into tangible user value—the shareable artefact.

**Independent Test**: Process a test video end-to-end; verify that (a) the downloaded MP4 contains no original audio track, (b) the meme sounds appear at the correct timestamps, (c) the JSON file matches the Director's Script shown on screen, and (d) "Wipe Buffer" resets all state to the Source Page.

**Acceptance Scenarios**:

1. **Given** the engine has completed, **When** the Reveal Page loads, **Then** the video player shows the final video with a CRT monitor graphic overlay and the glitch transition animation plays exactly once.
2. **Given** the Reveal Page is visible, **When** the user clicks the MP4 download button, **Then** a valid MP4 is downloaded with the original audio removed and meme sounds embedded at the correct timestamps.
3. **Given** the Reveal Page is visible, **When** the user clicks the JSON download button, **Then** a valid JSON file is downloaded that exactly matches the Director's Script shown in the scrollable panel.
4. **Given** the Reveal Page is visible, **When** the user clicks "Wipe Buffer," **Then** all session state is cleared and the Source Page is displayed in its initial empty state.

---

### User Story 4 – Retro Terminal UI & Real-Time Engine Dashboard (Priority: P4)

The entire application consistently renders in the Matrix Green retro-terminal aesthetic: monospaced font, scanline overlay, CRT spherical-bulge edges, double-line ASCII borders, and flickering cursor elements. The Engine Page additionally displays a real-time Hardware Monitor dashboard showing inference latency and hardware load, and a System Audit Box console.

**Why this priority**: The aesthetic is a core product differentiator and directly shapes user perception of the tool's seriousness and comedy value. It can be validated as a visual layer on top of P1–P3 functionality.

**Independent Test**: Load each of the three pages; verify that (a) the font is monospaced throughout, (b) the scanline animation is visible, (c) all borders use double-line ASCII characters, (d) the Hardware Monitor updates in real time during processing.

**Acceptance Scenarios**:

1. **Given** any page is loaded, **When** the user views it, **Then** the background is black, all primary text is Matrix Green (#00FF41 or equivalent), the font is monospaced, and a scanline overlay animation is visible.
2. **Given** the Engine Page is active, **When** inference is running, **Then** the Hardware Monitor updates at least once per second showing inference latency in milliseconds and a CPU/GPU load percentage.
3. **Given** the Engine Page is active, **When** a conflict is resolved, **Then** the System Audit Box appends the resolution note within 500 ms of the decision.
4. **Given** the Reveal Page is active, **When** the video player is displayed, **Then** it is visually framed by a CRT monitor graphic with the spherical-bulge effect applied.

---

### User Story 5 – ANON Authentication & User Identity (Priority: P5)

Per the PoMemeVideo Constitution, the application supports Microsoft OAuth login for both development and production environments. During local development, an "ANON" button is available that generates a unique random-suffix identity (e.g., `ANON463443`). When logged in via Microsoft, the user's email appears in the navigation bar. When using ANON, "ANON LOGGED IN" is displayed instead. All user sessions and any future user-specific data are associated with the correct identity.

**Why this priority**: Authentication is infrastructure. While the core video pipeline (P1–P3) can be tested without auth, production deployments require it. It is a constitutional mandate.

**Independent Test**: Click ANON login twice in separate sessions; verify that (a) two distinct ANON usernames are generated, (b) "ANON LOGGED IN" appears in the nav bar, (c) Microsoft OAuth login shows the user's email in the nav bar.

**Acceptance Scenarios**:

1. **Given** the app is running in Development mode, **When** the user clicks "ANON," **Then** a unique username with a random numeric suffix is generated, the session is started, and "ANON LOGGED IN" appears in the nav bar.
2. **Given** the app is running in any environment, **When** the user signs in via Microsoft OAuth, **Then** the user's email address is displayed in the nav bar.
3. **Given** two separate ANON logins occur, **When** both usernames are compared, **Then** the numeric suffixes are different with high probability (collision rate < 1 in 1,000,000).
4. **Given** an ANON user generates output, **When** future user-specific data is stored, **Then** it is attributed to the ANON account (not anonymous/null).

---

### Edge Cases

- What happens when a video file has no audio track? The system should proceed normally—original audio removal is a no-op and the meme soundtrack is applied as usual.
- What happens when the video has no detectable trigger events at all? The fallback token-bucket timer places sounds at regular intervals and logs all placements as "fallback" events.
- What happens when the user closes the browser during engine processing? The session state is preserved for the duration of the server session; on reload, the user is returned to the Engine Page if processing is still running, or to the Reveal Page if completed.
- What happens when the sound library is unavailable (network error in cloud-extended mode)? The system falls back to local-only analysis and notifies the user via the System Audit Box.
- What happens if the uploaded video exceeds 500 MB? The system rejects the file at the drop zone before any processing begins, displays an ASCII-styled error message with the size limit stated explicitly, and invites the user to retry with a smaller file.
- What happens when a video contains only static frames (no motion)? The system logs "no motion triggers detected," applies audio via fallback timing, and proceeds to render.

---

## Requirements *(mandatory)*

### Functional Requirements

**Stage 1 – Ingestion**

- **FR-001**: The system MUST accept video files in MP4, MOV, AVI, and WebM formats via drag-and-drop on the Source Page.
- **FR-002**: The system MUST validate uploaded files and display a retro-styled error in the drop zone for unsupported or corrupt files.
- **FR-002a**: The system MUST reject any video file exceeding **500 MB** at the drop zone, before any server-side processing begins, and display an ASCII-styled error stating the limit.
- **FR-003**: The system MUST extract one keyframe every 3 seconds from the ingested video.
- **FR-004**: The system MUST apply a 1-bit, green-scale dithering algorithm to every extracted keyframe and display them in a horizontal strip inside an ASCII-bordered preview zone.
- **FR-005**: The Source Page MUST provide an "Aggressive Visuals" toggle that primes the engine for visual effect improvisation.
- **FR-006**: The system MUST display a Matrix Green retro-terminal aesthetic on all pages: monospaced font, scanline overlay, CRT spherical-bulge edge effect, double-line ASCII borders.

**Stage 2 – Engine**

- **FR-007**: Upon "Initiate," the system MUST transition to the Engine Page and begin real-time analysis of the video for semantic visual triggers (falls, surprised expressions, sudden movements, confused/ironic moments) using Azure OpenAI GPT-4o Vision as the inference backend.
- **FR-008**: The system MUST match identified triggers to sounds from a library of 200+ classified meme sounds stored in Azure Blob Storage (PoMemeVideo RG), with catalogue metadata (sound ID, display name, duration, action vector tags, blob URL) held in Azure Table Storage (PoMemeVideo RG). Each sound is tagged with Action Vectors (e.g., `thud`, `fail`, `oof`, `confused`, `ironic`).
- **FR-009**: The system MUST stream the Director's Log (human-readable reasoning) to a scrolling terminal feed on the right side of the Engine Page in real time via a SignalR hub.
- **FR-010**: The system MUST stream the Director's Script (raw JSON containing sound ID, timestamp, action vector, visual effect, and rationale) to a rapid-fire text feed on the left side of the Engine Page in real time via the same SignalR hub.
- **FR-011**: The system MUST enforce Token-Bucket Timing: minimum 2-second gap between sounds, maximum 10-second gap, with a target average of one sound per 5 seconds.
- **FR-012**: When the gap between triggers exceeds 10 seconds, the system MUST place a fallback sound and log the event in the System Audit Box.
- **FR-013**: When two triggers conflict within the 2-second minimum gap, the system MUST resolve the conflict by choosing one sound, log the "dice roll" reasoning in the System Audit Box.
- **FR-014**: The Engine Page MUST display a real-time Hardware Monitor showing inference latency (ms) and CPU/GPU load (%), updated at least once per second.
- **FR-015**: When "Aggressive Visuals" is enabled, the system MUST independently improvise and schedule one visual effect per sound trigger: deep-fry, snap-zoom (200%–300%), motion blur, or overlay (e.g., "Wasted" text, floating question marks).
- **FR-016**: If mock data mode is active, the UI MUST display a prominent "MOCK DATA" banner at the top of the Engine and Reveal pages.

**Stage 3 – Reveal**

- **FR-017**: After processing completes, the system MUST play a "system glitch" transition animation (flickering green text + screen reset) exactly once before displaying the Reveal Page.
- **FR-018**: The Reveal Page MUST display the final video in a central player framed by a CRT monitor graphic with spherical-bulge edges.
- **FR-019**: The final video's original audio MUST be completely removed and replaced by the meme soundtrack generated from the Director's Script.
- **FR-020**: When "Aggressive Visuals" was enabled, the visual effects scheduled in the Director's Script MUST be rendered into the final video.
- **FR-021**: The Director's Script JSON MUST be presented in a scrollable, syntax-highlighted panel beside the video player on the Reveal Page.
- **FR-022**: The Reveal Page MUST provide download buttons for (a) the final MP4 and (b) the JSON Director's Script metadata file.
- **FR-023**: A "Wipe Buffer" button MUST clear all session state and return the user to the Source Page in its initial empty state.

**System & Infrastructure**

- **FR-024**: The application MUST expose a `/health` JSON endpoint reporting the status of all external service connections (Azure OpenAI, Azure Blob Storage sound library, Azure Table Storage).
- **FR-025**: The application MUST expose a `/diag` page showing all external connection statuses and configuration keys in use, with middle characters of sensitive values masked.
- **FR-026**: All secrets (API keys, OAuth credentials) MUST be sourced from Azure Key Vault; none may be stored in `appsettings.json`.
- **FR-027**: The application MUST support Microsoft OAuth login in both Development and Production environments.
- **FR-028**: In Development mode, the application MUST provide an "ANON" login button that creates a unique session identity with a random numeric suffix.
- **FR-029**: Real AI service calls MUST only be made when the application is running in Development or Production mode with a live user; Integration and E2E tests MUST use mock responses.
- **FR-030**: Local development MUST use Azurite running in Docker for any storage simulation.
- **FR-031**: The Azure App Service deployment MUST enable the platform health-check feature pointed at `/health` to support automatic instance recycling when uptime falls below the 99.9% monthly target.

### Key Entities

- **VideoSession**: Represents a single user processing run. Attributes: session ID, user identity, uploaded file reference, video duration, creation timestamp, status (Ingesting / Processing / Complete / Error), aggressive visuals flag.
- **Keyframe**: A dithered preview image extracted from the video. Attributes: session ID, sequence number, timestamp offset (seconds), image data (1-bit green-scale).
- **DirectorScript**: The AI's complete output plan. Attributes: session ID, generation timestamp, list of `ScriptEntry` items, total sound count, density metrics.
- **ScriptEntry**: A single meme event in the Director's Script. Attributes: entry ID, session ID, timestamp (ms), sound ID, action vector tags, selection rationale, visual effect (nullable), effect type, irony flag.
- **SoundAsset**: A catalogued meme sound. Attributes: sound ID, display name, duration (ms), action vector tags (array), blob storage URL. Catalogue metadata persisted in Azure Table Storage; audio files stored in Azure Blob Storage (both in PoMemeVideo RG).
- **UserIdentity**: The authenticated or ANON user. Attributes: identity ID, type (Microsoft / ANON), display name (email or "ANONxxxxxx"), created at.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can go from dropping a video file to receiving a downloadable meme-enhanced MP4 in under 60 seconds for videos up to 60 seconds in length.
- **SC-002**: Every processed video contains at least one meme sound event per 10 seconds of footage (enforced by the token-bucket fallback).
- **SC-003**: The Director's Script JSON produced for every video is internally consistent — every `ScriptEntry` timestamp is unique, within the video's duration, and respects the 2-second minimum gap.
- **SC-004**: The retro-terminal aesthetic is perceptible on all three wizard pages — a usability check confirms all text is rendered in monospaced green-on-black with visible scanline animation.
- **SC-005**: The system handles concurrent sessions from at least 50 users without degradation in the Engine Page streaming experience.
- **SC-006**: ANON login generates a unique identifier with a collision probability of less than 1 in 1,000,000 across concurrent sessions.
- **SC-007**: The `/health` endpoint returns a valid JSON response within 500 ms under normal operating conditions.
- **SC-008**: 95% of E2E Playwright tests for the three-stage wizard complete successfully in headed Development mode.
- **SC-009**: The application achieves **99.9% monthly uptime** (≤ 43.8 minutes downtime per month), measured via the `/health` endpoint. The `/diag` page flags degraded state when any external dependency (Azure OpenAI, Blob Storage, Table Storage) is unreachable.

---

## Assumptions

- The application is PC-first and landscape-optimised; mobile responsiveness is out of scope for v1.
- The 200+ meme sound library is pre-curated and stored in **Azure Blob Storage** within the PoMemeVideo resource group. Sound catalogue metadata (sound ID, display name, duration, action vector tags) is stored in **Azure Table Storage** (PoMemeVideo RG). Audio file references in Table Storage point to Blob Storage URLs. The engine does not self-learn new sounds at runtime; library updates are done via asset deployment, not code deployment.
- The AI semantic trigger detection uses **Azure OpenAI GPT-4o Vision** (multimodal) as the inference backend, called from the server project via the Azure OpenAI SDK. The Key Vault secret is prefixed `PoMemeVideo-` (e.g., `PoMemeVideo-AzureOpenAI-Endpoint`, `PoMemeVideo-AzureOpenAI-Key`). Managed Identity is used in Azure; API key fallback is used in local development.
- Video files are processed server-side; the Blazor WASM client handles only UI interaction and progress streaming.
- Rendered video encoding (mixing audio into MP4) is performed server-side; FFmpeg or equivalent is available in the server environment.
- The CRT monitor graphic, scanline overlay, and spherical-bulge effect are implemented as CSS/SVG layers over the Blazor WASM UI, not post-processed into the video.
- The "Hardware Monitor" metrics reflect the server's CPU/GPU load during AI inference, streamed to the client via **SignalR**. The Director's Log, Director's Script JSON stream, System Audit Box events, and Hardware Monitor updates all share the same SignalR hub.
- The application is deployed to Azure App Service (App Service Plan from PoShared RG) with Azure Table Storage in the PoMemeVideo-specific resource group for session persistence.
- Local development uses Azurite in Docker for Table Storage simulation.
- Microsoft OAuth is the sole production authentication provider; ANON login is disabled at the infrastructure level in non-Development environments.
- AI integration tests and E2E Playwright tests run against mock AI responses; only manually triggered dev sessions use real AI calls.
