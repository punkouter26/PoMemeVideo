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

function Clear-Port {
    param([Parameter(Mandatory = $true)][int]$Port)
    $pids = netstat -ano 2>$null |
        Select-String -Pattern "0\.0\.0\.0:$Port\s|127\.0\.0\.1:$Port\s|\[::\]:$Port\s" |
        ForEach-Object { ($_ -split '\s+')[-1] } |
        Where-Object { $_ -match '^\d+$' } |
        Select-Object -Unique
    foreach ($p in $pids) {
        try {
            Stop-Process -Id $p -Force -ErrorAction SilentlyContinue
            Write-Host "Killed process $p holding port $Port."
        } catch { }
    }
}

Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    # ── Kill any orphaned dotnet processes on ports 5000/5001 (rule 4) ────────
    Write-Host 'Clearing ports 5000 and 5001...'
    Clear-Port -Port 5000
    Clear-Port -Port 5001

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

    # ── az login check — ensures Key Vault access matches Production (rule 9) ─
    Write-Host 'Checking Azure CLI login status...'
    if (-not (Test-Command -Name 'az')) {
        Write-Warning 'Azure CLI (az) not found. Install it via: winget install Microsoft.AzureCLI'
        Write-Warning 'Without az login, the app will fall back to appsettings.Development.json secrets instead of Key Vault.'
    }
    else {
        $azAccount = az account show 2>&1 | Out-String
        if ($azAccount -match '"state":\s*"Enabled"') {
            Write-Host 'Azure CLI: logged in. Key Vault access available.'
        }
        else {
            Write-Warning 'Azure CLI: not logged in. Run "az login" to enable Key Vault secret resolution.'
            Write-Warning 'Falling back to appsettings.Development.json for local secrets.'
        }
    }

    Write-Host 'Setup completed.'
}
finally {
    Pop-Location
}
