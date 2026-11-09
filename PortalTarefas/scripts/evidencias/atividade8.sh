#!/usr/bin/env bash
# Evidências da Atividade 8 (build reprodutível, imagem, contêiner, health check, pipeline). Executar com a árvore commitada.
set -u
source .env; source scripts/evid.sh
E=docs/evidencias/atividade8; mkdir -p $E
SHA=$(git rev-parse --short HEAD); export APP_VERSION=$SHA APP_COMMIT=$SHA

# 01 - imagem
bash scripts/docker-build.sh > $E/01-docker-build.txt 2>&1

# 02 - build a partir de clone novo
T=$(mktemp -d); git clone -q "$(git rev-parse --show-toplevel)" $T/clone
( cd $T/clone/PortalTarefas
  echo "\$ git clone <repositório> && cd PortalTarefas   # clone novo, sem bin/obj/node_modules"; git log --oneline -1
  echo; echo "\$ dotnet --version && node --version && npm --version"; dotnet --version; node --version; npm --version
  echo; echo '$ dotnet build -c Release'; dotnet build -c Release --nologo 2>&1 | grep -E "error|Build succeeded|Warn"
  echo; echo '$ dotnet test -c Release --no-build'
  PORTAL_SQL_TESTES="Server=localhost,1433;User Id=sa;Password=$MSSQL_SA_PASSWORD;TrustServerCertificate=True" dotnet test -c Release --no-build --nologo 2>&1 | grep -E "Passed!|Failed!"
  cd src/PortalTarefas.Frontend; echo; echo '$ npm ci && npx vitest run && npm run build'
  npm ci --no-audit --no-fund --silent && echo "npm ci ok"
  npx vitest run 2>&1 | sed 's/\x1b\[[0-9;]*m//g' | grep "Tests "; npm run build 2>&1 | grep -E "built|error"
  echo; echo '-- git status após o build:'; git status --short | head; echo "(vazio = nenhum arquivo gerado/alterado pendente)"
) > $E/02-build-clone-limpo.txt 2>&1; rm -rf $T

# 03 - inspeção da imagem
{ echo "===== Imagem portal-tarefas:$SHA ====="
  docker image ls portal-tarefas:$SHA --format 'tamanho em disco: {{.Size}}'
  echo; echo "-- Rótulos (rastreabilidade):"
  docker image inspect portal-tarefas:$SHA --format '{{range $k,$v := .Config.Labels}}{{$k}}={{$v}}{{"\n"}}{{end}}' | grep opencontainers
  echo "-- Usuário de execução: $(docker image inspect portal-tarefas:$SHA --format '{{.Config.User}}')"; docker run --rm --entrypoint id portal-tarefas:$SHA
  echo; echo "-- Variáveis de ambiente gravadas na imagem:"; docker image inspect portal-tarefas:$SHA --format '{{range .Config.Env}}{{println .}}{{end}}'
  echo "-- Histórico de camadas da imagem final:"; docker history portal-tarefas:$SHA --format '{{.Size}}\t{{.CreatedBy}}' | head -13 | cut -c1-150
  echo; echo "-- Arquivos de configuração presentes em /app:"; docker run --rm --entrypoint sh portal-tarefas:$SHA -c 'ls /app/*.json | grep appsettings; ls -a /app | grep -ciE "^\.env|secrets" >/dev/null && echo "ATENÇÃO: .env/secrets encontrado" || echo "nenhum .env/secrets.json em /app"'
  docker run --rm --entrypoint sh -e S1="$MSSQL_SA_PASSWORD" -e S2="$SEED_SENHA" portal-tarefas:$SHA -c 'echo "ocorrências das senhas locais em todo o sistema de arquivos da imagem: $(grep -rlF -e "$S1" -e "$S2" /app /etc /usr/share/dotnet 2>/dev/null | wc -l)"'
  echo; echo "-- Ferramentas de build/rede ausentes na imagem final:"
  docker run --rm --entrypoint sh portal-tarefas:$SHA -c 'for b in node npm curl wget; do command -v $b >/dev/null && echo "$b presente" || echo "$b ausente"; done; echo "SDKs .NET instalados: $(dotnet --list-sdks | wc -l)"'
} > $E/03-inspecao-imagem.txt 2>&1

