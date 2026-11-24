# Built from the repository root by deploy/multi-region/docker-compose.yml.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Shared/Pingo.ServiceDefaults/Pingo.ServiceDefaults.csproj Shared/Pingo.ServiceDefaults/
COPY Edge/Pingo.Edge/Pingo.Edge.csproj Edge/Pingo.Edge/
RUN dotnet restore Edge/Pingo.Edge/Pingo.Edge.csproj

COPY Shared/ Shared/
COPY Edge/Pingo.Edge/ Edge/Pingo.Edge/
RUN dotnet publish Edge/Pingo.Edge/Pingo.Edge.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Pingo.Edge.dll"]
