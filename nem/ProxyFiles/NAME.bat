@echo off
REM nem proxy: asks nem which copy of this tool to run here (installing the env
REM first if it has to), then runs it itself - nem is not running while the tool
REM does. setlocal keeps the env's variables out of the calling cmd; delayed
REM expansion stays off so a '!' in a path survives.
setlocal disabledelayedexpansion

set "NEM_TOOL="
set "NEM_EXIT="
for /f "delims=" %%L in ('nem resolve "%~n0" --shell cmd') do %%L
if defined NEM_EXIT exit /b %NEM_EXIT%
if not defined NEM_TOOL exit /b 1

call "%NEM_TOOL%" %*
exit /b %errorlevel%
