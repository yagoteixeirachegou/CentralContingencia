# syntax=docker/dockerfile:1
# Imagem única: a API ASP.NET Core serve o build do React em wwwroot (mesma origem). Contexto de build: raiz do repositório.

FROM node:22-alpine AS web
WORKDIR /web
COPY src/central-contingencia-web/package.json src/central-contingencia-web/package-lock.json ./
RUN npm ci
COPY src/central-contingencia-web/ ./
RUN npm run build

# global.json (SDK 10.0.300 local) não é copiado: a imagem traz o SDK 10.0.x mais recente, de outra faixa de funcionalidade.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/CentralContingencia.Domain/CentralContingencia.Domain.csproj src/CentralContingencia.Domain/
COPY src/CentralContingencia.Application/CentralContingencia.Application.csproj src/CentralContingencia.Application/
COPY src/CentralContingencia.Infrastructure/CentralContingencia.Infrastructure.csproj src/CentralContingencia.Infrastructure/
COPY src/CentralContingencia.Api/CentralContingencia.Api.csproj src/CentralContingencia.Api/
RUN dotnet restore src/CentralContingencia.Api/CentralContingencia.Api.csproj
COPY src/CentralContingencia.Domain/ src/CentralContingencia.Domain/
COPY src/CentralContingencia.Application/ src/CentralContingencia.Application/
COPY src/CentralContingencia.Infrastructure/ src/CentralContingencia.Infrastructure/
COPY src/CentralContingencia.Api/ src/CentralContingencia.Api/
RUN dotnet publish src/CentralContingencia.Api/CentralContingencia.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
COPY --from=web /web/dist ./wwwroot
ENV ASPNETCORE_ENVIRONMENT=Production
# A Railway define PORT em tempo de execução e a API a lê (PlatformHosting): ENTRYPOINT em forma exec, sem shell, para que o comando de
# pré-deploy/inicialização da plataforma (que substitui o ENTRYPOINT em forma exec) possa ser "dotnet CentralContingencia.Api.dll --migrate" etc.
# O TLS termina no proxy da plataforma. Sem PORT (execução local), escuta em 8080.
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "CentralContingencia.Api.dll"]
