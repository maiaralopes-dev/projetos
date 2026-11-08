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
└── tests/HubPedidos.Tests       → unidade, reflexão, arquitetura e integração HTTP
```

Direção das dependências: `Api → Application → Domain`. O domínio não referencia ninguém.

## Como executar

Pré-requisito: SDK .NET 10 (ver `global.json`).

```bash
dotnet build          # warnings são tratados como erro
dotnet test           # 72 testes na atividade 1
dotnet run --project src/HubPedidos.Api --launch-profile http   # http://localhost:5080
```

Requisições de exemplo em `src/HubPedidos.Api/HubPedidos.Api.http` (Visual Studio / VS Code).

## Endpoints (atividade 1)

| Método | Rota             | Descrição                                                         |
|--------|------------------|-------------------------------------------------------------------|
| POST   | `/pedidos`       | Valida DTO → cria domínio → classifica → seleciona processador    |
| GET    | `/processadores` | Lista o catálogo construído por reflexão na inicialização         |

## Evidências

`docs/evidencias/atividade1/` — ambiente, saída dos testes, chamadas HTTP e erro do compilador.
Relatórios em `../relatorios/`.
