# syntax=docker/dockerfile:1
#
# Multi-stage build for the QueryForge API and its EF Core migration bundle.
# Build context: repository root (the API references Domain, Application, and Infrastructure).
#
#   docker build -f src/QueryForge.Api/Dockerfile .                    -> API image (default, last stage)
#   docker build -f src/QueryForge.Api/Dockerfile --target migrator .  -> one-shot migration image

ARG DOTNET_VERSION=8.0

# ---------------------------------------------------------------------------
# Restore and compile
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

# Restore first using only project files so the restore layer is cached until dependencies change.
COPY global.json Directory.Build.props ./
COPY src/QueryForge.Domain/QueryForge.Domain.csproj src/QueryForge.Domain/
COPY src/QueryForge.Application/QueryForge.Application.csproj src/QueryForge.Application/
COPY src/QueryForge.Infrastructure/QueryForge.Infrastructure.csproj src/QueryForge.Infrastructure/
COPY src/QueryForge.Api/QueryForge.Api.csproj src/QueryForge.Api/
RUN dotnet restore src/QueryForge.Api/QueryForge.Api.csproj

# Installed before the source is copied so this layer stays cached across code changes.
# dotnet-ef matches the EF Core 8.0.11 packages; it is only used by the migrations-build stage.
ARG DOTNET_EF_VERSION=8.0.11
RUN dotnet tool install --global dotnet-ef --version ${DOTNET_EF_VERSION}
ENV PATH="${PATH}:/root/.dotnet/tools"

COPY src/ src/

# ---------------------------------------------------------------------------
# Publish the API
# ---------------------------------------------------------------------------
FROM build AS publish
RUN dotnet publish src/QueryForge.Api/QueryForge.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

# ---------------------------------------------------------------------------
# Build the EF Core migration bundle
# ---------------------------------------------------------------------------
FROM build AS migrations-build
# Creating the bundle starts the API host far enough to build the DbContext, which requires a connection string.
# This placeholder (no credentials) exists only for this RUN step; the bundle never connects at build time and
# reads the real connection string from ConnectionStrings__SqlServer when it runs.
RUN ConnectionStrings__SqlServer="Server=build-placeholder;Database=QueryForge" \
    dotnet ef migrations bundle \
    --project src/QueryForge.Infrastructure \
    --startup-project src/QueryForge.Api \
    --configuration Release \
    --output /app/efbundle

# ---------------------------------------------------------------------------
# Migrator: applies pending migrations and exits.
# The connection string is supplied at runtime via ConnectionStrings__SqlServer, never baked in.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS migrator
WORKDIR /app
COPY --from=migrations-build /app/efbundle ./efbundle
USER $APP_UID
ENTRYPOINT ["./efbundle"]

# ---------------------------------------------------------------------------
# API runtime (default target)
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS api

# curl is used only by the container health check (GET /health/live).
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=publish /app/publish ./

# Built-in non-root user of the .NET 8 images; ASP.NET Core listens on 8080 by default.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "QueryForge.Api.dll"]
