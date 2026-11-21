@echo off
chcp 65001 >nul
REM Загружаем сохранённые настройки с отключённым delayed expansion, чтобы "!" в паролях выжил.
setlocal DisableDelayedExpansion

REM ============================================================
REM  Публикация TorrentWatcher на сервере в Docker.
REM  Требуется: Docker Desktop + PuTTY (plink.exe, pscp.exe).
REM
REM  Скрипт собирает образ, сохраняет его в tar, копирует на сервер
REM  вместе с шаблоном docker-compose.yaml и remote-deploy.sh, затем через
REM  plink загружает образ и пересоздаёт контейнер через docker compose.
REM
REM  Настройки подключения хранятся в scripts\deploy\server\deploy.local.cfg
REM  (в git не попадает).
REM
REM  Вход возможен по root или по другой УЗ. Для не-root нужен sudo:
REM  либо NOPASSWD в sudoers, либо пароль sudo (запрашивается и передаётся
REM  на сервер через stdin, в файл и журнал не пишется).
REM
REM  Порт приложения по умолчанию 8088: 8080 на сервере обычно занят
REM  qBittorrent. Внутри контейнера Kestrel по-прежнему слушает 8080.
REM ============================================================

set "SAVED_FILE=%~dp0scripts\deploy\server\deploy.local.cfg"

set "HAVE_SAVED=0"
if exist "%SAVED_FILE%" (
  for /f "usebackq tokens=1,* delims==" %%a in ("%SAVED_FILE%") do set "%%a=%%b"
  set "HAVE_SAVED=1"
)

setlocal EnableDelayedExpansion

if "%HAVE_SAVED%"=="1" (
  set "AUTH_KIND=unknown"
  if "!SSH_AUTH:~0,2!"=="-i" set "AUTH_KIND=приватный ключ"
  if "!SSH_AUTH:~0,3!"=="-pw" set "AUTH_KIND=пароль"
  echo.
  echo Найдены сохранённые настройки в scripts\deploy\server\deploy.local.cfg:
  echo   Пользователь : !SSH_USER!
  echo   Хост         : !SSH_HOST!
  echo   Авторизация  : !AUTH_KIND!
  echo   Порт         : !APP_PORT!
  echo   Каталог      : !INSTALL_DIR!
  echo.
  set /p "REUSE=Использовать сохранённые настройки? [Y/n]: "
  if /i "!REUSE!"=="n" (
    set "SSH_USER="
    set "SSH_HOST="
    set "SSH_AUTH="
  )
)

if "!SSH_USER!"=="" set /p "SSH_USER=Пользователь SSH [root]: "
if "!SSH_USER!"=="" set "SSH_USER=root"

if "!SSH_HOST!"=="" set /p "SSH_HOST=IP или хост SSH-сервера: "
if "!SSH_HOST!"=="" (
  echo [ОШИБКА] SSH_HOST не задан.
  exit /b 1
)

if "!SSH_AUTH!"=="" (
  set /p "SSH_KEY=Путь к приватному ключу PuTTY .ppk или пусто для пароля: "
  if not "!SSH_KEY!"=="" (
    set SSH_AUTH=-i "!SSH_KEY!"
  ) else (
    set /p "SSH_PW=SSH-пароль: "
    if "!SSH_PW!"=="" (
      echo [ОШИБКА] SSH-данные не заданы.
      exit /b 1
    )
    set SSH_AUTH=-pw "!SSH_PW!"
  )
)

set "SSH_TARGET=%SSH_USER%@%SSH_HOST%"

REM Пароль sudo нужен, только если входим не под root и на сервере нет NOPASSWD.
REM SUDO_PW можно задать заранее в окружении — тогда запрос не появится.
set "SUDO_MODE=nopass"
if /i not "!SSH_USER!"=="root" if not defined SUDO_PW (
  set /p "SUDO_PW=Пароль sudo для !SSH_USER! (Enter — если настроен NOPASSWD): "
  if not defined SUDO_PW echo [ВНИМАНИЕ] Пароль sudo не введён: проверяю NOPASSWD.
)
if defined SUDO_PW (
  set "SUDO_MODE=file"
  set "PW_FILE=%TEMP%\torrentwatcher-sudo.txt"
  powershell -NoProfile -Command "[IO.File]::WriteAllText($env:PW_FILE, $env:SUDO_PW, (New-Object System.Text.UTF8Encoding($false)))"
  if errorlevel 1 (
    echo [ОШИБКА] Не удалось подготовить пароль sudo.
    exit /b 1
  )
)

