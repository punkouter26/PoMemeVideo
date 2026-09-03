<#
PoMemeVideo end-to-end test drive.

Uploads BODY_Matt_SimpleMove.mp4 through the full ingestion -> processing ->
output pipeline against a running Development API on http://localhost:5280.

Assumptions:
- Azurite running on 10000/10001/10002 (matching docker-compose.yml)
- API started with UseMockAI=true (Azure AI calls intercepted locally, no tokens burned)
- Sound library has been seeded via python scripts/seed-meme-sounds.py
#>

$ErrorActionPreference = 'Stop'

$Base = 'http://localhost:5280'
$VideoPath = 'C:\Users\punko\Downloads\PoMemeVideo\BODY_Matt_SimpleMove.mp4'

if (-not (Test-Path $VideoPath)) {
    throw "Test video not found: $VideoPath"
}

$VideoBytes = (Get-Item $VideoPath).Length
$VideoName = Split-Path -Leaf $VideoPath
Write-Host "Test video: $VideoPath ($VideoBytes bytes)" -ForegroundColor Cyan

# ── 1. Create a persistent cookie jar so guest login sticks ──────────────────
$Jar = Join-Path $env:TEMP "pomemevideo-e2e-cookies-$([guid]::NewGuid()).txt"
$Session = [Microsoft.PowerShell.Commands.WebRequestSession]::new()

# ── 2. Sign in as GUEST (Development-only path) ──────────────────────────────
Write-Host "`n=== STEP 1: Authenticate as GUEST ===" -ForegroundColor Yellow
$Login = Invoke-RestMethod -Uri "$Base/auth/guest" -Method Post -WebSession $Session -UseBasicParsing
Write-Host "Guest login OK: $($Login.displayName) ($($Login.identityType))"

# Verify identity propagated
$Me = Invoke-RestMethod -Uri "$Base/api/auth/me" -WebSession $Session -UseBasicParsing
Write-Host "Identity: $($Me.displayName) / $($Me.email)"

# ── 3. Verify the API is healthy and storage is wired ───────────────────────
Write-Host "`n=== STEP 2: Health check ===" -ForegroundColor Yellow
$Cfg = Invoke-RestMethod -Uri "$Base/api/config" -WebSession $Session -UseBasicParsing
Write-Host ("Config: env={0} storage={1} isDev={2}" -f $Cfg.environment, $Cfg.storageStatus, $Cfg.isDevelopment)

# ── 4. Seed the sound library if it's empty ─────────────────────────────────
Write-Host "`n=== STEP 3: Seed sound library if needed ===" -ForegroundColor Yellow
$Sounds = Invoke-RestMethod -Uri "$Base/api/memelibrary/sounds?limit=1" -WebSession $Session -UseBasicParsing
if (-not $Sounds -or $Sounds.Count -eq 0 -or $Sounds.totalCount -eq 0) {
    Write-Host "Seeding sound library..."
    Invoke-RestMethod -Uri "$Base/api/memelibrary/seed" -Method Post -WebSession $Session -UseBasicParsing | Out-Null
    Start-Sleep -Seconds 2
}
$Sounds = Invoke-RestMethod -Uri "$Base/api/memelibrary/sounds?limit=1" -WebSession $Session -UseBasicParsing
Write-Host "Sound library count: $($Sounds.totalCount ?? $Sounds.Count)"

# ── 5. Request a SAS token for the upload ───────────────────────────────────
Write-Host "`n=== STEP 4: Request SAS token ===" -ForegroundColor Yellow
$SasBody = @{ fileName = $VideoName; fileSizeBytes = $VideoBytes } | ConvertTo-Json
$Sas = Invoke-RestMethod -Uri "$Base/api/ingestion/sas" -Method Post `
    -ContentType 'application/json' -Body $SasBody -WebSession $Session -UseBasicParsing

Write-Host ("SessionId = {0}" -f $Sas.sessionId)
Write-Host ("SAS URL  = {0}" -f ($Sas.sasUrl.Substring(0, [Math]::Min(80, $Sas.sasUrl.Length))) + '...')

# ── 6. Upload the MP4 directly to Azurite via the SAS URL ───────────────────
Write-Host "`n=== STEP 5: Upload BODY_Matt_SimpleMove.mp4 to blob storage ===" -ForegroundColor Yellow
$UploadStart = Get-Date
# Streaming PUT keeps memory flat for a 50 MB file. Azurite is strict about
# x-ms-blob-type: BlockBlob on direct PUT — without it you get a 400 with no
# body. Real Azure tolerates a missing header; the Blazor client always sets
# it (see src/PoMemeVideo.Client/Services/BlobUploadService.cs).
$UploadHeaders = @{ 'x-ms-blob-type' = 'BlockBlob' }
$UploadResult = Invoke-RestMethod -Uri $Sas.sasUrl -Method Put -InFile $VideoPath `
    -ContentType 'video/mp4' -Headers $UploadHeaders -UseBasicParsing
$UploadElapsed = (Get-Date) - $UploadStart
$UploadMBps = ($VideoBytes / 1MB) / [Math]::Max($UploadElapsed.TotalSeconds, 0.001)
Write-Host ("Upload OK in {0:F1}s ({1:F2} MB/s)" -f $UploadElapsed.TotalSeconds, $UploadMBps)

# ── 7. Probe the source duration so we can drive trim/length ────────────────
Write-Host "`n=== STEP 6: Probe source duration ===" -ForegroundColor Yellow
$Probe = & "C:\Users\punko\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0-full_build\bin\ffprobe.exe" `
    -v error -show_entries format=duration -of csv=p=0 $VideoPath 2>$null
$Duration = [Math]::Round([double]$Probe.Trim(), 2)
Write-Host "Source duration: $Duration s"

# ── 8. Confirm the session metadata with the API ─────────────────────────────
Write-Host "`n=== STEP 7: Confirm session metadata ===" -ForegroundColor Yellow
$ConfirmBody = @{
    sessionId            = $Sas.sessionId
    blobPath             = "sessions/$($Sas.sessionId)/source.mp4"
    videoDurationSeconds = $Duration
    aggressiveVisuals    = $false
    aspectRatio          = "16:9"
} | ConvertTo-Json

