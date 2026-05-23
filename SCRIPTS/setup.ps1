[CmdletBinding()]
param(
    [switch]$SkipWinget,
    [switch]$SkipPythonBootstrap,
    [switch]$SkipAzurite,
    [switch]$SkipKeys
)

$ErrorActionPreference = 'Stop'

function Test-Command {
    param([Parameter(Mandatory = $true)][string]$Name)
    return [bool](Get-Command $Name -ErrorAction SilentlyContinue)
}

function Ensure-WingetPackage {
    param(
        [Parameter(Mandatory = $true)][string]$Id,
        [Parameter(Mandatory = $true)][string]$Name
    )

    Write-Host "Checking $Name..."
    $installed = winget list --id $Id --exact | Out-String
    if ($installed -match [Regex]::Escape($Id)) {
        Write-Host "$Name already installed."
        return
    }

    Write-Host "Installing $Name via winget..."
    winget install --id $Id --exact --accept-source-agreements --accept-package-agreements
}

Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    if (-not $SkipWinget) {
        if (-not (Test-Command -Name 'winget')) {
            throw 'winget is required for first-run bootstrap. Install App Installer from Microsoft Store.'
        }

        Ensure-WingetPackage -Id 'Python.Python.3.12' -Name 'Python 3.12'
        Ensure-WingetPackage -Id 'Docker.DockerDesktop' -Name 'Docker Desktop'
        Ensure-WingetPackage -Id 'Gyan.FFmpeg' -Name 'FFmpeg'
    }

    if (-not $SkipAzurite) {
        if (-not (Test-Command -Name 'docker')) {
            throw 'Docker CLI not found. Install Docker Desktop first.'
        }

        Write-Host 'Starting Azurite in Docker (if not already running)...'
        docker compose up -d azurite
    }

    if (-not $SkipKeys) {
        Write-Host 'Configuring local mock/fallback settings...'
        $apiSettingsPath = Join-Path $PWD 'src\PoMemeVideo.Api\appsettings.Development.json'
        if (Test-Path $apiSettingsPath) {
            $existing = Get-Content -Raw -Path $apiSettingsPath
            if ($existing -notmatch '"UseMockAI"') {
                Write-Warning 'appsettings.Development.json exists but does not include UseMockAI. Add it if you need deterministic local fallback.'
            }
        }
    }

    if (-not $SkipPythonBootstrap) {
        if (-not (Test-Command -Name 'python')) {
            throw 'Python is required to run setup-new-machine.py.'
        }

        Write-Host 'Running Python bootstrap (models/sounds/seeding)...'
        python SCRIPTS/setup-new-machine.py
    }

    Write-Host 'Setup completed.'
}
finally {
    Pop-Location
}
