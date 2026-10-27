FROM mcr.microsoft.com/dotnet/sdk:9.0-bookworm-slim AS build
WORKDIR /src

COPY server/DefenseGame.sln server/
COPY server/src/DefenseGame.Server/DefenseGame.Server.csproj server/src/DefenseGame.Server/
COPY server/tests/DefenseGame.Server.Tests/DefenseGame.Server.Tests.csproj server/tests/DefenseGame.Server.Tests/
RUN dotnet restore server/DefenseGame.sln

COPY server/ server/
RUN dotnet publish server/src/DefenseGame.Server/DefenseGame.Server.csproj \
    --configuration Release \
    --no-restore \
    --output /out

FROM mcr.microsoft.com/dotnet/aspnet:9.0-bookworm-slim AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/data \
    && chown -R app:app /app

WORKDIR /app
COPY --from=build --chown=app:app /out/ ./

ENV Urls=http://+:8080 \
    ProgressStore__FilePath=/app/data/progress.json
EXPOSE 8080
VOLUME ["/app/data"]
USER app

HEALTHCHECK --interval=10s --timeout=3s --start-period=10s --retries=3 \
    CMD curl --fail --silent http://127.0.0.1:8080/health || exit 1

ENTRYPOINT ["dotnet", "DefenseGame.Server.dll"]