# 04 - execução com compose (proxy TLS -> API -> SQL/calendário)
docker compose down -v >/dev/null 2>&1
{ echo "\$ APP_VERSION=$SHA docker compose up -d --no-build   # segredos vêm do .env (fora do Git)"
  docker compose up -d --no-build 2>&1 | grep -E "Started|Healthy|Created" | grep Container
  for i in $(seq 1 40); do st=$(docker inspect -f '{{.State.Health.Status}}' portal-tarefas-api-1 2>/dev/null); [ "$st" = healthy ] && break; sleep 3; done
  echo; echo "Estado do HEALTHCHECK do contêiner da API: $st"
  docker compose ps --format 'table {{.Service}}\t{{.Image}}\t{{.Status}}\t{{.Ports}}' | cut -c1-170
  for i in $(seq 1 20); do curl -sk -o /dev/null https://localhost:8443/health/live && break; sleep 2; done
  echo; req "Liveness via proxy TLS" https://localhost:8443/health/live
  req "Readiness (banco + calendário)" https://localhost:8443/health/ready
  req "Versão implantada (commit -> imagem -> execução)" https://localhost:8443/api/versao
  req "SPA servida pela API (implantação integrada)" https://localhost:8443/app/tarefas | head -9
  req "Sessão anônima: cookies Secure emitidos atrás do proxy" https://localhost:8443/api/v2/auth/sessao | head -12
  req "Rota sem endpoint (ex.: UI Scalar, só existe em Development)" https://localhost:8443/scalar/v2 | head -5
  echo "-- Configuração vista pelo contêiner (nomes; valores ocultos):"
  docker exec portal-tarefas-api-1 sh -c 'env | grep -E "^(ConnectionStrings|SeedUsuarios|Banco|Dependencias|DataProtection|ASPNETCORE_ENVIRONMENT)" | sort | sed -E "s/=.*/=<definida>/"'
  echo; echo "-- Log do contêiner (JSON estruturado, primeiras linhas):"
  docker compose logs api --no-log-prefix 2>&1 | grep '^{' | head -4 | cut -c1-230
} > $E/04-compose-execucao.txt 2>&1
SEED_SENHA="$SEED_SENHA" NODE_PATH=$(npm root -g) node scripts/capturas/atividade8.js $E

# 07 - outro ambiente com a mesma imagem e validação de configuração
{ echo "===== Mesma imagem em Production (sem alterar código), na rede do compose ====="
  echo "\$ docker run -d --name prod-sim --network portal-tarefas_default -e ASPNETCORE_ENVIRONMENT=Production -e ConnectionStrings__PortalTarefas=<omitida> -e Dependencias__Calendario__Url=http://calendario:5099/ portal-tarefas:$SHA"
  docker run -d --name prod-sim --network portal-tarefas_default -p 8081:8080 -e ASPNETCORE_ENVIRONMENT=Production \
    -e ConnectionStrings__PortalTarefas="Server=sql,1433;Database=PortalTarefas;User Id=sa;Password=$MSSQL_SA_PASSWORD;TrustServerCertificate=True" \
    -e Dependencias__Calendario__Url=http://calendario:5099/ portal-tarefas:$SHA >/dev/null
  for i in $(seq 1 20); do curl -s -o /dev/null http://localhost:8081/health/live && break; sleep 1; done
  req "Versão (Production)" http://localhost:8081/api/versao
  req "Readiness (Production)" http://localhost:8081/health/ready
  docker rm -f prod-sim >/dev/null
  echo; echo "===== Configuração obrigatória ausente: falha na inicialização, mensagem cita a chave e não o valor ====="
  echo "\$ docker run --rm -e Dependencias__Calendario__Url= -e ConnectionStrings__PortalTarefas='Server=x;Password=NAO_DEVE_APARECER' portal-tarefas:$SHA"
  SAIDA=$(docker run --rm -e Dependencias__Calendario__Url= -e ConnectionStrings__PortalTarefas='Server=x;Password=NAO_DEVE_APARECER' portal-tarefas:$SHA 2>&1)
  echo "$SAIDA" | grep -E "^Unhandled" | cut -c1-240; echo "código de saída do contêiner: $(docker run --rm -e Dependencias__Calendario__Url= -e ConnectionStrings__PortalTarefas=x portal-tarefas:$SHA >/dev/null 2>&1; echo $?)"
  echo "ocorrências de 'NAO_DEVE_APARECER' na saída: $(echo "$SAIDA" | grep -c NAO_DEVE_APARECER)"
  echo; echo "\$ docker run --rm portal-tarefas:$SHA   # sem connection string"
  docker run --rm portal-tarefas:$SHA 2>&1 | grep -E "^Unhandled" | cut -c1-240
} > $E/07-config-ambientes-e-validacao.txt 2>&1
docker compose down -v >/dev/null 2>&1

