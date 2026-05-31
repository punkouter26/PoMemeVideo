param(
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$e2eRoot = Resolve-Path (Join-Path $scriptRoot '..')
$browserRoot = Join-Path $e2eRoot '.playwright-browsers'
$localLock = Join-Path $browserRoot '__dirlock'

Write-Host "E2E root: $e2eRoot"
Write-Host "Browser cache: $browserRoot"

if ($Force -and (Test-Path $browserRoot)) {
    Write-Host 'Force mode: removing existing local browser cache'
    Remove-Item -Path $browserRoot -Recurse -Force -ErrorAction SilentlyContinue
}

if (-not (Test-Path $browserRoot)) {
    New-Item -Path $browserRoot -ItemType Directory | Out-Null
}

if (Test-Path $localLock) {
    # Only clear stale local lock files older than 10 minutes.
    $ageMinutes = ((Get-Date) - (Get-Item $localLock).LastWriteTime).TotalMinutes
    if ($ageMinutes -gt 10) {
        Write-Host "Removing stale local lock file ($([math]::Round($ageMinutes, 1)) min old)"
        Remove-Item -Path $localLock -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$env:PLAYWRIGHT_BROWSERS_PATH = $browserRoot

Push-Location $e2eRoot
try {
    Write-Host 'Installing Chromium + headless shell for this Playwright version...'
    npx playwright install --force chromium
    if ($LASTEXITCODE -ne 0) {
        throw "playwright install failed with exit code $LASTEXITCODE"
    }

    $chromeExe = Get-ChildItem -Path $browserRoot -Filter 'chrome.exe' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    $headlessExe = Get-ChildItem -Path $browserRoot -Filter 'chrome-headless-shell.exe' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1

    if (-not $chromeExe) {
        throw 'chrome.exe was not found after installation'
    }

    if (-not $headlessExe) {
        throw 'chrome-headless-shell.exe was not found after installation'
    }

    Write-Host "Found Chrome: $($chromeExe.FullName)"
    Write-Host "Found Headless Shell: $($headlessExe.FullName)"
    Write-Host 'Playwright browser installation completed successfully.'
}
finally {
    Pop-Location
}
