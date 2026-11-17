@echo off
rem Build the desktop app. Real logic lives in build.ps1 (UTF-8 with BOM, so Chinese paths are safe).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
if errorlevel 1 pause