# 08 - SBOM e vulnerabilidades
{ echo "===== SBOM (SPDX) da imagem com Syft ====="
  docker run --rm -v /var/run/docker.sock:/var/run/docker.sock anchore/syft:v1.33.0 portal-tarefas:$SHA -o spdx-json 2>/dev/null > $E/sbom-portal-tarefas.spdx.json
  python3 -c "
import json,collections; d=json.load(open('$E/sbom-portal-tarefas.spdx.json'))
pk=d['packages']; print('pacotes inventariados:', len(pk))
tipos=collections.Counter(next((r['referenceType'].split('/')[-1] for r in p.get('externalRefs',[]) if r.get('referenceType')=='purl'), '?') for p in pk)
tipos=collections.Counter(next((r['referenceLocator'].split(':')[1].split('/')[0] for r in p.get('externalRefs',[]) if r.get('referenceType')=='purl'), 'outros') for p in pk)
print('por ecossistema:', dict(tipos.most_common()))
for n in ('Microsoft.EntityFrameworkCore','Dapper','OpenTelemetry','Microsoft.AspNetCore.Identity.EntityFrameworkCore'):
  v=[p['versionInfo'] for p in pk if p['name']==n]; print(f'  {n}: {v[:1]}')"
  echo; echo '$ dotnet list src/PortalTarefas.Web package --vulnerable --include-transitive'
  dotnet list src/PortalTarefas.Web package --vulnerable --include-transitive 2>&1 | grep -vE "^\s*$" | tail -4
  echo; echo '$ npm audit --omit=dev --audit-level=high   (front-end)'
  (cd src/PortalTarefas.Frontend && npm audit --omit=dev --audit-level=high 2>&1 | tail -2)
} > $E/08-sbom-e-vulnerabilidades.txt 2>&1

# 05/06 - pipeline local: sucesso e falha deliberada
rm -rf artifacts/publicado
{ echo '$ scripts/ci-local.sh   # alteração válida'; bash scripts/ci-local.sh 2>&1; echo "código de saída: $?"; cat artifacts/publicado/$SHA/manifesto.json; } > $E/05-ci-local-sucesso.txt 2>&1
{ echo '$ CI_FALHA_DELIBERADA=1 scripts/ci-local.sh   # teste falha de propósito'; rm -rf artifacts/publicado
  CI_FALHA_DELIBERADA=1 bash scripts/ci-local.sh 2>&1; echo "código de saída: $?"
  echo; echo '$ ls artifacts/publicado   # nada foi publicado'; ls artifacts/publicado 2>&1; } > $E/06-ci-local-falha-bloqueia.txt 2>&1
