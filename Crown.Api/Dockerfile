FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Crown.Api/Crown.Api.csproj", "Crown.Api/"]
RUN dotnet restore "Crown.Api/Crown.Api.csproj"

COPY ["Crown.Api/", "Crown.Api/"]
WORKDIR /src/Crown.Api
RUN dotnet publish "Crown.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# docker CLI + compose для обновления стека из UI (docker.sock).
# Бинари кладёт scripts/prepare-docker-cli.sh с хоста — без curl на GitHub во время build.
RUN set -eux; \
    ok=0; \
    for i in 1 2 3 4 5; do \
      if apt-get update \
        && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
            curl ca-certificates postgresql-client gzip bash git; then \
        ok=1; \
        break; \
      fi; \
      echo "apt attempt $i failed, retrying..."; \
      sleep 10; \
    done; \
    test "$ok" -eq 1; \
    command -v curl; \
    command -v psql; \
    command -v gunzip; \
    command -v git; \
    rm -rf /var/lib/apt/lists/*

RUN mkdir -p /usr/local/lib/docker/cli-plugins
COPY Crown.Api/.docker-cli/docker /usr/local/bin/docker
COPY Crown.Api/.docker-cli/docker-compose /usr/local/lib/docker/cli-plugins/docker-compose
RUN set -eux; \
    chmod +x /usr/local/bin/docker /usr/local/lib/docker/cli-plugins/docker-compose; \
    docker --version; \
    docker compose version

ENV ASPNETCORE_URLS=http://+:8080

COPY --from=build /app/publish .

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Crown.Api.dll"]
