@echo off
setlocal
set "ScriptName=Setup.ps1"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0%ScriptName%" %*
set "ScriptExitCode=%ERRORLEVEL%"
if not "%ScriptExitCode%"=="0" (
    echo.
    echo ================================================================
    echo  PowerShell could not run %ScriptName% ^(exit code %ScriptExitCode%^).
    echo  The reason is printed above.
    echo  Launcher log: %TEMP%\Setup-LastError.log
    echo ================================================================
    echo PowerShell could not run %ScriptName% ^(exit code %ScriptExitCode%^).>>"%TEMP%\Setup-LastError.log"
    pause
)