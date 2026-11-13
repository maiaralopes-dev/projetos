# Output cache do painel — carga (modelo aberto, API Release/Production, banco novo com 20.000 pedidos)

Contagem de 'Age' inclui o aquecimento (10 s) + medição (30 s).

## Carga 'sem-cache-20' — 2026-11-13 16:49 UTC

Modelo aberto; criação 20/s + consulta 20/s; aquecimento 10s (descartado); medição 30s; timeout 5000 ms; máx. em voo 16; não enviados (gerador saturado) 0.
Consulta: /painel/regioes; respostas com cabeçalho Age (vindas do output cache): 0.
Latência medida desde o instante agendado (corrige omissão coordenada). Percentis por posição (nearest-rank) sobre TODAS as requisições da janela de medição, inclusive erros e timeouts (timeout conta como o valor do timeout).

| Rota | Ofertadas | OK (2xx) | Erros HTTP | Timeouts | Outros | Vazão OK/s | p50 ms | p95 ms | p99 ms | máx ms |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| GET /painel/regioes | 600 | 600 | 0 | 0 | 0 | 20.0 | 64.8 | 87.8 | 90.9 | 102.0 |
| POST /pedidos | 600 | 600 | 0 | 0 | 0 | 20.0 | 5.0 | 10.1 | 15.8 | 28.3 |

## Carga 'com-cache-20' — 2026-11-13 16:51 UTC

Modelo aberto; criação 20/s + consulta 20/s; aquecimento 10s (descartado); medição 30s; timeout 5000 ms; máx. em voo 11; não enviados (gerador saturado) 0.
Consulta: /painel/regioes; respostas com cabeçalho Age (vindas do output cache): 527.
Latência medida desde o instante agendado (corrige omissão coordenada). Percentis por posição (nearest-rank) sobre TODAS as requisições da janela de medição, inclusive erros e timeouts (timeout conta como o valor do timeout).

| Rota | Ofertadas | OK (2xx) | Erros HTTP | Timeouts | Outros | Vazão OK/s | p50 ms | p95 ms | p99 ms | máx ms |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| GET /painel/regioes | 600 | 600 | 0 | 0 | 0 | 20.0 | 11.2 | 75.9 | 85.4 | 88.5 |
| POST /pedidos | 600 | 600 | 0 | 0 | 0 | 20.0 | 3.9 | 6.7 | 7.9 | 23.4 |

## Carga 'com-cache-200-sem-escrita' — 2026-11-13 16:52 UTC

Modelo aberto; criação 0/s + consulta 200/s; aquecimento 10s (descartado); medição 30s; timeout 5000 ms; máx. em voo 67; não enviados (gerador saturado) 0.
Consulta: /painel/regioes; respostas com cabeçalho Age (vindas do output cache): 7998.
Latência medida desde o instante agendado (corrige omissão coordenada). Percentis por posição (nearest-rank) sobre TODAS as requisições da janela de medição, inclusive erros e timeouts (timeout conta como o valor do timeout).

| Rota | Ofertadas | OK (2xx) | Erros HTTP | Timeouts | Outros | Vazão OK/s | p50 ms | p95 ms | p99 ms | máx ms |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| GET /painel/regioes | 6000 | 6000 | 0 | 0 | 0 | 200.0 | 2.2 | 3.8 | 5.4 | 186.0 |

## Carga 'cache-ligado' — 2026-11-13 16:47 UTC

Modelo aberto; criação 20/s + consulta 200/s; aquecimento 10s (descartado); medição 30s; timeout 5000 ms; máx. em voo 80; não enviados (gerador saturado) 0.
Consulta: /painel/regioes; respostas com cabeçalho Age (vindas do output cache): 7603.
Latência medida desde o instante agendado (corrige omissão coordenada). Percentis por posição (nearest-rank) sobre TODAS as requisições da janela de medição, inclusive erros e timeouts (timeout conta como o valor do timeout).

| Rota | Ofertadas | OK (2xx) | Erros HTTP | Timeouts | Outros | Vazão OK/s | p50 ms | p95 ms | p99 ms | máx ms |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| GET /painel/regioes | 6000 | 6000 | 0 | 0 | 0 | 200.0 | 16.9 | 65.1 | 80.9 | 114.6 |
| POST /pedidos | 600 | 600 | 0 | 0 | 0 | 20.0 | 3.6 | 6.1 | 7.9 | 8.7 |

## Carga 'cache-desligado' — 2026-11-13 16:48 UTC

Modelo aberto; criação 20/s + consulta 200/s; aquecimento 10s (descartado); medição 30s; timeout 5000 ms; máx. em voo 1145; não enviados (gerador saturado) 0.
Consulta: /painel/regioes; respostas com cabeçalho Age (vindas do output cache): 0.
Latência medida desde o instante agendado (corrige omissão coordenada). Percentis por posição (nearest-rank) sobre TODAS as requisições da janela de medição, inclusive erros e timeouts (timeout conta como o valor do timeout).

| Rota | Ofertadas | OK (2xx) | Erros HTTP | Timeouts | Outros | Vazão OK/s | p50 ms | p95 ms | p99 ms | máx ms |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| GET /painel/regioes | 6000 | 5 | 0 | 5995 | 0 | 0.0 | 5000.0 | 5000.0 | 5000.0 | 5000.0 |
|  ↳ erros: timeout=5995 |||||||||||
| POST /pedidos | 600 | 1 | 0 | 599 | 0 | 0.0 | 5000.0 | 5000.0 | 5000.0 | 5000.0 |
|  ↳ erros: timeout=599 |||||||||||

