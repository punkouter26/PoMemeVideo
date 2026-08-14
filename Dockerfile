# NOT the deployment path. `deploy-pomemevideo.yml` publishes framework-dependent, bundles a
# static ffmpeg into the output and ZIP-deploys it, because requiring a custom container is what
# held the App Service plan at B1. This image is kept for local `docker run` and as the way back
# to a container deploy if the free-tier constraints (60 CPU-min/day, no Always On) prove too
# tight — it is not built by CI, so treat it as able to drift.

# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files first for layer caching
COPY PoMemeVideo.slnx global.json Directory.Build.props Directory.Packages.props ./
COPY src/PoMemeVideo.Shared/PoMemeVideo.Shared.csproj src/PoMemeVideo.Shared/
COPY src/PoMemeVideo.Api/PoMemeVideo.Api.csproj src/PoMemeVideo.Api/
COPY src/PoMemeVideo.Client/PoMemeVideo.Client.csproj src/PoMemeVideo.Client/

RUN dotnet restore src/PoMemeVideo.Api/PoMemeVideo.Api.csproj

# Copy full source and publish
COPY . .
RUN dotnet publish src/PoMemeVideo.Api/PoMemeVideo.Api.csproj \
    -c Release \
    -o /app/publish

# Stage 2: Runtime — install FFmpeg
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install FFmpeg (required for audio replacement and video filter pipeline)
RUN apt-get update && \
    apt-get install -y --no-install-recommends ffmpeg curl && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

# Copy published output
COPY --from=build /app/publish .

# Health check — platform feature pointed at /health (FR-031 / SC-009)
HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "PoMemeVideo.Api.dll"]
