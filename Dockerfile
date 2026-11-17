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

# Runtime: non-root, /data для SQLite и .torrent
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime

RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /data \
    && chown app:app /data

WORKDIR /app
COPY --from=build --chown=app:app /app/publish ./

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    ConnectionStrings__Default="Data Source=/data/torrentwatcher.db" \
    Storage__TorrentsDirectory="/data/torrents"

EXPOSE 8080
VOLUME ["/data"]
USER app

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl --fail --silent --show-error http://127.0.0.1:8080/api/health || exit 1

ENTRYPOINT ["dotnet", "TorrentWatcher.Web.dll"]
