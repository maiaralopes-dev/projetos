# ADR 0003 — Versionamento por segmento de URL

**Status:** aceito · **Data:** 2026-11-12

## Decisão
`/api/v{n}/...`; rota legada sem versão = v1; versão desconhecida = 400 com as versões
suportadas; v1 anuncia `Deprecation`/`Sunset`/`Link: successor-version`; um documento
OpenAPI por versão. Detalhes e matriz de mudanças em `docs/api/versionamento.md`.

## Alternativas descartadas
- **Cabeçalho (`api-version`):** URL limpa, mas invisível em logs/links e esquecido por
  clientes; o cache precisaria variar pelo cabeçalho.
- **Query string (`?api-version=2`):** fácil de omitir sem perceber.
- **Media type (`application/vnd.hub.v2+json`):** mais "REST", porém mais difícil de
  testar e documentar para o front-end.
- **Biblioteca Asp.Versioning:** completa, mas para UM endpoint versionado a política
  explícita em ~100 linhas é mais fácil de explicar e testar; reavaliar se o número de
  endpoints versionados crescer.
