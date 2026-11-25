# Build from the repository root, because the projects under Shared/ are needed too:
#   docker build -f Services/Message/Pingo.Messages.Api/Dockerfile -t pingo-messages .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Project files first, so packages are restored again only when a project file changes.
COPY Shared/Pingo.Contracts/Pingo.Contracts.csproj Shared/Pingo.Contracts/
COPY Shared/Pingo.ServiceDefaults/Pingo.ServiceDefaults.csproj Shared/Pingo.ServiceDefaults/
COPY Services/Message/Pingo.Messages/Pingo.Messages.csproj Services/Message/Pingo.Messages/
COPY Services/Message/Pingo.Messages.Api/Pingo.Messages.Api.csproj Services/Message/Pingo.Messages.Api/
RUN dotnet restore Services/Message/Pingo.Messages.Api/Pingo.Messages.Api.csproj

COPY Shared/ Shared/
COPY Services/Message/ Services/Message/
RUN dotnet publish Services/Message/Pingo.Messages.Api/Pingo.Messages.Api.csproj -c Release -o /app --no-restore

# The final image has only the ASP.NET Core runtime and the published app, and doesn't run as root.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Pingo.Messages.Api.dll"]
