# scripts/check-test-budgets.ps1
# Enforces test suite quotas: Unit <= 100, Integration <= 50, E2E API <= 25, E2E UI <= 25

$ErrorActionPreference = 'Stop'

$BUDGETS = @{
    "PoMemeVideo.UnitTests"        = 100
    "PoMemeVideo.IntegrationTests" = 50
    "PoMemeVideo.E2EAPI"           = 25
    "PoMemeVideo.E2EUI"            = 25
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " PoMemeVideo Test Suite Budget Audit    " -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$hasViolation = $false

foreach ($proj in $BUDGETS.Keys) {
    $projPath = Join-Path "tests" $proj
    if (-not (Test-Path $projPath)) {
        Write-Warning "Test project not found at $projPath"
        continue
    }

    # Count [Fact] and [Theory] occurrences across all *.cs test files
    $files = Get-ChildItem -Path $projPath -Filter "*.cs" -Recurse | Where-Object { $_.FullName -notmatch "[\\/]obj[\\/]" -and $_.FullName -notmatch "[\\/]bin[\\/]" }
    $testCount = 0
    foreach ($file in $files) {
        $content = Get-Content $file.FullName -Raw
        $facts = ([regex]::Matches($content, '\[Fact\b')).Count
        $theories = ([regex]::Matches($content, '\[Theory\b')).Count
        $testCount += ($facts + $theories)
    }

    $limit = $BUDGETS[$proj]
    $status = if ($testCount -le $limit) { "[OK]" } else { "[BUDGET EXCEEDED]" }
    $color = if ($testCount -le $limit) { "Green" } else { "Red" }

    Write-Host ("{0,-30} Count: {1,3} / Limit: {2,3}  {3}" -f $proj, $testCount, $limit, $status) -ForegroundColor $color

    if ($testCount -gt $limit) {
        $hasViolation = $true
    }
}

Write-Host "========================================" -ForegroundColor Cyan

if ($hasViolation) {
    Write-Error "Test budget policy violated! Prune redundant or oversized tests."
    exit 1
} else {
    Write-Host "All test suites conform to strict size budgets." -ForegroundColor Green
    exit 0
}
