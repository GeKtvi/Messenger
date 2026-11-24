# Built from the repository root by lab/docker-compose.yml.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Shared/Pingo.ServiceDefaults/Pingo.ServiceDefaults.csproj Shared/Pingo.ServiceDefaults/
COPY lab/Pingo.Edge/Pingo.Edge.csproj lab/Pingo.Edge/
RUN dotnet restore lab/Pingo.Edge/Pingo.Edge.csproj

COPY Shared/ Shared/
COPY lab/Pingo.Edge/ lab/Pingo.Edge/
RUN dotnet publish lab/Pingo.Edge/Pingo.Edge.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Pingo.Edge.dll"]
