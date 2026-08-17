@echo off
setlocal enabledelayedexpansion

REM Call nem run with this script's name and all arguments
nem run %~n0 %*

REM Propagate exit code
exit /b %errorlevel%
