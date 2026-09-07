# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY ["RoadmapTracker.csproj", "./"]
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Minimal Alpine Runtime (~55 MB total)
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app

# Ensure SQLite native libs and data directory permissions
RUN apk add --no-cache icu-libs sqlite-libs \
    && mkdir -p /app/data \
    && chown -R $APP_UID:$APP_UID /app/data

ENV DATA_DIR=/app/data
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

COPY --from=build /app/publish .
USER $APP_UID

EXPOSE 8081
ENV ASPNETCORE_URLS=http://+:8081

ENTRYPOINT ["dotnet", "RoadmapTracker.dll"]
