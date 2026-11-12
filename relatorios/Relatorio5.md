# PROGRAMAÇÃO APLICADA EM .NET — RELATÓRIO DE AULAS PRÁTICAS

**Nome:** ______________________ **RA:** ______________ **Polo:** ______________
**Local da realização da aula prática:** ______________ **Ano da postagem:** 2026

---

## Relatório 5 — BenchmarkDotNet, counters, traces, p95/p99 e testes de carga

**Unidade 5 – Diagnóstico e perfilamento**

**Objetivo:** diagnosticar o Hub sob carga, estabelecer metas de latência e investigar gargalos com evidências reproduzíveis.

## 1. Entrega e ambiente

- **Repositório:** `HubPedidos/`. Itens novos desta atividade:
  - `carga/HubPedidos.Carga`: gerador de carga em C#;
  - `carga/iniciar-api.sh` e `carga/medir.sh`: sobem a API e repetem o experimento sempre do mesmo jeito;
  - `carga/resumir_counters.py`: transforma o CSV dos counters numa linha do tempo;
  - `benchmarks/.../CriacaoBenchmarks.cs`: microbenchmark do método isolável;
  - `docs/evidencias/atividade5/`: plano de medição e resultados.
- **Ferramentas:** `dotnet-counters` e `dotnet-trace` **10.0.750501**, instaladas como ferramentas locais com versão fixa em `dotnet-tools.json` (`dotnet tool restore`).
- **Reproduzir:** `carga/medir.sh <rótulo> 300 300 <saída> [--trace]`. Para obter o comportamento anterior: `Banco__SerializarEscritasHttp=false`.

| Item | Valor registrado |
|---|---|
| Hardware / SO | VM Intel Xeon 2,30 GHz, 4 núcleos; Ubuntu 24.04.5 LTS *(trocar pelo seu)* |
| SDK / runtime | .NET SDK 10.0.114 / .NET 10.0.14; build **Release**, ambiente **Production**, log em Warning |
| Banco | SQLite 3.53.3 em arquivo local (EF Core 10.0.14); **banco novo por execução** com 20.000 pedidos (semente 123) |
| Fila | cap 500, 4 consumidores, lote 32. Não participa das rotas medidas |
| Gerador | modelo aberto, mesma máquina da API, timeout de 5 s, até 4.000 requisições em voo |
| BenchmarkDotNet | 0.15.8, Release, Warmup=3, Iterations=15 |
| IDE | Visual Studio 2026 versão ______ *(preencher)* |

## 2. Resumo teórico aplicado à atividade

**Quatro ferramentas, quatro perguntas.**

- O **microbenchmark** (BenchmarkDotNet) mede um método isolado, em laço, sem rede nem banco, e responde "quanto custa este código?".
- O **profiling/trace** (`dotnet-trace`) registra *onde* o tempo é gasto dentro do processo durante um intervalo.
- O **monitoramento de processo** (`dotnet-counters`) mostra *sinais agregados* ao longo do tempo: CPU, GC, threads, requisições ativas.
- O **teste de carga** mede o sistema inteiro do ponto de vista do cliente: latência, vazão e erros.

Nesta atividade foram usados os quatro, e cada um respondeu uma parte da investigação.

**BenchmarkDotNet.** Executa o método em processo separado, com aquecimento (warmup), várias iterações e uma linha de base, e reporta média, erro (IC 99,9%), desvio-padrão e alocação (`MemoryDiagnoser`). A **variabilidade** importa tanto quanto a média. Um microbenchmark **não** prevê a latência sob carga: ele não vê disputa de lock, fila nem I/O.

**dotnet-counters.** É a primeira triagem: CPU, alocação, coletas de GC, contenção de `Monitor`, número de threads do ThreadPool e requisições ativas. No .NET 10, as métricas do runtime vêm de `System.Diagnostics.Metrics`. Um cuidado prático encontrado aqui: contadores do tipo "sobe e desce" (threads do ThreadPool) chegam ao CSV como **variação por intervalo**, e foi preciso acumulá-los para obter o nível. Counters mostram *sintomas* (por exemplo, CPU baixa e threads crescendo). Sozinhos, não provam a causa.

