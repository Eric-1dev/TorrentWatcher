#!/bin/sh
# Запускает приложение под виртуальным дисплеем Xvfb: Cloudflare не пропускает
# headless-Chromium, поэтому Playwright работает с headed-браузером.
#
# xvfb-run в контейнере как PID 1 зависает на ожидании SIGUSR1 от Xvfb,
# поэтому X-сервер поднимаем напрямую.
set -eu

export DISPLAY=:99

# При перезапуске контейнера в /tmp остаются lock и сокет старого Xvfb —
# новый сервер из-за них не стартует.
rm -f /tmp/.X99-lock /tmp/.X11-unix/X99

Xvfb :99 -screen 0 1366x900x24 -nolisten tcp &
XVFB_PID=$!

# Ждём готовности X-сервера, чтобы первый запуск Chromium не упал.
i=0
while [ ! -S /tmp/.X11-unix/X99 ] && [ "$i" -lt 100 ]; do
    sleep 0.1
    i=$((i + 1))
done

if ! kill -0 "$XVFB_PID" 2>/dev/null; then
    echo "Xvfb не запустился: проверка Cloudflare будет недоступна." >&2
else
    echo "Xvfb запущен на :99 (pid $XVFB_PID)."
fi

exec dotnet TorrentWatcher.Web.dll
