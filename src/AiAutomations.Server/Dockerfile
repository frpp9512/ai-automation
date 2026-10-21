# Build from the repository root: docker build -f src/AiAutomations.Server/Dockerfile .
# (needs the whole repo in context since this project references Core/Infrastructure/Shared)

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/AiAutomations.Server/AiAutomations.Server.csproj", "src/AiAutomations.Server/"]
COPY ["src/AiAutomations.Core/AiAutomations.Core.csproj", "src/AiAutomations.Core/"]
COPY ["src/AiAutomations.Infrastructure/AiAutomations.Infrastructure.csproj", "src/AiAutomations.Infrastructure/"]
COPY ["src/AiAutomations.Shared/AiAutomations.Shared.csproj", "src/AiAutomations.Shared/"]
RUN dotnet restore "src/AiAutomations.Server/AiAutomations.Server.csproj"

COPY src/AiAutomations.Server/ src/AiAutomations.Server/
COPY src/AiAutomations.Core/ src/AiAutomations.Core/
COPY src/AiAutomations.Infrastructure/ src/AiAutomations.Infrastructure/
COPY src/AiAutomations.Shared/ src/AiAutomations.Shared/

RUN dotnet publish "src/AiAutomations.Server/AiAutomations.Server.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# curl for the compose healthcheck - the base image doesn't include it.
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# SQLite lives on a mounted volume so it survives container recreation.
RUN mkdir -p /app/data

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "AiAutomations.Server.dll"]
