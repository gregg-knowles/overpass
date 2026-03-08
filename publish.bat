@echo off
setlocal

set OUT=publish
if not exist "%OUT%" mkdir "%OUT%"

echo.
echo === Building self-contained (standalone, ~180MB) ===
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "%OUT%\standalone"
if errorlevel 1 goto :fail

echo.
echo === Building framework-dependent (requires .NET 8, ~25MB) ===
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o "%OUT%\small"
if errorlevel 1 goto :fail

echo.
echo Done!
echo   Standalone: %OUT%\standalone\SatelliteEyesWin.exe
echo   Small:      %OUT%\small\SatelliteEyesWin.exe
echo.
for %%F in ("%OUT%\standalone\SatelliteEyesWin.exe") do echo   Standalone size: %%~zF bytes
for %%F in ("%OUT%\small\SatelliteEyesWin.exe") do echo   Small size:      %%~zF bytes
exit /b 0

:fail
echo Build failed!
exit /b 1
