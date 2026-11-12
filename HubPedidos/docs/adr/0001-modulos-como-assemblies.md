# ADR 0001 — Módulos como assemblies separados

**Status:** aceito (atividade 6) · **Data:** 2026-11-12

## Contexto
Até a atividade 5, processadores, fila e regras de pedido conviviam no mesmo projeto
(`HubPedidos.Application`). Havia um vazamento real: `MapeadorPedidos.ParaResposta`
(pedidos) dependia de `ResultadoProcessamento` (processamento).

## Decisão
Cada módulo é um assembly: `HubPedidos.Application` (módulo **Pedidos**),
`HubPedidos.Processamento` e `HubPedidos.Notificacoes`. A direção permitida é
Notificações → Processamento → Pedidos → Domain; Infrastructure → Pedidos; Api → todos.
O compilador impede usar um tipo de módulo não referenciado, e `ArquiteturaTests`
impede adicionar referências proibidas (matriz verificada nos `.csproj` e no assembly compilado).

## Alternativas descartadas
- **Pastas/namespaces no mesmo projeto + teste por reflexão:** reflexão enxerga
  assinaturas, não o corpo dos métodos; a fronteira dependeria de disciplina.
- **Serviços separados (microsserviços):** custo operacional sem necessidade; o
  problema é de dependências, não de implantação.

## Consequências
+ Fronteira verificável, renomeações seguras, módulos testáveis isoladamente.
− Mais projetos; tipos compartilhados precisam de um "dono" explícito
  (ex.: `CategoriaProcessamento` ficou em Pedidos porque a classificação é regra de pedido).
