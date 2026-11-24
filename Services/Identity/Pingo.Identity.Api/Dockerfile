# Build from the repository root, because the projects under Shared/ are needed too:
#   docker build -f Services/Identity/Pingo.Identity.Api/Dockerfile -t pingo-identity .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Project files first, so packages are restored again only when a project file changes.
COPY Shared/Pingo.Contracts/Pingo.Contracts.csproj Shared/Pingo.Contracts/
COPY Shared/Pingo.ServiceDefaults/Pingo.ServiceDefaults.csproj Shared/Pingo.ServiceDefaults/
COPY Services/Identity/Pingo.Identity/Pingo.Identity.csproj Services/Identity/Pingo.Identity/
COPY Services/Identity/Pingo.Identity.Api/Pingo.Identity.Api.csproj Services/Identity/Pingo.Identity.Api/
RUN dotnet restore Services/Identity/Pingo.Identity.Api/Pingo.Identity.Api.csproj

COPY Shared/ Shared/
COPY Services/Identity/ Services/Identity/
RUN dotnet publish Services/Identity/Pingo.Identity.Api/Pingo.Identity.Api.csproj -c Release -o /app --no-restore

# The final image has only the ASP.NET Core runtime and the published app, and doesn't run as root.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Pingo.Identity.Api.dll"]
