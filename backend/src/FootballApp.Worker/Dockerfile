# Build context is the backend/ directory:
#   docker build -f src/FootballApp.Worker/Dockerfile -t football-app-worker .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Project files first, so the NuGet restore layer is reused until a dependency changes.
COPY src/FootballApp.Domain/FootballApp.Domain.csproj src/FootballApp.Domain/
COPY src/FootballApp.Application/FootballApp.Application.csproj src/FootballApp.Application/
COPY src/FootballApp.Infrastructure/FootballApp.Infrastructure.csproj src/FootballApp.Infrastructure/
COPY src/FootballApp.Worker/FootballApp.Worker.csproj src/FootballApp.Worker/
RUN dotnet restore src/FootballApp.Worker/FootballApp.Worker.csproj

COPY src/ src/
RUN dotnet publish src/FootballApp.Worker/FootballApp.Worker.csproj -c Release -o /app --no-restore

# The aspnet image rather than the plain runtime: Infrastructure references the ASP.NET Core shared
# framework (it is shared with the API).
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

USER $APP_UID
ENTRYPOINT ["dotnet", "FootballApp.Worker.dll"]
