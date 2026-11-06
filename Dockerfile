
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

COPY . .

RUN dotnet restore InventorySystem.sln

ARG PROJECT
RUN test -n "$PROJECT" 

RUN dotnet publish "$PROJECT" \
        --configuration Release \
        --output /app \
        --no-restore \
    && basename "$PROJECT" .csproj > /app/.entry-assembly

# -- runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY --from=build /app .

USER $APP_UID

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080

HEALTHCHECK --interval=10s --timeout=5s --start-period=30s --retries=10 \
    CMD curl -fsS http://localhost:8080/health -o /dev/null || exit 1


ENTRYPOINT ["/bin/sh", "-c", "exec dotnet /app/$(cat /app/.entry-assembly).dll"]
