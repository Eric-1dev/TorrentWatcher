@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"

set "APP_URL=http://localhost:8088"
set "APP_PORT=8088"
set "BUILD_ARG=--build"

if /I "%~1"=="--no-build" (
    set "BUILD_ARG="
    shift
)

echo ============================================================
echo  TorrentWatcher: build and run in Docker
echo ============================================================
echo.

where docker >nul 2>nul
if errorlevel 1 (
    echo [ERROR] Docker was not found in PATH.
    echo Install Docker Desktop: https://www.docker.com/products/docker-desktop/
    exit /b 1
)

docker info >nul 2>nul
if not errorlevel 1 goto daemon_ready

echo Docker daemon is not running, starting Docker Desktop...
set "DOCKER_EXE=%ProgramFiles%\Docker\Docker\Docker Desktop.exe"
if not exist "%DOCKER_EXE%" (
    echo [ERROR] "%DOCKER_EXE%" was not found.
    echo Start Docker Desktop manually and try again.
    exit /b 1
)
start "" "%DOCKER_EXE%"

set /a WAITED=0
:wait_daemon
timeout /t 5 /nobreak >nul
docker info >nul 2>nul
if not errorlevel 1 goto daemon_ready
set /a WAITED+=5
if !WAITED! LSS 180 goto wait_daemon
echo [ERROR] Docker did not start within 3 minutes.
exit /b 1

:daemon_ready
echo Docker is ready. Running docker compose...
if defined BUILD_ARG (
    echo The first build can take 5-15 minutes: npm, .NET and Chromium.
) else (
    echo Build skipped ^(--no-build^), using the existing image.
)
echo.

docker compose up -d !BUILD_ARG!
if errorlevel 1 (
    echo.
    echo [ERROR] docker compose failed. See: docker compose logs
    exit /b 1
)

echo Waiting for the container healthcheck...
set "CID="
for /f "usebackq delims=" %%i in (`docker compose ps -q torrentwatcher 2^>nul`) do set "CID=%%i"
if not defined CID (
    echo [ERROR] Container not found. See: docker compose logs
    exit /b 1
)

set /a TRIES=0
:wait_health
set "HEALTH="
for /f "usebackq delims=" %%s in (`docker inspect --format "{{if .State.Health}}{{.State.Health.Status}}{{else}}starting{{end}}" !CID! 2^>nul`) do set "HEALTH=%%s"

if /I "!HEALTH!"=="healthy" goto healthy
if /I "!HEALTH!"=="unhealthy" (
    echo.
    echo [ERROR] Container is unhealthy. Logs:
    docker compose logs --tail 50 torrentwatcher
    exit /b 1
)

set /a TRIES+=1
if !TRIES! GEQ 40 (
    echo.
    echo [WARN] Healthcheck did not become healthy within 2 minutes.
    echo The app may still be applying migrations. Logs: docker compose logs -f torrentwatcher
    goto ready
)
timeout /t 3 /nobreak >nul
goto wait_health

:healthy
echo Container is healthy.

:ready
echo.
echo ============================================================
echo  Ready.
echo  UI:  %APP_URL%
echo  API: %APP_URL%/api
echo  Docs: %APP_URL%/api/docs
echo.
echo  Data: .\data  ^(SQLite, .torrent, Chromium profile^)
echo  Logs: docker compose logs -f torrentwatcher
echo  Stop: docker compose down
echo ============================================================

start "" "%APP_URL%"
endlocal
exit /b 0
