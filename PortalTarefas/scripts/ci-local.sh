#!/usr/bin/env bash
# Executa localmente as MESMAS etapas do workflow .github/workflows/portal-tarefas.yml, na mesma ordem.
# Qualquer falha encerra o script (set -e) ANTES da etapa de publicação.
set -euo pipefail
cd "$(dirname "$0")/.."
SHA=$(git rev-parse --short HEAD)
etapa() { echo; echo "::: [$(date +%H:%M:%S)] $*"; }
source .env 2>/dev/null || true
export PORTAL_SQL_TESTES="Server=localhost,1433;User Id=sa;Password=${MSSQL_SA_PASSWORD};TrustServerCertificate=True"

etapa "1/8 dotnet restore";  dotnet restore --nologo -v q
etapa "2/8 dotnet build (Release)"; dotnet build -c Release --no-restore --nologo -v q -p:OpenApiGenerateDocuments=false 2>&1 | grep -E "error|Warn|Build succeeded" || true
etapa "3/8 dotnet test"; dotnet test -c Release --no-build --nologo --logger "trx;LogFileName=testes.trx" --results-directory artifacts/testes 2>&1 | grep -E "Passed!|Failed!|Failed |error" 
etapa "4/8 npm ci";   (cd src/PortalTarefas.Frontend && npm ci --no-audit --no-fund --silent)
etapa "5/8 npm test"; (cd src/PortalTarefas.Frontend && npx vitest run 2>&1 | sed 's/\x1b\[[0-9;]*m//g' | grep -E "Tests |FAIL")
etapa "6/8 npm run build"; (cd src/PortalTarefas.Frontend && npm run build 2>&1 | grep -E "built|error")
etapa "7/8 imagem + smoke test"
scripts/docker-build.sh > artifacts/docker-build.log 2>&1 && echo "imagem portal-tarefas:$SHA construída"
docker rm -f smoke >/dev/null 2>&1 || true
docker run -d --name smoke -p 18080:8080 -e ConnectionStrings__PortalTarefas="Server=nao-usado" \
  -e Dependencias__Calendario__Url=http://nao-usado/ portal-tarefas:$SHA >/dev/null
for i in $(seq 1 30); do curl -sf http://localhost:18080/health/live >/dev/null && break; sleep 1; done
echo "smoke /health/live: $(curl -s -o /dev/null -w '%{http_code}' http://localhost:18080/health/live)"
echo "smoke /api/versao:  $(curl -s http://localhost:18080/api/versao)"
docker rm -f smoke >/dev/null
etapa "8/8 publicação de artefatos (só chega aqui se tudo acima passou)"
DEST=artifacts/publicado/$SHA; mkdir -p $DEST
docker save portal-tarefas:$SHA | gzip > $DEST/imagem.tar.gz
cp -r src/PortalTarefas.Frontend/dist $DEST/frontend-dist
docker run --rm -v /var/run/docker.sock:/var/run/docker.sock anchore/syft:v1.33.0 portal-tarefas:$SHA -o spdx-json > $DEST/sbom.spdx.json 2>/dev/null
cat > $DEST/manifesto.json <<JSON
{ "commit": "$(git rev-parse HEAD)", "imagem": "portal-tarefas:$SHA",
  "imagemId": "$(docker image inspect --format '{{.Id}}' portal-tarefas:$SHA)",
  "geradoEm": "$(date -u +%FT%TZ)", "testes": "artifacts/testes/testes.trx" }
JSON
ls -la $DEST | awk '{print $5, $9}'
echo "PUBLICADO: $DEST"
