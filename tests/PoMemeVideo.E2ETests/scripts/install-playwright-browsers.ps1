param(
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$e2eRoot = Resolve-Path (Join-Path $scriptRoot '..')
$browserRoot = Join-Path $e2eRoot '.playwright-browsers'
$localLock = Join-Path $browserRoot '__dirlock'

function Ensure-Markers {
    param([string]$RootDir)
    New-Item -Path (Join-Path $RootDir 'INSTALLATION_COMPLETE') -ItemType File -Force | Out-Null
    New-Item -Path (Join-Path $RootDir 'DEPENDENCIES_VALIDATED') -ItemType File -Force | Out-Null
}

function Install-WithFallback {
    param(
        [string]$BrowsersJsonPath,
        [string]$TargetRoot
    )

    $meta = Get-Content $BrowsersJsonPath -Raw | ConvertFrom-Json
    $chromiumMeta = $meta.browsers | Where-Object { $_.name -eq 'chromium' } | Select-Object -First 1
    $headlessMeta = $meta.browsers | Where-Object { $_.name -eq 'chromium-headless-shell' } | Select-Object -First 1

    if (-not $chromiumMeta -or -not $headlessMeta) {
        throw 'Unable to find chromium metadata in browsers.json'
    }

    $version = $chromiumMeta.browserVersion
    $revision = $chromiumMeta.revision

    $chromiumDir = Join-Path $TargetRoot ("chromium-{0}" -f $revision)
    $headlessDir = Join-Path $TargetRoot ("chromium_headless_shell-{0}" -f $revision)

    $chromeExe = Join-Path $chromiumDir 'chrome-win64\chrome.exe'
    $headlessExe = Join-Path $headlessDir 'chrome-headless-shell-win64\chrome-headless-shell.exe'

    if (-not (Test-Path $chromeExe)) {
        $chromeZip = Join-Path $env:TEMP ("chromium-{0}.zip" -f $revision)
        $chromeUrl = "https://storage.googleapis.com/chrome-for-testing-public/$version/win64/chrome-win64.zip"
        Write-Host "Fallback download: $chromeUrl"
        Invoke-WebRequest $chromeUrl -OutFile $chromeZip -UseBasicParsing
        Remove-Item $chromiumDir -Recurse -Force -ErrorAction SilentlyContinue
        Expand-Archive -Path $chromeZip -DestinationPath $chromiumDir -Force
        Remove-Item $chromeZip -Force -ErrorAction SilentlyContinue
        Ensure-Markers -RootDir $chromiumDir
    }

    if (-not (Test-Path $headlessExe)) {
        $headlessZip = Join-Path $env:TEMP ("chromium-headless-shell-{0}.zip" -f $revision)
        $headlessUrl = "https://storage.googleapis.com/chrome-for-testing-public/$version/win64/chrome-headless-shell-win64.zip"
        Write-Host "Fallback download: $headlessUrl"
        Invoke-WebRequest $headlessUrl -OutFile $headlessZip -UseBasicParsing
        Remove-Item $headlessDir -Recurse -Force -ErrorAction SilentlyContinue
        Expand-Archive -Path $headlessZip -DestinationPath $headlessDir -Force
        Remove-Item $headlessZip -Force -ErrorAction SilentlyContinue
        Ensure-Markers -RootDir $headlessDir
    }

    return @{
        ChromeExe = $chromeExe
        HeadlessExe = $headlessExe
    }
}

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
    Write-Host 'Installing Chromium + headless shell for this Playwright version (deterministic extraction)...'
    if (Test-Path $localLock) {
        Remove-Item -Path $localLock -Recurse -Force -ErrorAction SilentlyContinue
    }

    $browsersJson = Join-Path $e2eRoot 'node_modules\playwright-core\browsers.json'
    $installed = Install-WithFallback -BrowsersJsonPath $browsersJson -TargetRoot $browserRoot

    if (-not (Test-Path $installed.ChromeExe)) {
        throw 'chrome.exe was not found after installation'
    }

    if (-not (Test-Path $installed.HeadlessExe)) {
        throw 'chrome-headless-shell.exe was not found after installation'
    }

    Write-Host "Found Chrome: $($installed.ChromeExe)"
    Write-Host "Found Headless Shell: $($installed.HeadlessExe)"
    Write-Host 'Playwright browser installation completed successfully.'
}
finally {
    Pop-Location
}
