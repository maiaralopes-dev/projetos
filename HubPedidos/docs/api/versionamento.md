# Política de versão — consulta de pedido

## Consumidores conhecidos de `GET /pedidos/{id}`
| Consumidor | Versão | Observação |
|---|---|---|
| Painel web (front-end) | v1 (rota legada e `/api/v1`) | exibe `total` como número e `criadoEmUtc` como UTC |
| Testes de integração / cliente `ClientePainelV1` | v1 | contrato congelado, desserialização estrita |
| Novos integradores (ERP, app) | v2 | precisam de valores exatos e data com fuso |

## Regras
| Item | Regra |
|---|---|
| Leitura da versão | segmento de URL `/api/v{n}/` |
| Versão padrão | rota sem versão `/pedidos/{id}` = v1 (não documentada no OpenAPI para não atrair novos clientes) |
| Versão não suportada | 400 `application/problem+json` com `versoesSuportadas` e cabeçalho `api-supported-versions` |
| Anúncio | toda resposta: `api-supported-versions: 1, 2` |
| Depreciação da v1 | `Deprecation: @1794441600` (2026-11-12), `Sunset: Mon, 31 May 2027 00:00:00 GMT`, `Link: </api/v2/pedidos>; rel="successor-version"` |
| Documentação | `/openapi/v1.json` e `/openapi/v2.json`, separados por grupo |

## Matriz de mudanças v1 → v2
| Campo / aspecto | v1 | v2 | Compatível? |
|---|---|---|---|
| `id`, `clienteId`, `regiao`, `prioridade`, `status`, `categoria`, `entrega`, `metadados` | iguais | iguais | sim |
| `total` | número JSON (`3000000.07`) | objeto `{ "valor": "3000000.07", "moeda": "BRL" }` | **não** (tipo muda) |
| `itens[].precoUnitario`, `itens[].subtotal` | número | objeto dinheiro | **não** |
| `criadoEmUtc` (sem fuso) | `"2026-11-12T00:59:10.02"` | renomeado para `criadoEm` com fuso `"...+00:00"` | **não** (nome e formato) |
| Status HTTP (200/404), ordenação dos itens (por SKU) | iguais | iguais | sim |

Referência de classificação usada: adicionar campo opcional = compatível; remover,
renomear, mudar tipo, mudar significado, mudar valores de enum, mudar status HTTP ou
ordenação documentada = incompatível.

## Critérios para remover a v1
1. Uso observado (`GET /api/versoes` e métrica `hub.api.requisicoes_versionadas`)
   abaixo de 1% das consultas por 30 dias consecutivos, **e** nenhum consumidor conhecido na v1;
2. data de sunset atingida (2027-05-31);
3. aviso aos consumidores conhecidos com 60 dias de antecedência.
Após a remoção: `/api/v1/...` passa a responder 400 pela política de versão não suportada,
e a rota legada passa a 410 Gone por mais um ciclo antes de sair.
