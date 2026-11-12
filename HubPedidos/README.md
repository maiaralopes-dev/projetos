# Hub de Processamento de Pedidos

Solução cumulativa das aulas práticas de **Programação Aplicada em .NET** (.NET 10 / C# 14).
Cada atividade é entregue em um commit próprio (recomenda-se criar a tag `atividade-N` ao concluir).

## Estrutura (módulos = assemblies, atividade 6)

```
HubPedidos.slnx
├── src/
│   ├── HubPedidos.Domain          → Pedido, ItemPedido, PedidoId… (sem dependências)
│   ├── HubPedidos.Application     → módulo PEDIDOS: contratos v1/v2, mapeador, classificação, importação, portas de persistência
│   ├── HubPedidos.Processamento   → módulo PROCESSAMENTO: processadores, catálogo, fila, consumidor, porta INotificadorPedidos
│   ├── HubPedidos.Notificacoes    → módulo NOTIFICAÇÕES: implementa INotificadorPedidos
│   ├── HubPedidos.Infrastructure  → EF Core 10 + SQLite: implementa as portas de Pedidos
│   └── HubPedidos.Api             → composition root + endpoints por módulo (Modulos/…)
├── tests/HubPedidos.Tests         → unidade, arquitetura, composição de DI, integração HTTP, versões
├── benchmarks/HubPedidos.Benchmarks
└── carga/                         → gerador de carga e scripts de diagnóstico (atividade 5)
```

Direção das dependências (verificada por `ArquiteturaTests`): Notificações → Processamento → Pedidos → Domain;
Infrastructure → Pedidos; Api → todos. Diagrama em `docs/arquitetura/modulos.svg`, decisões em `docs/adr/`,
política de versão em `docs/api/versionamento.md`.

## Como executar

Pré-requisito: SDK .NET 10 (ver `global.json`).

```bash
dotnet build          # warnings são tratados como erro
dotnet test           # 185 testes (atividades 1 a 6)
dotnet run --project src/HubPedidos.Api --launch-profile http   # http://localhost:5080
```

Em `Development` a API aplica as migrations e semeia 20.000 pedidos em `src/HubPedidos.Api/hubpedidos.db`
(SQLite, sem servidor). O log mostra o SQL de cada comando, com parâmetros ocultos (`'?'`).

Migrations (ferramenta local, versão fixada em `dotnet-tools.json`):

```bash
dotnet tool restore
dotnet ef migrations add <Nome> --project src/HubPedidos.Infrastructure --startup-project src/HubPedidos.Infrastructure --output-dir Persistencia/Migrations
dotnet ef migrations script --project src/HubPedidos.Infrastructure --startup-project src/HubPedidos.Infrastructure
```

Benchmarks (sempre em Release, fora do depurador):

```bash
dotnet run -c Release --project benchmarks/HubPedidos.Benchmarks -- --filter "*"
```

Diagnóstico sob carga (atividade 5) — gerador em modo aberto, API em Release, counters e trace:

```bash
dotnet tool restore
carga/medir.sh depois 300 300 saida/            # carga + dotnet-counters
carga/medir.sh depois 300 300 saida/ --trace    # + dotnet-trace de 10 s
Banco__SerializarEscritasHttp=false carga/medir.sh antes 300 300 saida/   # comportamento anterior
python3 carga/resumir_counters.py saida/counters-depois.csv 2
dotnet dotnet-trace report saida/trace-depois.nettrace topN -n 20
```

Requisições de exemplo em `src/HubPedidos.Api/HubPedidos.Api.http` (Visual Studio / VS Code).

## Endpoints

| Ativ. | Método | Rota             | Descrição                                                         |
|-------|--------|------------------|-------------------------------------------------------------------|
| 1     | POST   | `/pedidos`       | Valida DTO → cria domínio → classifica → seleciona processador    |
| 1     | GET    | `/processadores` | Lista o catálogo construído por reflexão na inicialização         |
| 2     | POST   | `/pedidos/lote`  | Lote em texto (uma linha por pedido) → NDJSON com um resultado por linha |
| 3     | GET    | `/pedidos?clienteId=&regiao=&status=&pagina=&tamanho=` | Listagem paginada (offset) com total |
| 3     | GET    | `/pedidos/cursor?apos=&tamanho=` | Listagem por cursor (keyset)                       |
| 3     | GET    | `/pedidos/{id}`  | Detalhe com itens, endereço (tipo complexo) e metadados (JSON)    |
| 3     | PUT    | `/pedidos/{id}/status` | Altera status (máquina de estados; 204/404/409)             |
| 3     | GET    | `/relatorios/pedidos-itens` | Pedidos com itens em uma única consulta                |
| 3     | GET    | `/painel/regioes` | Agregações por região calculadas no banco                        |
| 4     | POST   | `/pedidos/fila`  | Valida, classifica e enfileira → 202 (ou 503 + `Retry-After` com fila cheia) |
| 4     | GET    | `/fila/metricas` | Profundidade, aceitos, rejeitados, descartados, processados, uso do banco |
| 6     | GET    | `/api/v1/pedidos/{id}` | Detalhe v1 (depreciada; = rota legada `/pedidos/{id}`) |
| 6     | GET    | `/api/v2/pedidos/{id}` | Detalhe v2 (dinheiro como objeto string+moeda, data com fuso) |
| 6     | GET    | `/api/versoes`   | Versões suportadas, depreciação e uso observado por versão |
| 6     | GET    | `/openapi/v1.json`, `/openapi/v2.json` | Documentação separada por versão |
| 6     | GET    | `/notificacoes/recentes` | Últimas notificações de mudança de status |

A partir da atividade 4, `POST /pedidos/lote` também enfileira cada linha aceita. A fila
(`Channel` limitado) é configurada na seção `Fila` do `appsettings.json` e consumida por
`ConsumidorPedidos` (BackgroundService). Experimento de backpressure:

```bash
dotnet run -c Release --project benchmarks/HubPedidos.Benchmarks -- experimento-fila linha-tempo.csv
python3 docs/scripts/grafico_fila.py linha-tempo.csv profundidade-fila.svg
```

Formato do lote: `clienteId;regiao;prioridade;sku:quantidade:preco|sku:quantidade:preco`.
Limites configuráveis em `appsettings.json`, seção `Importacao`.

## Evidências

`docs/evidencias/atividadeN/` — ambiente, saída dos testes, chamadas HTTP, benchmarks e erros do compilador.
Relatórios em `../relatorios/`.
