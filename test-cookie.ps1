$ErrorActionPreference = 'Stop'
$cookieJar = Join-Path $env:TEMP "cookies.txt"
Remove-Item $cookieJar -ErrorAction SilentlyContinue
$exe = "$env:WINDIR\system32\curl.exe"

# 1. Acquire cookie
& $exe -s -i -c $cookieJar "http://localhost:7000/api/output/sessions/3764008b-7fcc-44c0-b764-971c485e982e/script" | Out-Null

Write-Host "=== Cookies ==="
Get-Content $cookieJar

$mp4 = Join-Path $env:TEMP "rendered.mp4"
Remove-Item $mp4 -ErrorAction SilentlyContinue
& $exe -s -b $cookieJar -o $mp4 "http://localhost:7000/api/output/sessions/3764008b-7fcc-44c0-b764-971c485e982e/stream/video"
Get-Item $mp4 | Format-List Length