if "%IMAGE_NAME%"=="" set "IMAGE_NAME=torrentwatcher"
if "%APP_PORT%"=="" set "APP_PORT=8088"
if "%INSTALL_DIR%"=="" set "INSTALL_DIR=/torrentwatcher"
if "%REMOTE_DIR%"=="" set "REMOTE_DIR=/tmp"
if "%PLINK%"=="" set "PLINK=plink.exe"
if "%PSCP%"=="" set "PSCP=pscp.exe"

call :save_settings

setlocal DisableDelayedExpansion

set "ROOT=%~dp0"
set "ROOT=%ROOT:~0,-1%"

REM Тег версии (timestamp).
for /f %%i in ('powershell -NoProfile -Command "Get-Date -Format yyyyMMdd-HHmmss"') do set "BUILD_ID=%%i"

echo.
echo ================================================
echo   Сборка и деплой %IMAGE_NAME%
echo   Версия  : %BUILD_ID%
echo   Сервер  : %SSH_TARGET%
echo   Каталог : %INSTALL_DIR%
echo   Порт    : %APP_PORT%
echo   Sudo    : %SUDO_MODE%
echo ================================================
echo.

where docker >nul 2>nul
if errorlevel 1 (
  echo [ОШИБКА] docker не найден в PATH.
  exit /b 1
)
where %PLINK% >nul 2>nul
if errorlevel 1 (
  echo [ОШИБКА] %PLINK% не найден в PATH. Установите PuTTY и добавьте его в PATH.
  exit /b 1
)
where %PSCP% >nul 2>nul
if errorlevel 1 (
  echo [ОШИБКА] %PSCP% не найден в PATH. Установите PuTTY и добавьте его в PATH.
  exit /b 1
)

pushd "%ROOT%"

set "COMPOSE_CFG=scripts\deploy\server\docker-compose.yaml"
if not exist "%COMPOSE_CFG%" (
  echo [ОШИБКА] Не найден шаблон %COMPOSE_CFG%.
  exit /b 1
)
if not exist "scripts\deploy\server\remote-deploy.sh" (
  echo [ОШИБКА] Не найден scripts\deploy\server\remote-deploy.sh.
  exit /b 1
)
echo Шаблон compose: %COMPOSE_CFG%
echo.

REM На сервер файлы должны уходить с LF: CRLF ломает bash
REM ("set: pipefail: invalid option name"). Делаем копии с LF в отдельном
REM каталоге и отправляем их, чтобы деплой не зависел от core.autocrlf.
set "STAGE=%TEMP%\torrentwatcher-deploy"
if exist "%STAGE%" rd /s /q "%STAGE%"
mkdir "%STAGE%"
powershell -NoProfile -Command "$ErrorActionPreference='Stop'; $stage=$env:STAGE; foreach($f in @('scripts\deploy\server\remote-deploy.sh','scripts\deploy\server\docker-compose.yaml')) { $leaf=[IO.Path]::GetFileName($f); $t=[IO.File]::ReadAllText((Join-Path $env:ROOT $f)); $t=$t.Replace([string][char]13,''); [IO.File]::WriteAllText((Join-Path $stage $leaf), $t, (New-Object System.Text.UTF8Encoding($false))) }"
if errorlevel 1 goto :error

echo [1/6] Сборка Docker-образа...
docker build -t %IMAGE_NAME%:latest -t %IMAGE_NAME%:%BUILD_ID% -f Dockerfile .
if errorlevel 1 goto :error

echo [2/6] Экспорт образа в tar...
docker save -o %IMAGE_NAME%.tar %IMAGE_NAME%:latest %IMAGE_NAME%:%BUILD_ID%
if errorlevel 1 goto :error

