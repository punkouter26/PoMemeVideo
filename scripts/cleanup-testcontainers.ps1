<#
.SYNOPSIS
    Removes orphaned Docker containers created by Testcontainers.

.DESCRIPTION
    Idempotent. Safe to run before AND after `dotnet test`.
    Targets only containers whose names follow the Testcontainers convention:
        {project-or-prefix}-test-{image}-{16-32-char-hex}
    Excludes anything matching the dev docker-compose service name
    (`pomemevideo-azurite`) so local dev containers are never removed.

.PARAMETER DryRun
    If set, prints what would be removed without touching Docker.

.EXAMPLE
    .\SCRIPTS\cleanup-testcontainers.ps1            # remove all leaked testcontainers
    .\SCRIPTS\cleanup-testcontainers.ps1 -DryRun    # list only
#>

[CmdletBinding()]
param(
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

# ── Heuristics ───────────────────────────────────────────────────────────────
# Testcontainers default name pattern (verified against 4.5.0):
#   {WithName-or-assembly-name}-test-{image-name}-{16-32-char-hex-checksum}
# We match on the literal "-test-" segment plus a 16-32 hex suffix at the end.
$testContainerPattern = '^(?:.+)-test-[^-]+-[0-9a-f]{16,32}$'

# Containers we NEVER touch (managed elsewhere — e.g., docker compose dev stack).
$protectedNames = @(
    'pomemevideo-azurite'   # docker-compose.yml service
)

function Test-IsProtected {
    param([Parameter(Mandatory=$true)][string]$Name)
    foreach ($protected in $protectedNames) {
        if ($Name -eq $protected) { return $true }
    }
    return $false
}

# ── Discover ─────────────────────────────────────────────────────────────────
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    Write-Host "docker CLI not on PATH — skipping testcontainer cleanup." -ForegroundColor Yellow
    return 0
}

$all    = docker ps -a --format '{{.Names}}' 2>$null
$target = @($all | Where-Object {
    ($_ -match $testContainerPattern) -and -not (Test-IsProtected $_)
})

if (-not $target -or $target.Count -eq 0) {
    Write-Host "No orphaned Testcontainers found. ✓" -ForegroundColor Green
    return 0
}

Write-Host "Found $($target.Count) orphaned Testcontainer(s):" -ForegroundColor Cyan
$target | ForEach-Object { Write-Host "  - $_" }

if ($DryRun) {
    Write-Host "DryRun: would call 'docker container rm -f' on each." -ForegroundColor Yellow
    return 0
}

# ── Remove ───────────────────────────────────────────────────────────────────
$failed = @()
foreach ($name in $target) {
    Write-Host "  rm -f $name" -NoNewline
    docker container rm -f $name 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  ✓" -ForegroundColor Green
    } else {
        Write-Host "  ✗ (exit $LASTEXITCODE)" -ForegroundColor Red
        $failed += $name
    }
}

if ($failed.Count -gt 0) {
    Write-Host "Cleanup completed with $($failed.Count) failure(s): $($failed -join ', ')" -ForegroundColor Red
    return 1
}

Write-Host "Cleanup complete." -ForegroundColor Green
return 0