**dotnet-trace / EventPipe.** Grava eventos e amostras de pilha. O perfil padrão do `dotnet-trace` 10 (`dotnet-sampled-thread-time`) amostra **todas as threads, inclusive as bloqueadas**. Por isso ele mostra onde está o *tempo de parede*, e não só a CPU. Esse é o perfil certo para investigar espera e *starvation*. O relatório `topN` traz o tempo **exclusivo** (no próprio método) e o **inclusivo** (incluindo os métodos chamados).

**Percentis.** O p50 é a experiência típica. O p95 e o p99 são a **cauda**: 1 em cada 20 e 1 em cada 100 requisições. Uma média esconde a cauda. Na linha de base, o POST teve **mediana de 4,7 ms e p99 de 513 ms**, e a média seria de algumas dezenas de milissegundos, o que parece aceitável e não é. Os percentis foram calculados por posição (*nearest-rank*) sobre **todas** as requisições da janela, inclusive erros e timeouts.

**Teste de carga orientado a hipótese.** Para que o resultado seja interpretável, foram fixados antes:

- **Modelo de chegada:** aberto. As requisições saem numa taxa constante, sem esperar as anteriores. Um gerador "fechado", que espera a resposta para mandar a próxima, reduz a taxa justamente quando o servidor fica lento e esconde a cauda (**omissão coordenada**). Por isso a latência é medida a partir do instante **agendado**.
- **Concorrência:** resultado da taxa, e não fixada.
- **Duração:** 10 s de **aquecimento descartado** e 30 s de **janela de medição**.
- **Massa de dados:** banco novo com 20 mil pedidos em cada execução.
- **Critério de parada:** timeout de 5 s por requisição e limite de requisições em voo no próprio gerador.

## 3. Código-fonte, decisões e evidências

### 3.1 Plano de medição (Etapa 1, `plano-medicao.md`, escrito antes das comparações)

- **Rotas medidas:** `POST /pedidos` (criação síncrona) e `GET /pedidos?clienteId=…&tamanho=20` (consulta do painel).
- **Método isolável:** caminho de CPU do POST, ou seja, `ParaDominio`, `Classificar` e `ParaResposta`.
- **Carga:** **300 criações/s + 300 consultas/s**. É o "joelho" encontrado numa sondagem em degraus: a 150/s tudo era rápido; a 300/s a mediana seguia baixa, mas a cauda explodia; a 500/s tudo colapsava.
- **Hipótese inicial:** a cauda vem de **espera por lock no SQLite**, e não de CPU.
- **Metas:**

| Métrica | Meta |
|---|---|
| POST p95 / p99 | ≤ 50 ms / ≤ 100 ms |
| GET p95 / p99 | ≤ 20 ms / ≤ 50 ms |
| Vazão | igual à taxa ofertada |
| Erros e timeouts | 0% |
| Testes funcionais | sem regressão |

### 3.2 Linha de base sob carga (antes)

Duas execuções sem trace (`carga-execucoes.md`):

| Execução | Rota | Vazão OK | Erros | p50 | p95 | p99 | máx |
|---|---|---:|---:|---:|---:|---:|---:|
| antes-1 | POST | 300/s | 0 | 4,7 ms | **302 ms** | **513 ms** | 1.803 ms |
| antes-1 | GET | 300/s | 0 | 3,3 ms | 81 ms | 135 ms | 232 ms |
| antes-2 | POST | 300/s | 0 | 4,3 ms | **303 ms** | **603 ms** | 2.405 ms |
| antes-2 | GET | 300/s | 0 | 2,9 ms | 57 ms | 102 ms | 158 ms |

Nenhuma requisição falhou e a vazão acompanhou a oferta, mas o p95 e o p99 ficaram muito acima das metas. Numa terceira execução, feita **com trace**, o p99 do POST chegou a 753 ms. Ela foi usada só para diagnóstico, porque a coleta do trace perturba o processo.

### 3.3 Counters (Etapa 2, `counters-antes-1.md` e `counters-resumo.md`)

Médias na janela com carga:

| Sinal | Antes |
|---|---|
| CPU (4 núcleos) | **21%**: não é gargalo de CPU |
| Threads do ThreadPool | subiram de **7 para 15 a 21** durante a carga (injeção de threads) |
| Requisições POST ativas | 10 a 11 em média (pela Lei de Little, 300/s × cerca de 35 ms) |
| Contenção de `Monitor` | 5 a 7 por segundo (desprezível) |
| GC gen0 / alocação | 3 a 3,5 por segundo / 40 a 44 MB/s (leve) |
| Exceções | 0 |

