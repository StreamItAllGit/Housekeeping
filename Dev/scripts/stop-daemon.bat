@echo off
setlocal
for %%I in ("%~dp0..") do set "ROOT_DIR=%%~fI"
powershell -NoProfile -ExecutionPolicy Bypass -Command "try { Invoke-RestMethod -Uri 'http://127.0.0.1:5178/api/v1/system/shutdown' -Method Post -TimeoutSec 3 | Out-Null; Write-Host 'Dispatched shutdown to Housekeeping daemon.' } catch { Write-Host 'Housekeeping daemon is not running.' }"
exit /b 0
