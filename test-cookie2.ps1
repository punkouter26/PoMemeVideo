$ErrorActionPreference = 'Stop'
$sessionId = "3193356e-d18c-4694-9a26-be6cf96fadb3"
$exe = "$env:WINDIR\system32\curl.exe"

# Hit the endpoint and capture both headers + body
$tmpfile = Join-Path $env:TEMP "headers.txt"
Remove-Item $tmpfile -ErrorAction SilentlyContinue
& $exe -s -D $tmpfile -o $null "http://localhost:7000/api/output/sessions/$sessionId/script"
$cookieLine = Get-Content $tmpfile | Where-Object { $_ -match 'Set-Cookie:.*PmvDevAnon' } | Select-Object -First 1
Write-Host "Cookie line: $cookieLine"
if ($cookieLine -match 'PmvDevAnon=([^;]+)') {
    $devAnonValue = $matches[1]
    Write-Host "Got PmvDevAnon value: $devAnonValue"

    $mp4 = Join-Path $env:TEMP "rendered.mp4"
    Remove-Item $mp4 -ErrorAction SilentlyContinue
    & $exe -s -o $mp4 -H "Cookie: PmvDevAnon=$devAnonValue" "http://localhost:7000/api/output/sessions/$sessionId/stream/video"
    $len = (Get-Item $mp4).Length
    Write-Host "Downloaded: $len bytes"
    if ($len -gt 1000) {
        & "C:\Users\punko\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-8.1.2-full_build\bin\ffprobe.exe" -v error -show_streams $mp4 2>&1 | Select-Object -First 25
    }
}
