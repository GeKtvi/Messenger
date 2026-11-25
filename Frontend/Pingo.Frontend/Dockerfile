# Build from the repository root, like the API images:
#   docker build -f Frontend/Pingo.Frontend/Dockerfile -t pingo-frontend .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Frontend/Pingo.Frontend/Pingo.Frontend.csproj Frontend/Pingo.Frontend/
RUN dotnet restore Frontend/Pingo.Frontend/Pingo.Frontend.csproj

COPY Frontend/Pingo.Frontend/ Frontend/Pingo.Frontend/
RUN dotnet publish Frontend/Pingo.Frontend/Pingo.Frontend.csproj -c Release -o /app --no-restore

# Published Blazor WebAssembly is only static files, so no .NET is needed to serve it. This nginx
# variant doesn't run as root and listens on 8080.
FROM nginxinc/nginx-unprivileged:1.30-alpine
COPY Frontend/Pingo.Frontend/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/wwwroot /usr/share/nginx/html
EXPOSE 8080