$Confirm = Invoke-RestMethod -Uri "$Base/api/ingestion/sessions" -Method Post `
    -ContentType 'application/json' -Body $ConfirmBody -WebSession $Session -UseBasicParsing
Write-Host "Session confirmed: $($Confirm.sessionId) status=$($Confirm.status)"

# ── 9. Upload a single placeholder frame so the engine has *some* vision input.
#       UseMockAI means even an empty frame array reaches the deterministic fallback
#       path (time-based placements every 2s up to 10s) which is the cheapest end-to-end.
Write-Host "`n=== STEP 8: Upload keyframes (using mocked AI vision) ===" -ForegroundColor Yellow

$Frame = New-Object System.Drawing.Bitmap 16, 9
$FrameBytes = [System.IO.File]::ReadAllBytes($VideoPath)  # not used as a frame
$Frame.Dispose()

# Construct a tiny PNG via .NET to keep the request shape realistic.
Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap 32, 18
$ms = New-Object System.IO.MemoryStream
$bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
$b64 = [Convert]::ToBase64String($ms.ToArray())
$bmp.Dispose(); $ms.Dispose()

$FramesPayload = @{ frames = @("data:image/png;base64,$b64") } | ConvertTo-Json
try {
    $Frames = Invoke-RestMethod `
        -Uri "$Base/api/ingestion/sessions/$($Sas.sessionId)/frames" `
        -Method Post -ContentType 'application/json' -Body $FramesPayload `
        -WebSession $Session -UseBasicParsing
    Write-Host ("Frames uploaded: stored={0} labels={1} mode={2}" -f `
        $Frames.visionDiagnostics.framesStored, $Frames.visionDiagnostics.labelsDetected,
        $Frames.visionDiagnostics.placementMode)
} catch {
    Write-Warning "Frame upload failed: $($_.Exception.Message)"
}

# ── 10. Initiate the engine run ─────────────────────────────────────────────
Write-Host "`n=== STEP 9: Initiate engine run ===" -ForegroundColor Yellow
$Initiate = Invoke-RestMethod `
    -Uri "$Base/api/processing/sessions/$($Sas.sessionId)/initiate" `
    -Method Post -WebSession $Session -UseBasicParsing
Write-Host "Engine initiated: $($Initiate.status)"

# ── 11. Poll the session until Complete / Error ─────────────────────────────
Write-Host "`n=== STEP 10: Poll until terminal status ===" -ForegroundColor Yellow
$Deadline = (Get-Date).AddMinutes(4)
$Status = 'Ingesting'
$PollCount = 0
while ((Get-Date) -lt $Deadline) {
    $PollCount++
    $SessionDto = Invoke-RestMethod `
        -Uri "$Base/api/ingestion/sessions/$($Sas.sessionId)" `
        -WebSession $Session -UseBasicParsing
    $Status = $SessionDto.status
    if ($Status -in @('Complete','Error')) { break }
    Start-Sleep -Seconds 2
}
Write-Host ("Final status after {0} polls: {1}" -f $PollCount, $Status)
if ($SessionDto.errorMessage) {
    Write-Warning "Error message: $($SessionDto.errorMessage)"
}
if ($SessionDto.outputBlobPath) {
    Write-Host "Output blob: $($SessionDto.outputBlobPath)"
}

# SessionStatus enum: Ingesting=0, Processing=1, Complete=2, Error=3.
# The JSON serializer in the API emits the integer value rather than the name unless
# the controller decorates with [JsonStringEnumConverter]. Accept both forms.
$IsTerminal = ($Status -in @('Complete','Error')) -or ([int]$Status -in @(2,3))

# ── 12. Download the rendered output if available ───────────────────────────
if ($IsTerminal -and [int]$Status -eq 2 -and $SessionDto.outputBlobPath) {
    Write-Host "`n=== STEP 11: Download rendered MP4 ===" -ForegroundColor Yellow
    $OutPath = Join-Path $env:TEMP "pomemevideo-$($Sas.sessionId).mp4"
    Invoke-RestMethod -Uri "$Base/api/output/sessions/$($Sas.sessionId)/download/video" `
        -Method Get -WebSession $Session -OutFile $OutPath -UseBasicParsing
    $OutBytes = (Get-Item $OutPath).Length
    Write-Host ("Downloaded: {0} ({1:F2} MB)" -f $OutPath, ($OutBytes / 1MB))
}

# ── 13. Pull the director's script for inspection ───────────────────────────
Write-Host "`n=== STEP 12: Fetch director's script ===" -ForegroundColor Yellow
try {
    $Script = Invoke-RestMethod `
        -Uri "$Base/api/output/sessions/$($Sas.sessionId)/script" `
        -WebSession $Session -UseBasicParsing
    Write-Host ("Director's script: {0} entry/entries" -f $Script.entries.Count)
    foreach ($e in $Script.entries | Select-Object -First 5) {
        Write-Host ("  t={0,5}ms  sound={1,-30}  rationale={2}" -f `
            $e.timestampMs, $e.soundName, $e.selectionRationale)
    }
} catch {
    Write-Warning "Script fetch failed: $($_.Exception.Message)"
}

Remove-Item $Jar -ErrorAction SilentlyContinue
Write-Host "`nDONE" -ForegroundColor Green
