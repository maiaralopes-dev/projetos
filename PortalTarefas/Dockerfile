# syntax=docker/dockerfile:1
# Imagem única (implantação integrada): API ASP.NET Core + SPA React servida em /app.
# Imagens base com tag exata + digest: o build não muda se a tag for republicada.

# ---------- 1. Front-end: instala dependências travadas e gera o bundle ----------
FROM node:22.23.4-alpine@sha256:0e1754758c2df4cfa3e68d59e2e1b7374bb7fd9ea6328eaaacf15cc934af8202 AS frontend
WORKDIR /src
COPY src/PortalTarefas.Frontend/package.json src/PortalTarefas.Frontend/package-lock.json ./
# CA extra opcional (proxy corporativo) via BuildKit secret: usada só durante o RUN e nunca gravada na imagem.
RUN --mount=type=secret,id=ca_extra,required=false \
    if [ -f /run/secrets/ca_extra ]; then export NODE_EXTRA_CA_CERTS=/run/secrets/ca_extra; fi; \
    npm ci --ignore-scripts --no-audit --no-fund
COPY src/PortalTarefas.Frontend/ ./
RUN npm run build                                   # tsc -b && vite build -> /src/dist (base /app/)

# ---------- 2. Back-end: restore (camada em cache) -> publish ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0.403@sha256:212fe4cb74627d5adf9817a4088906ae3edff1bd853bb27565d4b46731efe61e AS backend
WORKDIR /src
COPY global.json ./
COPY src/PortalTarefas.Web/PortalTarefas.Web.csproj src/PortalTarefas.Web/
RUN --mount=type=secret,id=ca_extra,required=false \
    if [ -f /run/secrets/ca_extra ]; then export SSL_CERT_FILE=/run/secrets/ca_extra; fi; \
    dotnet restore src/PortalTarefas.Web/PortalTarefas.Web.csproj
COPY src/PortalTarefas.Web/ src/PortalTarefas.Web/
COPY docs/openapi/ docs/openapi/
RUN dotnet publish src/PortalTarefas.Web/PortalTarefas.Web.csproj -c Release -o /app/publish \
      --no-restore -p:OpenApiGenerateDocuments=false -p:UseAppHost=false
COPY --from=frontend /src/dist /app/publish/wwwroot/app

# ---------- 3. Execução: só o runtime ASP.NET Core + arquivos publicados ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0.14@sha256:1a48394b6f29d3a32c79d9edecd4abda0ecbe8776d567dbfc4590ed6979c740c AS final
ARG APP_VERSION=dev
ARG APP_COMMIT=local
LABEL org.opencontainers.image.title="portal-tarefas" \
      org.opencontainers.image.version="${APP_VERSION}" \
      org.opencontainers.image.revision="${APP_COMMIT}" \
      org.opencontainers.image.source="https://github.com/maiaralopes-dev/projetos"
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    DOTNET_NOLOGO=1 \
    APP_VERSION=${APP_VERSION} \
    APP_COMMIT=${APP_COMMIT}
WORKDIR /app
COPY --from=backend --chown=root:root /app/publish ./
# Usuário não privilegiado já existente na imagem oficial (UID 1654). Arquivos da aplicação pertencem ao root (somente leitura para o app).
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=5s --start-period=30s --retries=3 \
  CMD ["dotnet", "PortalTarefas.Web.dll", "--sondar-saude"]
ENTRYPOINT ["dotnet", "PortalTarefas.Web.dll"]
