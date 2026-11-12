# Plano de medição — Atividade 5 (escrito ANTES das execuções comparativas)

## Alvos
| Tipo | Alvo | Por quê |
|---|---|---|
| Rota de criação | `POST /pedidos` (síncrona: valida, classifica, grava, responde 201) | caminho de escrita mais exigente |
| Rota de consulta | `GET /pedidos?clienteId=…&tamanho=20` (projeção + COUNT, índice por cliente) | leitura mais usada pelo painel |
| Método isolável (BenchmarkDotNet) | `ParaDominio` + `Classificar` + `ParaResposta` (caminho de CPU do POST) e `LimitadorBanco.ExecutarAsync` sem disputa | separar custo de CPU do nosso código de espera |

## Hipótese (a partir da sondagem exploratória a 300 criações/s)
H1: a cauda (p95/p99 alto com mediana baixa) vem de **espera por lock no SQLite**,
não de CPU. O provedor responde a `SQLITE_BUSY` dormindo e tentando de novo.
Variante a testar primeiro: disputa leitor × escritor (modo de journal padrão).

## Metas didáticas (carga mista 300 criações/s + 300 consultas/s)
| Métrica | Meta |
|---|---|
| p95 POST / GET | ≤ 50 ms / ≤ 20 ms |
| p99 POST / GET | ≤ 100 ms / ≤ 50 ms |
| Vazão | = taxa ofertada (300/s em cada rota) |
| Erros + timeouts (5 s) | 0 % |
| Regressão funcional | nenhuma (`dotnet test` verde) |
| CPU | sem aumento > 25 pontos percentuais |

Critério de sucesso: uma intervenção é mantida só se atingir as metas sem regressão.

## Ambiente
Ubuntu 24.04.5 LTS (VM), Intel Xeon 2,30 GHz, 4 núcleos; .NET SDK 10.0.114 / runtime 10.0.14;
EF Core 10.0.14 + SQLite 3.53.3 em arquivo local; API em Release, ambiente Production, log em
Warning (`carga/iniciar-api.sh`); banco NOVO por execução com 20.000 pedidos (semente 123);
fila: configuração padrão (cap 500, 4 consumidores, lote 32) — não usada pelas rotas medidas.
Gerador (`carga/HubPedidos.Carga`) na mesma máquina; ferramentas `dotnet-counters`/`dotnet-trace`
10.0.750501 (ferramentas locais, `dotnet-tools.json`).

## Carga
Modelo aberto (taxa constante, sem esperar respostas), latência a partir do instante agendado
(corrige omissão coordenada), aquecimento de 10 s descartado + janela de 30 s, timeout 5 s,
limite de 4.000 requisições em voo no gerador (excedentes = "não enviados").
Script: `carga/medir.sh <rótulo> <criação/s> <consulta/s> <saída> [--trace]`.

## Coleta
- `dotnet-counters collect` (System.Runtime, Hosting, Kestrel, HubPedidos.Fila) durante toda a carga.
- `dotnet-trace collect` (perfil padrão: thread-time amostra todas as threads) por 10 s no meio
  da janela — só em execuções separadas, pois perturba a medição.
- Cada configuração rodada ao menos 2 vezes para estimar a variação.