O padrão "CPU baixa + o pool criando threads" é a assinatura de **threads bloqueadas**. O ThreadPool injeta threads porque as existentes estão paradas, e não ocupadas. A coluna de fila do ThreadPool gerou níveis negativos quando acumulada. O dado não é confiável nesse formato e foi descartado da conclusão.

### 3.4 Trace (Etapa 3, `trace-antes-topN.txt`)

10 s de coleta no meio da janela de medição, com o perfil *thread-time*.

| Método | Exclusivo | Inclusivo |
|---|---:|---:|
| `Thread.Sleep(int32)` | **42,98%** | 42,98% |
| `LowLevelLifoSemaphore.WaitForSignal` (thread do pool ociosa) | 19,29% | — |
| `WaitHandle.WaitOneNoCheck` / `Monitor.Wait` (esperas ociosas) | 12,8% / 9,6% | — |
| `SqliteDataReader.NextResult()` | 1,1% | **44,35%** |

A cadeia inclusiva liga o sintoma ao nosso código:

`CriarPedidoAsync` (44,5%) → `RepositorioPedidos.AdicionarAsync` (44,5%) → `DbContext.SaveChangesAsync` → `BatchExecutor.ExecuteAsync` → `SqliteDataReader.NextResult` (44,35%) → **`Thread.Sleep`** (42,98%)

Ou seja, uma chamada **assíncrona** (`SaveChangesAsync`) termina num **`Sleep` síncrono** dentro do provedor. É o laço de nova tentativa do `Microsoft.Data.Sqlite` quando o banco responde `SQLITE_BUSY`. A distribuição das latências do POST confirma isso de forma independente (`histograma-latencia.txt`): há picos em **150–199, 300–349, 450–499 e 600–649 ms**, múltiplos de cerca de 150 ms, ou seja, 1, 2, 3 e 4 tentativas com espera fixa.

**Explicações descartadas pelos dados:**

- **CPU do nosso código.** O microbenchmark mede **1,26 µs** para validar, montar o domínio, classificar e montar a resposta, e 1,5 KB alocados (`benchmark-criacao.md`). Isso está cinco ordens de grandeza abaixo dos 300 ms observados.
- **GC.** Gen0 de 3 a 3,5 por segundo, e o GC não aparece no topo do trace.
- **Locks .NET.** Contenção de `Monitor` desprezível.
- **Consulta lenta.** Nenhum método de `ConsultasPedidos` aparece no top 60 inclusivo. Quem dorme é a gravação.

### 3.5 Intervenções e comparação (Etapa 4)

**Intervenção 1, configuração: SQLite em modo WAL.** Hipótese específica: a disputa seria entre **leitor e escritor**. No modo de journal padrão, uma leitura impede o commit e vice-versa; no WAL, não. Foi uma linha (`PRAGMA journal_mode=WAL`), mantendo `synchronous=FULL` para não alterar a durabilidade. O WAL foi confirmado ativo (arquivos `-wal`/`-shm`; `PRAGMA journal_mode` → `wal`).

| Execução (WAL) | POST p95 / p99 | GET p95 / p99 |
|---|---|---|
| depois-1 | 303 / 603 ms | 63 / 128 ms |
| depois-2 | 305 / 517 ms | 97 / 331 ms |

**Sem efeito: hipótese específica refutada.** Isso é coerente com o trace, em que as consultas nunca apareciam dormindo. O SQLite continua aceitando **um escritor por vez** mesmo em WAL. Logo, a disputa dominante era **escritor × escritor**: POSTs simultâneos (10 a 11 em média) tentando gravar juntos.

**Intervenção 2, limite: serializar as escritas no processo.** O `POST /pedidos` passou a gravar através do mesmo `LimitadorBanco` (`SemaphoreSlim` com limite 1) já usado pelos consumidores da fila (Atividade 4). Os escritores passam a esperar **de forma assíncrona e em ordem**, sem chegar a colidir no SQLite. A mudança ocupa poucas linhas no handler e é configurável por `Banco:SerializarEscritasHttp`.

