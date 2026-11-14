# Estratégia de testes e revisão automatizada

## Portfólio (140 métodos, 214 casos)

| Camada | Métodos | O que garante | Onde |
|---|---:|---|---|
| Unidade (domínio, aplicação, fila, parser, cliente) | 64 | regras, invariantes, concorrência, buffers — rápidos, sem I/O | `ClassificadorTests`, `ImutabilidadeTests`, `ImportadorTests`, `FilaTests`, `ConsumidorTests`… |
| Persistência (EF Core + SQLite em memória, sem HTTP) | 22 | SQL gerado, tracking, N+1, JSON, migrations | `Infra/*` |
| Integração HTTP (WebApplicationFactory, app real) | 47 | contratos, autorização, ETag, cache, fila, versões, SignalR (Kestrel real) | `EndpointTests`, `Http/*`, `Versionamento/*`, `TempoReal/*`, `Integracao/*` |
| Contrato e arquitetura | 7 | matriz de dependências entre assemblies; snapshot do OpenAPI v1/v2 | `ArquiteturaTests`, `Contratos/*` |
| Carga e benchmarks (FORA da suíte) | — | latência/p99 e custo de CPU; caros e dependentes de máquina | `carga/`, `benchmarks/` (atividades 2–5) |

Poucos testes "ponta a ponta" (TempoReal com Kestrel real + proxy de falhas): são os mais lentos e mais
sujeitos a variação, então cobrem só o que não dá para cobrir de outro jeito (queda de rede real).

## Banco efêmero

- **SQLite em memória**: `Data Source=hub-<guid>;Mode=Memory;Cache=Shared` por fábrica (API) ou
  `DataSource=:memory:` por teste (persistência). Nome aleatório = nenhum estado compartilhado entre testes.
- **Conexão-âncora aberta** durante toda a vida da fábrica/teste: no SQLite, o banco em memória
  some quando a última conexão fecha.
- **Esquema pelas migrations reais** (`MigrateAsync`), não `EnsureCreated` — o teste também valida as migrations
  (`HasPendingModelChanges`, `GetPendingMigrations`). O modelo alternativo "relacional" (atividade 3) usa
  `EnsureCreated` porque não tem migration.
- **Dados mínimos por teste**: cada teste cria o que usa (pedidos via HTTP ou `GeradorMassa` com semente fixa).
- **Trava**: `Banco:ExigirEmMemoria=true` na fábrica de testes; connection string de arquivo derruba a inicialização.

### Diferenças em relação ao banco de produção (exigem testes próprios)

| Aspecto | SQLite em memória (testes) | SQLite em arquivo (produção atual) | Se a produção fosse SQL Server/PostgreSQL |
|---|---|---|---|
| Durabilidade | nenhuma, sem `fsync` | `fsync` por transação | idem, com log de transação |
| Concorrência de escrita | cache compartilhado usa lock por **tabela** | lock do **arquivo** inteiro; `SQLITE_BUSY` + espera de ~150 ms (atividade 5) | locks de linha; deadlocks possíveis |
| Tempo das operações | muito mais rápido | o gargalo medido na atividade 5 só aparece aqui | rede + servidor |
| WAL | não se aplica (`journal_mode` = memory) | disponível (medido, desligado) | n/a |
| Tipos | `decimal` como INTEGER (centavos) por conversor | igual | `decimal(18,2)` nativo; conversor dispensável |
| JSON | `->>`, `json_set`, `json_each` | igual | `JSON_VALUE`/`OPENJSON` ou `jsonb` — SQL diferente |
| Colação / maiúsculas | binária por padrão | igual | depende da colação (`CI_AS` ignora caixa!) |
| Tamanho de texto | `HasMaxLength` ignorado | igual | truncamento/erro de verdade |

Consequência: os testes daqui provam **comportamento e SQL gerado para SQLite**. Desempenho, contenção de
escrita e (numa migração de provedor) colação, tipos e JSON precisam de testes contra o provedor real —
por exemplo, um job de CI com contêiner efêmero do banco de produção.

## Determinismo

- Ordem **aleatória** de testes e coleções com semente registrada (`HUB_SEMENTE_TESTES` reproduz);
  `./testes-repetidos.sh 10` roda 10 vezes com sementes diferentes.
- Nenhum `sleep` fixo para esperar processamento assíncrono: `CanalCaptura.AguardarAsync` (evento com prazo)
  ou espera por condição com prazo.
- Testes que medem memória do processo inteiro rodam fora do paralelismo (`SemParalelismo`).

## Revisão automatizada

`./revisao.sh` (local e CI — `.github/workflows/revisao.yml`): restore → build Release com analisadores →
`dotnet format --verify-no-changes` → testes (TRX + cobertura) → resumo em `artifacts/resumo.md`.
O job `publicar` só roda se a revisão passar. Cobertura é publicada como **informação**, não como meta.
