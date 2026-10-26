@echo off
cd /d "%~dp0"
start "" "D:\C#\VM\tools\VirtualLineConsole\bin\Debug\net48\VirtualLineConsole.exe"
timeout /t 2 /nobreak >nul
start "" "D:\C#\VM\VisionMaster\bin\Debug\net9.0-windows\VisionMaster.exe"
