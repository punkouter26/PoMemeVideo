# Quickstart: PoMemeVideo – Local Development

**Date**: 2026-05-05

---

## Prerequisites

| Tool | Version | Notes |
|------|---------|-------|
| .NET SDK | 10.x (pinned via `global.json`) | `dotnet --version` must show 10.x |
| Docker Desktop | Latest | Required for Azurite + FFmpeg container |
| Ollama | Latest | Must be running on `localhost:11434` |
| Node.js | 20 LTS | For Playwright E2E tests |
| Azure CLI | Latest | For Key Vault + Managed Identity setup |
| VS Code | Latest | With C# Dev Kit extension |

---

## 1. Clone & Restore

```bash
git clone <repo-url>
cd PoMemeVideo
dotnet restore
```

---

## 2. Start Azurite (Docker)

```bash
docker run -d --name azurite \
  -p 10000:10000 -p 10001:10001 -p 10002:10002 \
  mcr.microsoft.com/azure-storage/azurite
```

Azurite provides local Table Storage and Blob Storage on:
- Blob: `http://127.0.0.1:10000/devstoreaccount1`
- Queue: `http://127.0.0.1:10001/devstoreaccount1`
- Table: `http://127.0.0.1:10002/devstoreaccount1`

---

## 3. Start Ollama with Gemma 4

```bash
ollama pull gemma4
ollama serve
```

Verify: `curl http://localhost:11434/api/tags` returns model list.

---

## 4. Configure appsettings.Development.json

Copy the template and fill in your values. **Never commit this file.**

```json
{
  "AzureAiVision": {
    "Endpoint": "https://<your-vision-resource>.cognitiveservices.azure.com/",
    "Key": "<your-key>"
  },
  "AzureOpenAI": {
    "Endpoint": "https://<your-openai-resource>.openai.azure.com/",
    "Key": "<your-key>"
  },
  "Ollama": {
    "BaseUrl": "http://localhost:11434"
  },
  "AzureStorage": {
    "ConnectionString": "UseDevelopmentStorage=true"
  },
  "FeatureFlags": {
    "UseMockAI": false
  }
}
```

> Set `UseMockAI: true` to run the full pipeline without real AI calls (uses pre-baked mock responses). A **MOCK DATA** banner will appear in the UI.

---

## 5. Seed the Sound Library (first run)

```bash
dotnet run --project src/PoMemeVideo.Api -- seed-sounds
```

This imports the 200+ sound asset metadata records into local Azurite Table Storage and uploads audio files to local Azurite Blob Storage. Run once per fresh Azurite instance.

---

## 6. Run the Application (F5 or CLI)

**VS Code**: Press `F5` — the launch task kills any existing .NET processes, starts the server on `https://localhost:5001`, and opens Edge.

**CLI**:
```bash
dotnet run --project src/PoMemeVideo.Api
```

Server available at:
- HTTP:  `http://localhost:5000`
- HTTPS: `https://localhost:5001`
- OpenAPI (Scalar): `https://localhost:5001/scalar`
- Health: `https://localhost:5001/health`
- Diag:  `https://localhost:5001/diag`

---

## 7. ANON Login (Development)

1. Navigate to `https://localhost:5001`.
2. Click the **ANON** button on the login page.
3. A unique identity (e.g., `ANON463443`) is created and displayed as `ANON LOGGED IN` in the nav bar.
4. To test Microsoft OAuth, click **Sign in with Microsoft** instead.

---

## 8. Run Tests

```bash
# Unit tests
dotnet test tests/PoMemeVideo.UnitTests

# Integration tests (requires Azurite running)
dotnet test tests/PoMemeVideo.IntegrationTests

# E2E tests (headed mode, requires app running on localhost:5001)
cd tests/PoMemeVideo.E2ETests
npm install
npx playwright test --headed
```

---

## 9. Useful .http Files

`.http` files are located at `src/PoMemeVideo.Api/Features/{Slice}/{Slice}.http`.

| File | Purpose |
|------|---------|
| `Ingestion/Ingestion.http` | Request SAS token, confirm upload |
| `Processing/Processing.http` | Initiate engine, poll session status |
| `MemeLibrary/MemeLibrary.http` | Browse sound catalogue |
| `Output/Output.http` | Download video, download script JSON |

---

## 10. Common Troubleshooting

| Symptom | Fix |
|---------|-----|
| `Connection refused` on Azurite | Run step 2; verify ports 10000–10002 are free |
| Ollama `model not found` | Run `ollama pull gemma4` |
| `TrustFailure` on HTTPS | Run `dotnet dev-certs https --trust` |
| Engine page blank / no SignalR stream | Check browser console; ensure server is running before navigating to Engine page |
| `UseMockAI` has no effect | Confirm `appsettings.Development.json` is in the `PoMemeVideo.Api` project directory |
