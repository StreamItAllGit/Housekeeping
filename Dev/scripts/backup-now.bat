@echo off
setlocal
for %%I in ("%~dp0..") do set "ROOT_DIR=%%~fI"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference = 'Stop'; $root = '%ROOT_DIR%'; $data = Join-Path $root 'data'; $bk = Join-Path $root 'backups'; New-Item -ItemType Directory -Path $bk -Force | Out-Null; $stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMdd_HHmmss'); $arc = Join-Path $bk ('manual_backup_' + $stamp + '.7z'); $sevenZip = 'C:\Program Files\7-Zip\7z.exe'; if (Test-Path -LiteralPath $sevenZip) { & $sevenZip a -t7z -mx=9 $arc (Join-Path $data 'settings.json') (Join-Path $data 'history.json') | Out-Null; Write-Output ('Created .7z backup: ' + $arc) } else { Add-Type -AssemblyName System.IO.Compression.FileSystem; $zip = Join-Path $bk ('manual_backup_' + $stamp + '.zip'); [System.IO.Compression.ZipFile]::CreateFromDirectory($data, $zip); Write-Output ('Created zip backup: ' + $zip) }"
exit /b %ERRORLEVEL%
