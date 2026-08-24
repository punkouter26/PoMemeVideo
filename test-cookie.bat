@echo off
set SESSION=3193356e-d18c-4694-9a26-be6cf96fadb3
set EXE=C:\WINDOWS\system32\curl.exe

REM Get the cookie from the script endpoint
%EXE% -s -i "http://localhost:7000/api/output/sessions/%SESSION%/script" > "%TEMP%\headers.txt"
type "%TEMP%\headers.txt" | findstr /I "PmvDevAnon"
