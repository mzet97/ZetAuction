# syntax=docker/dockerfile:1.7
# ---------- Build stage ----------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore: copy only project files first for cacheable layers
COPY ZetAuction.slnx ./
COPY src/ZetAuction.Shared/ZetAuction.Shared.csproj          src/ZetAuction.Shared/
COPY src/ZetAuction.Domain/ZetAuction.Domain.csproj          src/ZetAuction.Domain/
COPY src/ZetAuction.Application/ZetAuction.Application.csproj src/ZetAuction.Application/
COPY src/ZetAuction.Infrastructure/ZetAuction.Infrastructure.csproj src/ZetAuction.Infrastructure/
COPY src/ZetAuction.Api/ZetAuction.Api.csproj                src/ZetAuction.Api/
RUN dotnet restore src/ZetAuction.Api/ZetAuction.Api.csproj

# Copy sources and publish (portable RID; runtime image picks the right libs)
COPY src/ src/
RUN dotnet publish src/ZetAuction.Api/ZetAuction.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ---------- Runtime stage ----------
FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS runtime
WORKDIR /app

# curl is used by HEALTHCHECK; tini gives us a proper PID 1 reaper.
RUN apk add --no-cache curl tini \
    && addgroup -S -g 1001 zetauction \
    && adduser -S -D -u 1001 -G zetauction zetauction \
    && mkdir -p /app/logs \
    && chown -R zetauction:zetauction /app

COPY --from=build --chown=zetauction:zetauction /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_USE_POLLING_FILE_WATCHER=false \
    DOTNET_GCDynamicAdaptationMode=1

# Connection strings, JWT secrets and other configuration are intentionally
# NOT baked into the image. They are injected at runtime via docker-compose,
# Kubernetes secrets or the platform's config provider.

USER zetauction

HEALTHCHECK --interval=30s --timeout=10s --start-period=40s --retries=3 \
    CMD curl -fsS http://localhost:8080/health || exit 1

EXPOSE 8080

ENTRYPOINT ["/sbin/tini", "--", "dotnet", "ZetAuction.Api.dll"]
