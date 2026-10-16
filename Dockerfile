# ---------- Build stage ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first for layer caching
COPY src/MatTrakr/MatTrakr.csproj src/MatTrakr/
RUN dotnet restore src/MatTrakr/MatTrakr.csproj

COPY src/ src/
RUN dotnet publish src/MatTrakr/MatTrakr.csproj -c Release -o /app/publish /p:UseAppHost=false

# ---------- Runtime stage ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# curl is required for the container HEALTHCHECK (the aspnet image ships without it).
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app

ARG APP_VERSION=local
ENV ASPNETCORE_URLS=http://+:9242 \
    Storage__DataPath=/app/data \
    MATTRAKR_VERSION=$APP_VERSION

COPY --from=build /app/publish .

EXPOSE 9242
VOLUME ["/app/data"]

# The platform health (MatDesign docs/PLATFORM.md): 200 for ok and degraded, 503 for down (a degraded app is not restarted).
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD curl -fsS http://127.0.0.1:9242/api/health || exit 1

ENTRYPOINT ["dotnet", "MatTrakr.dll"]
