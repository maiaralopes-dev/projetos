# Hub de Processamento de Pedidos

Solução cumulativa das aulas práticas de **Programação Aplicada em .NET** (.NET 10 / C# 14).
Cada atividade é entregue em um commit próprio (recomenda-se criar a tag `atividade-N` ao concluir).

## Estrutura

```
HubPedidos.slnx
├── src/
│   ├── HubPedidos.Domain        → entidade Pedido, value objects (PedidoId, ItemPedido), invariantes
│   ├── HubPedidos.Application   → DTOs (Contratos), mapeador, classificador, processadores, interfaces de persistência
│   ├── HubPedidos.Infrastructure → EF Core 10 + SQLite: DbContext, migrations, repositório e consultas
│   └── HubPedidos.Api           → composition root e endpoints (Minimal API)
├── tests/HubPedidos.Tests       → unidade, reflexão, arquitetura e integração HTTP
└── benchmarks/HubPedidos.Benchmarks → BenchmarkDotNet (parsing de lote e escrita NDJSON)
```

Direção das dependências: `Api → Infrastructure → Application → Domain`. O domínio não referencia ninguém; Application não referencia EF Core.

## Como executar

Pré-requisito: SDK .NET 10 (ver `global.json`).

```bash
dotnet build          # warnings são tratados como erro
dotnet test           # 135 testes (atividades 1 a 3)
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

Formato do lote: `clienteId;regiao;prioridade;sku:quantidade:preco|sku:quantidade:preco`.
Limites configuráveis em `appsettings.json`, seção `Importacao`.

## Evidências

`docs/evidencias/atividadeN/` — ambiente, saída dos testes, chamadas HTTP, benchmarks e erros do compilador.
Relatórios em `../relatorios/`.