| Execução | Config. | POST p50 / p95 / p99 / máx | GET p50 / p95 / p99 | Erros |
|---|---|---|---|---:|
| limitador-sem-wal-1 | limitador | 2,6 / **5,9** / **7,6** / 28 ms | 1,6 / 4,9 / 5,8 ms | 0 |
| depois-final | limitador | 2,4 / **5,8** / **7,5** / 19 ms | 1,5 / 4,8 / 5,8 ms | 0 |
| limitador-wal-1 | limitador + WAL | 2,7 / 6,3 / 25,0 / 89 ms | 1,7 / 5,0 / 5,9 ms | 0 |
| limitador-wal-2 | limitador + WAL | 2,6 / 5,9 / 8,1 / 44 ms | 1,6 / 4,9 / 5,7 ms | 0 |

Resumo antes × depois, com 2 execuções de cada:

| Métrica | Antes | Depois | Meta |
|---|---|---|---|
| POST p95 | 302–303 ms | **5,8–5,9 ms** | ≤ 50 ✔ |
| POST p99 | 513–603 ms | **7,5–7,6 ms** | ≤ 100 ✔ |
| GET p95 | 57–81 ms | **4,8–4,9 ms** | ≤ 20 ✔ |
| GET p99 | 102–135 ms | **5,8 ms** | ≤ 50 ✔ |
| Vazão | 300 + 300/s | 300 + 300/s | ✔ |
| Erros e timeouts | 0 | 0 | ✔ |
| Testes funcionais | 158 ✔ | 159 ✔ (+1 teste: 40 POSTs simultâneos nunca escrevem em paralelo) | ✔ |

**Novo consumo de recursos** (`counters-resumo.md`):

- **Threads do pool:** máximo de **2 a 4** (antes, 15 a 17). A injeção de threads acabou.
- **POSTs ativos:** 0,5 a 0,9 em média (antes, 10 a 11). Pela Lei de Little, 300/s × 2,6 ms ≈ 0,8.
- **CPU:** 23% a 24% (antes, 21%).
- **Custo novo:** alocação de 52 MB/s (antes, cerca de 42) e GC gen0 de 9 a 10 por segundo (antes, 3 a 3,5). É um aumento de cerca de 20% em alocação, ainda leve. Não investiguei a causa. O semáforo sem disputa custa **53 ns e não aloca** (benchmark), então a suspeita é a espera com disputa, que cria tarefas, ou a própria vazão maior de itens no pool. Fica registrado como pendência.

**Trace depois** (`trace-depois-final-trace-topN.txt`): `Thread.Sleep` **saiu do top 20**, e `SqliteDataReader.NextResult` caiu de 44,35% para **2,13% inclusivo**. O topo agora é só de threads ociosas esperando trabalho (`LowLevelLifoSemaphore` 35%, `WaitOne` 22%, `Monitor.Wait` 16%). Mesmo com o trace ligado, o POST ficou com p99 de 8,6 ms.

**Decisões:**

- **Limitador: mantido.** Atingiu todas as metas, sem regressão funcional e com CPU praticamente igual.
- **WAL: desligado por padrão** (`Banco:ModoWal=false`, mantido como opção). Não trouxe ganho mensurável nem sozinho nem combinado. Pela regra do plano, uma mudança só fica se melhorar a meta. Em WAL, o p99 do POST também variou mais (8 a 25 ms) do que sem WAL (7,5 a 7,6 ms), mas com só duas execuções isso não é conclusivo.

**Situação da hipótese:**

- **Geral** (espera por lock no SQLite, e não CPU): **confirmada** pelos counters, pelo trace, pela distribuição das latências e pelo benchmark.
- **Variante leitor × escritor:** **refutada** pela intervenção 1.
- **Variante escritor × escritor:** **confirmada** pela intervenção 2 e pelo trace posterior.

**Incertezas registradas:**

- O gerador e a API rodaram na mesma VM. Em parte do tempo, os dois disputam CPU.
- O valor de cerca de 150 ms por tentativa foi **inferido** da distribuição e do trace, não lido no código-fonte do provedor.
- Os resultados são do SQLite. Num banco com servidor, o mecanismo de espera seria outro, embora o princípio de não deixar escritores colidirem continue valendo.
- Foram feitas duas execuções por configuração: suficiente para separar efeitos grandes (de 300 ms para 6 ms), mas não diferenças de poucos milissegundos.

