# Build from the repository root: docker build -f src/AiAutomations.Client.Web/Dockerfile .
# (needs the whole repo in context since this project references Shared, which references Core)

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/AiAutomations.Client.Web/AiAutomations.Client.Web.csproj", "src/AiAutomations.Client.Web/"]
COPY ["src/AiAutomations.Shared/AiAutomations.Shared.csproj", "src/AiAutomations.Shared/"]
COPY ["src/AiAutomations.Core/AiAutomations.Core.csproj", "src/AiAutomations.Core/"]
RUN dotnet restore "src/AiAutomations.Client.Web/AiAutomations.Client.Web.csproj"

COPY src/AiAutomations.Client.Web/ src/AiAutomations.Client.Web/
COPY src/AiAutomations.Shared/ src/AiAutomations.Shared/
COPY src/AiAutomations.Core/ src/AiAutomations.Core/

RUN dotnet publish "src/AiAutomations.Client.Web/AiAutomations.Client.Web.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "AiAutomations.Client.Web.dll"]
