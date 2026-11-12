## Carga 'degrau-antes-400' — 2026-11-12 00:38 UTC

Modelo aberto; criação 400/s + consulta 300/s; aquecimento 10s (descartado); medição 30s; timeout 5000 ms; máx. em voo 2584; não enviados (gerador saturado) 0.
Latência medida desde o instante agendado (corrige omissão coordenada). Percentis por posição (nearest-rank) sobre TODAS as requisições da janela de medição, inclusive erros e timeouts (timeout conta como o valor do timeout).

| Rota | Ofertadas | OK (2xx) | Erros HTTP | Timeouts | Outros | Vazão OK/s | p50 ms | p95 ms | p99 ms | máx ms |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| GET /pedidos?clienteId | 9000 | 9000 | 0 | 0 | 0 | 300.0 | 1248.4 | 3233.2 | 3569.0 | 3831.1 |
| POST /pedidos | 12000 | 12000 | 0 | 0 | 0 | 400.0 | 1268.1 | 3273.9 | 3608.3 | 4561.6 |

## Carga 'degrau-antes-450' — 2026-11-12 00:39 UTC

Modelo aberto; criação 450/s + consulta 300/s; aquecimento 10s (descartado); medição 30s; timeout 5000 ms; máx. em voo 3441; não enviados (gerador saturado) 0.
Latência medida desde o instante agendado (corrige omissão coordenada). Percentis por posição (nearest-rank) sobre TODAS as requisições da janela de medição, inclusive erros e timeouts (timeout conta como o valor do timeout).

| Rota | Ofertadas | OK (2xx) | Erros HTTP | Timeouts | Outros | Vazão OK/s | p50 ms | p95 ms | p99 ms | máx ms |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| GET /pedidos?clienteId | 9000 | 9000 | 0 | 0 | 0 | 300.0 | 2908.7 | 4077.8 | 4298.5 | 4595.6 |
| POST /pedidos | 13500 | 13496 | 0 | 4 | 0 | 449.0 | 2947.0 | 4115.8 | 4405.5 | 5000.0 |
|  ↳ erros: timeout=4 |||||||||||

## Carga 'degrau-depois-1000' — 2026-11-12 00:37 UTC

Modelo aberto; criação 1000/s + consulta 300/s; aquecimento 10s (descartado); medição 30s; timeout 5000 ms; máx. em voo 4001; não enviados (gerador saturado) 8452.
Latência medida desde o instante agendado (corrige omissão coordenada). Percentis por posição (nearest-rank) sobre TODAS as requisições da janela de medição, inclusive erros e timeouts (timeout conta como o valor do timeout).

| Rota | Ofertadas | OK (2xx) | Erros HTTP | Timeouts | Outros | Vazão OK/s | p50 ms | p95 ms | p99 ms | máx ms |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| GET /pedidos?clienteId | 9000 | 7528 | 0 | 0 | 1472 | 250.0 | 2814.5 | 5000.0 | 5000.0 | 5000.0 |
|  ↳ erros: nao_enviado=1472 |||||||||||
| POST /pedidos | 30000 | 24032 | 0 | 0 | 5968 | 801.0 | 4074.7 | 5000.0 | 5000.0 | 5000.0 |
|  ↳ erros: nao_enviado=5968 |||||||||||

## Carga 'degrau-depois-450' — 2026-11-12 00:33 UTC

Modelo aberto; criação 450/s + consulta 300/s; aquecimento 10s (descartado); medição 30s; timeout 5000 ms; máx. em voo 329; não enviados (gerador saturado) 0.
Latência medida desde o instante agendado (corrige omissão coordenada). Percentis por posição (nearest-rank) sobre TODAS as requisições da janela de medição, inclusive erros e timeouts (timeout conta como o valor do timeout).

| Rota | Ofertadas | OK (2xx) | Erros HTTP | Timeouts | Outros | Vazão OK/s | p50 ms | p95 ms | p99 ms | máx ms |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| GET /pedidos?clienteId | 9000 | 9000 | 0 | 0 | 0 | 300.0 | 2.5 | 4.9 | 5.9 | 12.6 |
| POST /pedidos | 13500 | 13500 | 0 | 0 | 0 | 450.0 | 3.6 | 6.3 | 8.9 | 46.2 |

## Carga 'degrau-depois-600' — 2026-11-12 00:34 UTC

Modelo aberto; criação 600/s + consulta 300/s; aquecimento 10s (descartado); medição 30s; timeout 5000 ms; máx. em voo 650; não enviados (gerador saturado) 0.
Latência medida desde o instante agendado (corrige omissão coordenada). Percentis por posição (nearest-rank) sobre TODAS as requisições da janela de medição, inclusive erros e timeouts (timeout conta como o valor do timeout).

| Rota | Ofertadas | OK (2xx) | Erros HTTP | Timeouts | Outros | Vazão OK/s | p50 ms | p95 ms | p99 ms | máx ms |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| GET /pedidos?clienteId | 9000 | 9000 | 0 | 0 | 0 | 300.0 | 2.6 | 5.0 | 6.0 | 23.4 |
| POST /pedidos | 18000 | 18000 | 0 | 0 | 0 | 600.0 | 4.3 | 8.2 | 23.4 | 90.5 |

## Carga 'degrau-depois-800' — 2026-11-12 00:36 UTC

Modelo aberto; criação 800/s + consulta 300/s; aquecimento 10s (descartado); medição 30s; timeout 5000 ms; máx. em voo 2926; não enviados (gerador saturado) 0.
Latência medida desde o instante agendado (corrige omissão coordenada). Percentis por posição (nearest-rank) sobre TODAS as requisições da janela de medição, inclusive erros e timeouts (timeout conta como o valor do timeout).

| Rota | Ofertadas | OK (2xx) | Erros HTTP | Timeouts | Outros | Vazão OK/s | p50 ms | p95 ms | p99 ms | máx ms |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| GET /pedidos?clienteId | 9000 | 9000 | 0 | 0 | 0 | 300.0 | 1558.7 | 1654.6 | 1684.6 | 1743.1 |
| POST /pedidos | 24000 | 24000 | 0 | 0 | 0 | 800.0 | 2840.2 | 2930.9 | 2962.7 | 3012.9 |

