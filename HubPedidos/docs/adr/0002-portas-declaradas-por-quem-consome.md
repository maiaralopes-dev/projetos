# ADR 0002 — Portas declaradas por quem consome

**Status:** aceito · **Data:** 2026-11-12

## Decisão
A interface fica no módulo que PRECISA do serviço; a implementação fica fora:
- `IRepositorioPedidos`, `IConsultasPedidos`: declaradas em Pedidos, implementadas em Infrastructure.
- `INotificadorPedidos`: declarada em Processamento (é ele que avisa), implementada em
  Notificações (`RegistroNotificacoes`). Sem o módulo de notificações, vale `NotificadorNulo`.

## Alternativas descartadas
- **Projeto "Contracts" compartilhado com todas as interfaces:** vira um depósito que
  todos referenciam; esconde quem depende de quem.
- **Notificações chamadas diretamente pelo Processamento:** inverteria a dependência e
  impediria trocar o canal (SignalR na atividade 7) sem mexer no processamento.

## Consequências
+ Trocar EF/SQLite ou o canal de notificação não altera regra de negócio.
− Um nível de indireção a mais; registro no composition root precisa ser correto
  (coberto por `ComposicaoTests`).
