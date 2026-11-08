@echo off
setlocal
for %%I in ("%~dp0..\..") do set "ROOT_DIR=%%~fI"
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-Content -Path '%ROOT_DIR%\logs\Housekeeping_Live.log' -Wait -Tail 40"
