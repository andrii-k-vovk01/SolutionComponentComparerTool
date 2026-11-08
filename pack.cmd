@echo off
setlocal
rem Release build + NuGet package for the XrmToolBox Tool Library.
rem Output: .\artifacts\AndriiK.XrmToolBox.SolutionComponentComparer.<version>.nupkg
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo The .NET SDK ^(dotnet^) was not found. Install it from https://dotnet.microsoft.com/download
  exit /b 1
)

dotnet pack "src\SolutionComponentComparer\SolutionComponentComparer.csproj" -c Release -o artifacts -p:NuspecFile="%~dp0SolutionComponentComparer.nuspec" -p:NuspecBasePath="%~dp0."
if errorlevel 1 (
  echo PACK FAILED.
  exit /b 1
)

echo.
echo Package created in %~dp0artifacts
dir /b artifacts\*.nupkg
