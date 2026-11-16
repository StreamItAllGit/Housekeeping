@echo off
setlocal
for %%I in ("%~dp0..") do set "ROOT_DIR=%%~fI"
set "APP_EXE=%ROOT_DIR%\Housekeeping.exe"
if not exist "%APP_EXE%" set "APP_EXE=%ROOT_DIR%\app\Housekeeping.exe"
"%APP_EXE%" --status --data-dir "%ROOT_DIR%\data" --log-dir "%ROOT_DIR%\logs" %*
exit /b %ERRORLEVEL%
