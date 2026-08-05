# SportsDeepLinks.Scraper container image.
#
# Runtime stage is Microsoft's official Playwright .NET image, which ships with Chromium (and
# its OS-level dependencies) already installed for the exact Playwright version this project
# references - avoids running `playwright install` (a network download) at container build or
# run time. The tag's version MUST match the Microsoft.Playwright package version in
# SportsDeepLinks.Scraper.csproj; bump both together.
ARG PLAYWRIGHT_VERSION=v1.61.0-noble

# ---- Build stage ----
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore first, from just the project files, so dependency layers cache across code-only changes.
COPY SportsDeepLinks.sln .
COPY SportsDeepLinks.Core/SportsDeepLinks.Core.csproj SportsDeepLinks.Core/
COPY SportsDeepLinks.Scraper/SportsDeepLinks.Scraper.csproj SportsDeepLinks.Scraper/
COPY SportsDeepLinks.Tests/SportsDeepLinks.Tests.csproj SportsDeepLinks.Tests/
RUN dotnet restore SportsDeepLinks.Scraper/SportsDeepLinks.Scraper.csproj

COPY . .
RUN dotnet publish SportsDeepLinks.Scraper/SportsDeepLinks.Scraper.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# ---- Runtime stage ----
FROM mcr.microsoft.com/playwright/dotnet:${PLAYWRIGHT_VERSION} AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# Auth token cache (data/apple_uts_auth.json) and scraped output (out/events.json) both live
# under /app relative to AppContext.BaseDirectory - mount volumes here to persist across runs.
RUN mkdir -p /app/data /app/out
VOLUME ["/app/data", "/app/out"]

ENTRYPOINT ["dotnet", "SportsDeepLinks.Scraper.dll"]
