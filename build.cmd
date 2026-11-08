@echo off
setlocal
rem Release build of the tool. Output: .\dist\SolutionComponentComparer.dll
rem All paths are relative to this script.
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo The .NET SDK ^(dotnet^) was not found. Install it from https://dotnet.microsoft.com/download
  exit /b 1
)

dotnet build "src\SolutionComponentComparer\SolutionComponentComparer.csproj" -c Release
if errorlevel 1 (
  echo BUILD FAILED.
  exit /b 1
)

if not exist "dist" mkdir "dist"
copy /Y "src\SolutionComponentComparer\bin\Release\SolutionComponentComparer.dll" "dist\" >nul
if errorlevel 1 (
  echo Copy to dist failed.
  exit /b 1
)

echo.
echo Built: %~dp0dist\SolutionComponentComparer.dll
