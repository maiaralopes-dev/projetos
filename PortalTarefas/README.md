# Portal de Tarefas — Desenvolvimento Web com .NET (UNIP · ADS)

Aplicação full-stack construída de forma cumulativa nas 8 aulas práticas da disciplina
(ASP.NET Core 10 · EF Core 10 · SQL Server · React + TypeScript · Identity · OpenTelemetry · Docker).

## Relatórios

| # | Tema | Relatório (editável / PDF) | Evidências | Commit |
|---|------|----------------------------|------------|--------|
| 1 | Pipeline HTTP, middleware, binding, Minimal APIs × MVC | `docs/relatorios/Relatorio-01-*.docx` / `.pdf` | `docs/evidencias/atividade1` | `aa99393` |
| 2 | MVC, Razor, validação, filtros | `Relatorio-02-*` | `atividade2` | `e6a3427` |
| 3 | EF Core, migrations, seed, transações, concorrência, Dapper | `Relatorio-03-*` | `atividade3` | `614fc20` |
| 4 | API REST, paginação, Problem Details, OpenAPI, cliente TS | `Relatorio-04-*` | `atividade4` | `65c1594` |
| 5 | SPA React com estados de requisição e roteamento | `Relatorio-05-*` | `atividade5` | `28b4e8b` |
| 6 | Identity, cookie, roles, claims, rotas protegidas | `Relatorio-06-*` | `atividade6` | `6315a9c` |
| 7 | Logs, métricas, traces, health checks, resiliência, rate limiting, cache | `Relatorio-07-*` | `atividade7` | `6a1dece` |
| 8 | Configuração, segredos, contêineres, CI/CD | `Relatorio-08-*` | `atividade8` | `6c526ad` |

Antes de entregar, preencha na capa de cada relatório: **Nome, RA, Polo e Local da realização**.
Os relatórios são gerados por `python3 docs/relatorios/gerador/gerar_relatorios.py` (python-docx + LibreOffice).

> As evidências de cada pasta foram coletadas no estado do código da respectiva atividade
> (os scripts `scripts/evidencias/atividadeN.sh` das unidades 1–5 rodam sem autenticação,
> que só passou a existir na unidade 6).

## Estrutura

```
src/PortalTarefas.Web        API + MVC (ASP.NET Core 10), EF Core, Identity, OpenTelemetry
src/PortalTarefas.Frontend   SPA React (Vite + TypeScript), cliente gerado pelo NSwag
tests/PortalTarefas.Tests    xUnit: unidade + integração (WebApplicationFactory + SQL Server isolado)
docs/openapi                 Contrato OpenAPI gerado no build (snapshot versionado)
docs/evidencias              Saídas, logs e capturas de cada atividade
docs/relatorios              Relatórios .docx/.pdf e o gerador
scripts/                     Coleta de evidências, CI local, build da imagem, dependência simulada
```

## Executar localmente

Pré-requisitos: SDK .NET 10, Node.js 22 LTS, Docker.

```bash
cp .env.example .env                       # defina MSSQL_SA_PASSWORD e SEED_SENHA (não versionado)
source .env
docker run -d --name portal-sql -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD="$MSSQL_SA_PASSWORD" -p 1433:1433 mcr.microsoft.com/mssql/server:2022-latest
cd src/PortalTarefas.Web
dotnet user-secrets set "ConnectionStrings:PortalTarefas" "Server=localhost,1433;Database=PortalTarefas;User Id=sa;Password=$MSSQL_SA_PASSWORD;TrustServerCertificate=True"
dotnet user-secrets set "SeedUsuarios:SenhaInicial" "$SEED_SENHA"
dotnet run --launch-profile https           # https://localhost:7080 (migrations + seed em Development)
node ../../scripts/dependencia-calendario.js # dependência simulada (porta 5099), em outro terminal
cd ../PortalTarefas.Frontend && npm ci && npm run dev   # http://localhost:5173
```

Usuários de teste (senha = `SEED_SENHA`): `ana@portal.local` (Usuario/Backend), `bruno@portal.local` (Usuario/Frontend),
`carla@portal.local` (Administrador + claim de outras equipes), `visitante@portal.local` (sem papel).

Testes: `scripts/testes.sh` (.NET, 84 testes) e `npm test` no front-end (22 testes).
Ambiente completo em contêineres: `docker compose up -d --build` → `https://localhost:8443/app/`.
Pipeline local equivalente ao GitHub Actions: `scripts/ci-local.sh`.
