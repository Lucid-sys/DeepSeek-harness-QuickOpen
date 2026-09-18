@echo off
rem ---------------------------------------------------------------------------
rem  Rebuild the DeepSeek Harness one-click launcher.
rem
rem  ASCII only on purpose: cmd.exe decodes .cmd files with the console code
rem  page, so non-ASCII text written inline here could be misread. Chinese
rem  notes live in README.md.
rem
rem  The exe may currently be RUNNING (it is the launcher that keeps the web
rem  service alive). A running image cannot be overwritten, but Windows does
rem  allow RENAMING it, so we publish into a staging folder and swap by rename.
rem  The running process keeps its old image; the next double-click gets the
rem  new build.
rem
rem  Usage:  build.cmd                 framework-dependent single file (~300 KB)
rem          build.cmd /selfcontained self-contained single file (~70 MB)
rem ---------------------------------------------------------------------------
setlocal
set "HERE=%~dp0"
cd /d "%HERE%"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo [ERROR] dotnet SDK not found on PATH. Install .NET SDK 9 and retry.
  exit /b 1
)

rem Framework-dependent by default (~300 KB, needs the .NET 9 Desktop runtime).
rem /selfcontained embeds the runtime instead (~65 MB, runs anywhere).
rem For the self-contained case the WPF native libraries must ALSO be embedded,
rem otherwise publish emits StartDSH.exe plus five *cor3.dll files next to it
rem instead of one file; compression keeps that single file down to ~65 MB.
set "SELF=--self-contained false"
set "PORTABLE="
if /i "%~1"=="/selfcontained" (
  set "SELF=--self-contained true"
  set "PORTABLE=-p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true"
)

set "STAGE=%HERE%dist\.staging"
if exist "%STAGE%" rd /s /q "%STAGE%"

echo [build] publishing launcher (this takes a few seconds)...
dotnet publish "%HERE%DshLauncher.csproj" -c Release -r win-x64 -p:PublishSingleFile=true %SELF% %PORTABLE% -o "%STAGE%"
if errorlevel 1 (
  echo.
  echo [ERROR] publish failed. If NuGet complains about a missing fallback
  echo         package folder, keep NuGet.Config next to DshLauncher.csproj.
  exit /b 1
)

if not exist "%HERE%dist" mkdir "%HERE%dist"

rem Swap the new build in. The live exe may be the running image, which cannot
rem be overwritten but can be renamed; build-swap.ps1 handles both cases.
powershell -NoProfile -ExecutionPolicy Bypass -File "%HERE%build-swap.ps1" -Dist "%HERE%dist" -New "%STAGE%\StartDSH.exe"

rd /s /q "%STAGE%" >nul 2>nul

echo.
echo [build] done. Files in dist:
dir /b "%HERE%dist"
echo.
echo The desktop shortcut points at dist\StartDSH.exe.
endlocal
