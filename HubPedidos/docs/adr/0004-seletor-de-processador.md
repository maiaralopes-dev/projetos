# ADR 0004 — Resolução por chave confinada no seletor

**Status:** aceito · **Data:** 2026-11-12

## Contexto
O endpoint de criação e o consumidor da fila chamavam
`IServiceProvider.GetRequiredKeyedService<IProcessadorPedido>(categoria)` — service locator
espalhado pela regra.

## Decisão
`ISeletorProcessador` (scoped) é o único ponto que resolve por chave dinâmica; quem
processa recebe o seletor por injeção. O seletor também responde `Suporta(categoria)`
usando o catálogo.

## Alternativas descartadas
- **Injetar todos os processadores (`IEnumerable<IProcessadorPedido>`) e filtrar pelo
  atributo:** instancia todos a cada requisição.
- **Dicionário de fábricas montado à mão:** duplica o que o catálogo + keyed services já fazem.
