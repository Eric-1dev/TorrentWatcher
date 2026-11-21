# Сборка фронтенда: npm ci + vite build → /wwwroot
FROM node:24-alpine AS client
WORKDIR /src
COPY src/TorrentWatcher.Web/ClientApp/package.json src/TorrentWatcher.Web/ClientApp/package-lock.json ./
RUN npm ci
COPY src/TorrentWatcher.Web/ClientApp/ ./
RUN npm run build

# Сборка backend: restore по csproj, затем publish с готовым wwwroot
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY TorrentWatcher.slnx Directory.Build.props global.json ./
COPY src/TorrentWatcher.Domain/TorrentWatcher.Domain.csproj src/TorrentWatcher.Domain/
COPY src/TorrentWatcher.Application/TorrentWatcher.Application.csproj src/TorrentWatcher.Application/
COPY src/TorrentWatcher.Infrastructure/TorrentWatcher.Infrastructure.csproj src/TorrentWatcher.Infrastructure/
COPY src/TorrentWatcher.Web/TorrentWatcher.Web.csproj src/TorrentWatcher.Web/
RUN dotnet restore src/TorrentWatcher.Web/TorrentWatcher.Web.csproj
COPY src/ src/
COPY --from=client /wwwroot src/TorrentWatcher.Web/wwwroot/
RUN dotnet publish src/TorrentWatcher.Web/TorrentWatcher.Web.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore

# Runtime: non-root, /data для SQLite, .torrent и профиля браузера
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime

# xvfb нужен, потому что Cloudflare не пропускает headless-Chromium.
# libatomic1 нужен node из Playwright: без него `playwright install --with-deps` не стартует.
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl xvfb libatomic1 \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /data \
    && chown app:app /data

WORKDIR /app

# Браузеры Playwright ставим в общий каталог, чтобы они были доступны пользователю app.
# Копируем только саму сборку Playwright: тогда изменение кода не сбрасывает этот слой
# и ~325 МБ браузеров не скачиваются заново на каждой сборке.
# Рядом с Microsoft.Playwright.dll нет runtimeconfig.json (это библиотека), поэтому
# создаём его на время установки — иначе `dotnet ...dll` не запускается.
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
COPY --from=build /app/publish/Microsoft.Playwright.dll /app/Microsoft.Playwright.dll
COPY --from=build /app/publish/.playwright /app/.playwright
RUN printf '%s' '{"runtimeOptions":{"tfm":"net10.0","framework":{"name":"Microsoft.NETCore.App","version":"10.0.0"}}}' \
      > /app/Microsoft.Playwright.runtimeconfig.json \
    && dotnet /app/Microsoft.Playwright.dll install --with-deps chromium \
    && rm -f /app/Microsoft.Playwright.runtimeconfig.json

COPY --from=build --chown=app:app /app/publish ./

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    ConnectionStrings__Default="Data Source=/data/torrentwatcher.db" \
    Storage__TorrentsDirectory="/data/torrents" \
    Security__KeysDirectory="/data/keys" \
    Rutracker__Browser__ProfileDirectory="/data/browser" \
    Rutracker__Browser__Headless=false \
    Rutracker__Browser__DisableSandbox=true

EXPOSE 8080
VOLUME ["/data"]
USER app

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl --fail --silent --show-error http://127.0.0.1:8080/api/health || exit 1

# Браузер запускается с окном внутри виртуального дисплея Xvfb.
COPY --chown=app:app docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh
RUN chmod +x /usr/local/bin/docker-entrypoint.sh
ENTRYPOINT ["docker-entrypoint.sh"]
