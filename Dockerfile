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
WORKDIR /app

ARG APP_VERSION=local
ENV ASPNETCORE_URLS=http://+:9242 \
    Storage__DataPath=/app/data \
    MATTRAKR_VERSION=$APP_VERSION

COPY --from=build /app/publish .

EXPOSE 9242
VOLUME ["/app/data"]

ENTRYPOINT ["dotnet", "MatTrakr.dll"]