REM 1,7 ГБ tar по SSH на 20 Мбит/с идёт ~12 минут. PuTTY умеет сжимать поток
REM на лету (zlib): Chromium и .NET ужимаются примерно в 2,5 раза. Отключается
REM переменной PSCP_COMPRESS=0.
if "%PSCP_COMPRESS%"=="" set "PSCP_COMPRESS=1"
set "PSCP_COMPRESS_FLAG="
if "%PSCP_COMPRESS%"=="1" set "PSCP_COMPRESS_FLAG=-C"

echo [3/6] Копирование на сервер (%SSH_TARGET%)...
%PSCP% %PSCP_COMPRESS_FLAG% %SSH_AUTH% %IMAGE_NAME%.tar %SSH_TARGET%:%REMOTE_DIR%/
if errorlevel 1 goto :error
%PSCP% %SSH_AUTH% "%STAGE%\docker-compose.yaml" %SSH_TARGET%:%REMOTE_DIR%/docker-compose.yaml
if errorlevel 1 goto :error
%PSCP% %SSH_AUTH% "%STAGE%\remote-deploy.sh" %SSH_TARGET%:%REMOTE_DIR%/remote-deploy.sh
if errorlevel 1 goto :error
if defined PW_FILE (
  %PSCP% %SSH_AUTH% "%PW_FILE%" %SSH_TARGET%:%REMOTE_DIR%/torrentwatcher-sudo.txt
  if errorlevel 1 goto :error
)

echo [4/6] Загрузка образа и перезапуск контейнера на сервере...
REM Пароль sudo, если он нужен, лежит отдельным файлом: stdin занят баннером
REM SSH-сервера и не годится для передачи секрета.
%PLINK% %SSH_AUTH% %SSH_TARGET% "bash %REMOTE_DIR%/remote-deploy.sh %APP_PORT% %BUILD_ID% %INSTALL_DIR% %SUDO_MODE% %REMOTE_DIR%/torrentwatcher-sudo.txt"
set "DEPLOY_RC=%ERRORLEVEL%"
if defined PW_FILE del /q "%PW_FILE%" >nul 2>&1
REM Код 2 — деплой удался, но remote-deploy.sh выдал предупреждения
REM (например, healthcheck не успел стать healthy).
if "%DEPLOY_RC%"=="2" echo [ВНИМАНИЕ] Контейнер обновлён, но есть предупреждения (см. WARN выше).
if not "%DEPLOY_RC%"=="0" if not "%DEPLOY_RC%"=="2" goto :error

echo [5/6] Удаление временного tar...
if exist %IMAGE_NAME%.tar del /q %IMAGE_NAME%.tar

echo [6/6] Очистка старых локальных образов %IMAGE_NAME%...
for /f "delims=" %%t in ('docker images %IMAGE_NAME% --format "{{.Tag}}"') do (
  if not "%%t"=="latest" if not "%%t"=="%BUILD_ID%" docker rmi %IMAGE_NAME%:%%t >nul 2>&1
)

popd

if exist "%STAGE%" rd /s /q "%STAGE%"

echo.
echo ================================================
echo   Готово! Образ %IMAGE_NAME%:%BUILD_ID% развёрнут.
echo   UI/API: http://%SSH_HOST%:%APP_PORT%
echo   Данные: %INSTALL_DIR%/data
echo ================================================
exit /b 0

:save_settings
powershell -NoProfile -Command "$l=@(('SSH_USER='+$env:SSH_USER),('SSH_HOST='+$env:SSH_HOST),('SSH_AUTH='+$env:SSH_AUTH),('APP_PORT='+$env:APP_PORT),('INSTALL_DIR='+$env:INSTALL_DIR),('REMOTE_DIR='+$env:REMOTE_DIR)); Set-Content -LiteralPath $env:SAVED_FILE -Value $l -Encoding ASCII"
goto :eof

:error
echo.
echo ================================================
echo   ОШИБКА на предыдущем шаге. Смотрите вывод выше.
echo ================================================
popd
if exist %IMAGE_NAME%.tar del /q %IMAGE_NAME%.tar
if exist "%STAGE%" rd /s /q "%STAGE%"
if defined PW_FILE del /q "%PW_FILE%" >nul 2>&1
exit /b 1