### 3.6 Extensão: carga em degraus para localizar a saturação

Consulta fixa em 300/s, aumentando a criação (`carga-degraus.md`):

| Criação/s | Antes: POST p99 | Depois: POST p99 | Depois: GET p99 |
|---:|---|---|---|
| 300 | 513–603 ms | 7,5 ms | 5,8 ms |
| 400 | **3.608 ms (colapso)** | — | — |
| 450 | 4.406 ms, 4 timeouts | 8,9 ms | 5,9 ms |
| 600 | — | 23,4 ms | 6,0 ms |
| 800 | — | **2.963 ms (colapso)** | 1.685 ms |
| 1.000 | — | gerador saturado (5.968 não enviados) | — |

A saturação passou de **cerca de 400/s para entre 600 e 800/s**, aproximadamente o dobro. O novo teto é o próprio SQLite: um escritor e um `fsync` por transação.

O degrau de 800/s revelou um **novo risco**. Acima da capacidade, os POSTs ficam acumulados na espera do semáforo **sem limite**: a rota síncrona não tem backpressure. A solução já existe no projeto, que é a rota `POST /pedidos/fila` (Atividade 4), com capacidade limitada e 503. A outra opção seria limitar o tempo de espera no semáforo e responder 503.

## 4. Testes e validações obrigatórias

| Validação exigida | Como foi atendida |
|---|---|
| Ferramentas executadas contra build Release no ambiente registrado | `carga/iniciar-api.sh` compila e executa `bin/Release`, ambiente Production; o BenchmarkDotNet roda em Release com `--no-build` |
| Aquecimento e janela de medição separados | Gerador com fase `aquecimento` (10 s, descartada) e `medicao` (30 s); cada linha do CSV bruto traz a fase |
| p95/p99 calculados sobre amostra identificada | 9.000 requisições por rota por execução, identificadas por rota, fase, instante agendado, latência e status; *nearest-rank*; histogramas em `histograma-latencia.txt` |
| Erros e timeouts registrados, não só sucessos | Colunas "Erros HTTP", "Timeouts" e "Outros" (não enviados pelo gerador); timeouts entram no percentil com o valor do timeout. Evidência: 4 timeouts no degrau antes/450 e 5.968 não enviados no degrau depois/1.000 |
| Intervenção mantida só se melhora a meta sem regressão | WAL **revertido** (sem ganho); limitador **mantido** (metas atingidas); `dotnet test` com 159 testes verdes, 3 execuções seguidas |

### Aprimoramentos

1. **Carga em degraus para localizar a saturação** (extensão do roteiro): seção 3.6, antes e depois.
2. **Gerador de carga próprio em modelo aberto**, com correção de omissão coordenada e execução totalmente reproduzível por script (API nova, banco novo, counters e trace).
3. *(Não realizados)* Comparação com o Visual Studio Profiler (o ambiente usado foi Linux, sem VS; fica para execução local no Windows) e orçamento de performance periódico.

## 5. Referências

> Mantenha somente as fontes que você realmente consultou.

MICROSOFT. **dotnet-counters diagnostic tool**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/dotnet/core/diagnostics/dotnet-counters. Acesso em: 12 nov. 2026.

MICROSOFT. **dotnet-trace performance analysis utility**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/dotnet/core/diagnostics/dotnet-trace. Acesso em: 12 nov. 2026.

MICROSOFT. **Built-in metrics: System.Runtime**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/dotnet/core/diagnostics/built-in-metrics-runtime. Acesso em: 12 nov. 2026.

MICROSOFT. **Debug ThreadPool starvation**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/dotnet/core/diagnostics/debug-threadpool-starvation. Acesso em: 12 nov. 2026.

MICROSOFT. **Microsoft.Data.Sqlite: database errors**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/dotnet/standard/data/sqlite/database-errors. Acesso em: 12 nov. 2026.

SQLITE. **Write-Ahead Logging**. 2025. Disponível em: https://www.sqlite.org/wal.html. Acesso em: 12 nov. 2026.

BENCHMARKDOTNET. **Overview**. 2025. Disponível em: https://benchmarkdotnet.org/articles/overview.html. Acesso em: 12 nov. 2026.

TENE, G. **How NOT to measure latency**. Palestra, Strange Loop, 2015. (Conceito de omissão coordenada.)
