# Hub de Processamento de Pedidos

Solução cumulativa das aulas práticas de **Programação Aplicada em .NET** (.NET 10 / C# 14).
Cada atividade é entregue em um commit próprio (recomenda-se criar a tag `atividade-N` ao concluir).

## Estrutura

```
HubPedidos.slnx
├── src/
│   ├── HubPedidos.Domain        → entidade Pedido, value objects (PedidoId, ItemPedido), invariantes
│   ├── HubPedidos.Application   → DTOs (Contratos), mapeador, classificador, processadores e catálogo
│   └── HubPedidos.Api           → composition root e endpoints (Minimal API)
├── tests/HubPedidos.Tests       → unidade, reflexão, arquitetura e integração HTTP
└── benchmarks/HubPedidos.Benchmarks → BenchmarkDotNet (parsing de lote e escrita NDJSON)
```

Direção das dependências: `Api → Application → Domain`. O domínio não referencia ninguém.

## Como executar

Pré-requisito: SDK .NET 10 (ver `global.json`).

```bash
dotnet build          # warnings são tratados como erro
dotnet test           # 108 testes (atividades 1 e 2)
dotnet run --project src/HubPedidos.Api --launch-profile http   # http://localhost:5080
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

Formato do lote: `clienteId;regiao;prioridade;sku:quantidade:preco|sku:quantidade:preco`.
Limites configuráveis em `appsettings.json`, seção `Importacao`.

## Evidências

`docs/evidencias/atividadeN/` — ambiente, saída dos testes, chamadas HTTP, benchmarks e erros do compilador.
Relatórios em `../relatorios/`.
