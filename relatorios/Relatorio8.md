# PROGRAMAÇÃO APLICADA EM .NET — RELATÓRIO DE AULAS PRÁTICAS

**Nome:** ______________________ **RA:** ______________ **Polo:** ______________
**Local da realização da aula prática:** ______________ **Ano da postagem:** 2026

---

## Relatório 8 — WebApplicationFactory, bancos efêmeros, analisadores e revisão automatizada

**Unidade 8 – Qualidade contínua**

**Objetivo:** criar uma rede de segurança executável para contratos, persistência, concorrência e estilo, integrada ao fluxo de revisão.

## 1. Entrega e ambiente

- **Repositório:** `HubPedidos/`. Itens novos desta atividade:

| Onde | O quê |
|---|---|
| `.editorconfig` | política de analisadores e estilo |
| `Directory.Build.props` | análise ligada no build |
| `revisao.sh` | revisão local, igual à do CI |
| `testes-repetidos.sh` | suíte N vezes em ordem aleatória |
| `.github/workflows/revisao.yml` (raiz do repositório) | CI |
| `tests/.../HubApiFactory.cs` | fábrica de testes revisada |
| `tests/.../OrdemAleatoria.cs` | ordenadores aleatórios do xUnit |
| `tests/.../Contratos/` | *snapshot* do contrato OpenAPI |
| `docs/testes/estrategia.md` | estratégia de testes e diferenças do banco efêmero |

- **Reproduzir:** `./revisao.sh` (a mesma revisão do CI) e `./testes-repetidos.sh 10`.

| Item | Versão |
|---|---|
| SDK / runtime | .NET SDK 10.0.114 / .NET 10.0.14 |
| Testes | xUnit 2.9.3, Microsoft.AspNetCore.Mvc.Testing 10.0.14, coverlet.collector 6.0.4 |
| Análise | analisadores do SDK 10.0.114 (`AnalysisLevel=latest-recommended`), `dotnet format` do SDK |
| CI | GitHub Actions `ubuntu-latest`, `actions/setup-dotnet@v4` com `global.json` |
| IDE | Visual Studio 2026 versão ______ *(preencher)* |

## 2. Resumo teórico aplicado à atividade

**Teste de integração.** Executa a aplicação **real**: o mesmo `Program`, pipeline, DI, middlewares e serializador. A infraestrutura é trocada de forma **explícita** e controlada (banco, relógio, serviços externos). Ele pega o que o teste de unidade não vê: ordem de middleware, registro no contêiner, serialização, rotas, autorização.

**`WebApplicationFactory<TEntryPoint>`.** Sobe o host da aplicação em memória (`TestServer`) ou, no .NET 10, também em **Kestrel real** (`UseKestrel`, usado nos testes de reconexão da Atividade 7). O `CreateClient` fornece um `HttpClient` ligado a esse host. A configuração é ajustada por ambiente (`UseEnvironment`, `UseSetting`) e os serviços são trocados com `ConfigureTestServices`. `WithWebHostBuilder` cria variações a partir de uma fábrica base.

**Banco efêmero.** É criado para o teste e destruído no fim. Garante **isolamento** (nenhum teste vê dados de outro) e **repetibilidade**. O ponto delicado é a **fidelidade**: um banco efêmero de outro tipo, ou em outro modo, pode se comportar diferente do de produção. Passar no banco efêmero não é o mesmo que ser equivalente à produção.

**Portfólio de testes.** A ideia da pirâmide é ter muitos testes de unidade (rápidos e estáveis), uma camada de integração nos pontos de contato, testes de contrato nas fronteiras públicas, testes de carga separados e **poucos** testes ponta a ponta, porque são lentos e mais instáveis. O objetivo é confiança por custo, e não uma porcentagem de cobertura.

**Analisadores Roslyn.** Rodam durante a compilação e emitem diagnósticos de três tipos:

- **estilo** (`IDE*`): chaves, namespaces, `using`s;
- **qualidade** (`CA*`): uso correto de APIs, desempenho, cultura, `IDisposable`;
- **segurança** (`CA2100`, `CA3075`, `CA53xx`): SQL montado com entrada do usuário, XML inseguro, criptografia fraca.

O `.editorconfig` define a **severidade** de cada regra (`none`, `suggestion`, `warning`, `error`) e pode variar por pasta. Só o que é `error` (ou `warning` com `TreatWarningsAsErrors`) quebra o build.

**Revisão automatizada.** É uma verificação **reproduzível** que roda igual na máquina do desenvolvedor e no CI: restore, build, `dotnet format --verify-no-changes`, testes, artefatos de resultado e um **diagnóstico de falha** legível. Ela deve **falhar cedo**, dizer **qual etapa** falhou e por quê, e **impedir a publicação** quando falha.

## 3. Código-fonte, decisões e evidências

### 3.1 Fábrica customizada (Etapa 1)

A `HubApiFactory` (criada na Atividade 3 e revisada aqui) é a aplicação real com a infraestrutura trocada de forma explícita:

| Requisito | Como foi atendido |
|---|---|
| Ambiente de teste isolado | `UseEnvironment("Testing")`: não carrega o `appsettings.Development` (que semearia 20 mil pedidos) e gera uma chave JWT aleatória por execução |
| Impedir uso do banco de desenvolvimento | `Banco:ExigirEmMemoria=true`: com um connection string de arquivo, a **inicialização falha** com `InvalidOperationException: Banco:ExigirEmMemoria está ligado, mas ConnectionStrings:Hub não é um banco em memória ('Data Source=hubpedidos.db')` (teste `Banco_de_arquivo_e_recusado_no_ambiente_de_teste`) |
| Substituir serviços externos | `ConfigureTestServices`: um interceptor que conta os comandos SQL (atividades 3 e 7) e o **`CanalCaptura`**, um canal de notificação de teste que registra cada evento e permite **aguardar** um evento específico com prazo |
| Cliente HTTP definido | `ClientOptions.AllowAutoRedirect = false` e `HandleCookies = false`. A autenticação é sempre **explícita** (`ClienteAutenticado(cliente, operador)` com Bearer); sem token, a rota protegida responde 401 (testado) |

### 3.2 Banco efêmero (Etapa 2)

- SQLite **em memória**, com nome aleatório por fábrica (`Data Source=hub-<guid>;Mode=Memory;Cache=Shared`) ou `:memory:` por teste de persistência.
- **Conexão-âncora aberta** durante toda a vida da fábrica, porque no SQLite o banco em memória some quando a última conexão fecha.
- Esquema criado pelas **migrations reais** (`MigrateAsync`). O teste também valida as migrations (`GetPendingMigrations` vazio, `HasPendingModelChanges` falso).
- Dados mínimos criados pelo próprio teste.
- Teste `Cada_fabrica_tem_banco_proprio_e_vazio_com_esquema_migrado`: um pedido criado na fábrica A não aparece na B.

**Diferenças de fidelidade que ainda exigem testes específicos** (detalhes em `docs/testes/estrategia.md`):

| Aspecto | Em memória (testes) | Arquivo (produção) |
|---|---|---|
| Durabilidade e tempo | sem `fsync`, muito mais rápido | `fsync` por transação: o gargalo da Atividade 5 **só aparece aqui** |
| Concorrência de escrita | cache compartilhado com lock **por tabela** | lock do **arquivo** inteiro, `SQLITE_BUSY` e espera de cerca de 150 ms |
| WAL | não se aplica | disponível |

Se a produção migrasse para SQL Server ou PostgreSQL, também mudariam a colação (`CI_AS` ignora maiúsculas), os tipos (`decimal` nativo), as funções JSON e o limite de tamanho de texto, que o SQLite ignora.

Por isso o portfólio mantém os testes de **carga e contenção** (Atividade 5) fora da suíte, rodando contra o arquivo real.

### 3.3 Contratos críticos cobertos (Etapa 3)

| Contrato | Teste(s) de integração |
|---|---|
| Criação e consulta com persistência real do ambiente de teste | `Pedido_criado_e_persistido_e_consultado_pelo_detalhe`, `Fluxo_completo_classifica_e_seleciona_processador` (×5) |
| v1/v2 | `Cliente_v1_continua_funcionando...` (cliente congelado e estrito), `Contrato_v1_e_identico_ao_publicado`, `V2_tem_a_mudanca_incompativel...` |
| ETag/304 | `Etag_e_estavel_quando_nada_muda_e_304_vem_sem_corpo` (v1 e v2), `If_match_atual_permite_e_obsoleto_da_412_sem_gravar` |
| Autorização e validação | `V2_exige_identidade_e_esconde_pedido_alheio`, `Cliente_so_assina_e_recebe_os_proprios_pedidos`, `Dto_invalido_retorna_400_com_erros_por_campo` |
| Produtor da fila com consumidor observável, **sem atraso arbitrário** | `Fluxo_assincrono_da_fila_e_observado_sem_polling`: `POST /pedidos/fila` → `CanalCaptura.AguardarAsync(e => e.PedidoId == id && e.Status == Concluido)` → o detalhe HTTP já mostra `Concluido`. Os dois testes da fila que antes consultavam em laço também passaram a usar o canal |

**Ordem aleatória e repetição.** Implementei ordenadores do xUnit (`ITestCaseOrderer` e `ITestCollectionOrderer`) que embaralham os testes dentro de cada classe e as coleções entre si. A **semente** é impressa em toda execução (`[ordem-aleatoria] semente=… (reproduza com HUB_SEMENTE_TESTES=…)`) e pode ser fixada para reproduzir uma falha. Ela é combinada com um hash estável (FNV-1a), porque o `string.GetHashCode` muda a cada processo. O script `./testes-repetidos.sh 10` rodou a suíte **10 vezes com sementes diferentes: 10/10 aprovadas** (`testes-repetidos.txt`). Os testes entre classes já rodam em paralelo; os que medem memória do processo inteiro ficam numa coleção sem paralelismo (Atividade 3).

### 3.4 Analisadores e revisão automatizada (Etapa 4)

**Política** (`Directory.Build.props` e `.editorconfig`):

- `AnalysisLevel=latest-recommended` e `EnforceCodeStyleInBuild=true`.
- Avisos do **compilador** continuam bloqueando (`TreatWarningsAsErrors`).
- Avisos de **analisador** ficam visíveis, mas não bloqueiam (`CodeAnalysisTreatWarningsAsErrors=false`).
- **Só as regras aceitas pela equipe** são elevadas a `error`.

Antes de decidir, fiz um levantamento com o conjunto recomendado: cerca de 150 ocorrências, 143 delas de nomenclatura nos testes.

| Regra | Severidade | Justificativa |
|---|---|---|
| CA2016 (repassar `CancellationToken`), CA2012 (`ValueTask`), CA1068 (token por último) | **error** | correção assíncrona: sem repassar o token, o trabalho continua depois que o cliente desistiu |
| CA1001 (campo `IDisposable`), CA1854 (dupla busca em dicionário), CA1873 (argumento de log caro) | **error** | vazamento de recurso, corrida em código concorrente e custo no caminho quente do consumidor |
| CA1305, CA1309, CA1310 (cultura) | **error** | bug real já visto no projeto: `"3,000,000.07"` × `"3.000.000,07"` |
| CA2100, CA3075, CA5350, CA5351, CA2326, CA5369 (segurança) | **error** | SQL montado com entrada do usuário, XXE, criptografia fraca/quebrada, desserialização insegura |
| IDE0161, IDE0011, IDE0005, IDE0040, CA1852 (só em `src/` e `clientes/`) | **error** | estrutura uniforme no código de produção |
| CA1000 (estático em tipo genérico) | suggestion | `Resultado<T>.Ok/Falha` é o padrão idiomático do tipo |
| CA1707 (sublinhado em nomes), CA1307 em testes e benchmarks | none / suggestion | nomes de teste descritivos (`Metodo_faz_x`) e nomes que aparecem nas tabelas do BenchmarkDotNet |
| CA1014 (CLSCompliant) | none | interoperabilidade CLS não é requisito |
| Migrations do EF | `generated_code = true` | código gerado não se edita à mão |

**Correções feitas** (todas no código, nenhuma silenciada sem motivo):

- cultura invariante no gerador de carga;
- `LimitadorBanco` agora é `IDisposable`, e o contêiner libera o `SemaphoreSlim` no encerramento;
- parâmetro renomeado para casar com a interface `IRetryPolicy` (CA1725);
- `StringComparison` explícito onde faltava;
- o argumento de log caro passou para uma variável local, dentro de `IsEnabled`. O analisador não reconhece a proteção quando o método de log é gerado por `[LoggerMessage]`, e isso foi documentado;
- `dotnet format` ordenou `using`s, removeu um que não era usado e corrigiu indentação.

**Única supressão local:** o *check-then-act* do experimento da Atividade 4 é **errado de propósito** (é o que a demonstração mede). Ele recebeu `#pragma warning disable CA1854` com o motivo escrito ao lado.

**Script de revisão** (`revisao.sh`, o mesmo no CI):

1. restore;
2. build Release com analisadores;
3. `dotnet format --verify-no-changes`;
4. testes em ordem aleatória, com TRX e cobertura (`XPlat Code Coverage`);
5. resumo em `artifacts/resumo.md` (e no *job summary* do GitHub).

Execução aprovada (`revisao-aprovada.txt`):

```
══════ [2/5] Build Release com analisadores …    ok (avisos não bloqueantes: 0)
══════ [3/5] Formatação e estilo …               ok
══════ [4/5] Testes (ordem aleatória, TRX e cobertura)
HubPedidos.Tests: [ordem-aleatoria] semente=… 
Passed!  - Failed: 0, Passed: 214, Total: 214
| Testes | Aprovados | Falhas | Cobertura de linhas (informativa) | Duração |
| 214 | 214 | 0 | 90.8% | 43 s |
✔ REVISÃO APROVADA
```

**Falhas provocadas para confirmar o bloqueio:**

1. **Analisador** (`falha-analisador.txt`). Uma consulta deixou de repassar o token (`FirstOrDefaultAsync()`). A revisão parou **na etapa 2**, antes de rodar os testes:
   ```
   src/HubPedidos.Infrastructure/Persistencia/ConsultasPedidos.cs(104,9): error CA2016: Forward the 'cancellationToken' parameter to the 'FirstOrDefaultAsync' method…
   ✖ REVISÃO REPROVADA na etapa: build/analisadores — 1 erro(s) distinto(s); detalhes em artifacts/build.log
   código de saída: 1
   ```
   Na primeira versão, o script mostrava a mensagem cortada (o `sed` removia demais) e contava cada erro duas vezes, porque o MSBuild repete as mensagens. Corrigi as duas coisas para o diagnóstico ficar legível.
2. **Teste** (`falha-teste.txt`). Uma regressão de regra (limite de alto valor de 5.000 para 5.001) passou pelo build e pela formatação, mas **a etapa 4 reprovou** e o resumo mostrou o caso quebrado:
   ```
   - `ClassificadorTests.Classifica_cada_classe_de_equivalencia(caso: "5.000 (limite alto valor)", …)` — esperado AltoValor, obtido Padrao.
   ✖ REVISÃO REPROVADA na etapa: testes        código de saída: 1
   ```

**CI** (`.github/workflows/revisao.yml`):

- dispara em `push`, `pull_request` e manualmente;
- usa `global.json` para fixar o SDK;
- roda `./revisao.sh`;
- **sempre** publica os artefatos (resumo, logs, TRX e cobertura), inclusive quando falha;
- o job **`publicar`** (`dotnet publish` da API) declara `needs: revisao`, então **não roda se a revisão falhar**.

O workflow foi enviado ao GitHub e disparou a execução #1 em 14/11/2026. Resultado: **______** *(ver a seção "Resultado no GitHub Actions" abaixo)*. Para que um *pull request* não possa ser integrado com a revisão vermelha, falta marcar o check "Revisão automatizada" como obrigatório na proteção da branch principal, nas configurações do repositório.

## 4. Testes e validações obrigatórias

Resultado local: **214 casos, 214 aprovados** (140 métodos). Desde a Atividade 7, 6 novos: 4 da fábrica e 2 de contrato.

| Validação exigida | Evidência |
|---|---|
| Cada teste inicia com estado conhecido e não depende da ordem | banco por fábrica ou por teste com nome aleatório; ordem aleatória com semente; **10/10 execuções** com sementes diferentes (`testes-repetidos.txt`) |
| Banco efêmero preserva a conexão e é descartado ao final | conexão-âncora aberta durante a vida da fábrica e liberada no `Dispose`; `Cada_fabrica_tem_banco_proprio_e_vazio...`; `Banco_de_arquivo_e_recusado...` |
| Contrato v1 e resposta condicional protegidos por integração | `Contrato_v1_e_identico_ao_publicado`, `Cliente_v1_continua_funcionando...`, testes de ETag/304/412 e o **snapshot do OpenAPI** v1/v2 |
| Uma violação de analisador falha o build conforme a política | CA2016 provocado → build reprovado na etapa 2 (`falha-analisador.txt`) |
| O pipeline falha e impede a publicação quando build, testes ou análise falham | código de saída 1 nas duas falhas provocadas; `publicar` depende de `revisao` no workflow |

### Aprimoramentos (extensões do roteiro)

1. **Teste de contrato por snapshot com revisão explícita** (`ContratoSnapshotTests`). O OpenAPI v1/v2 gerado é comparado com `Contratos/Snapshots/openapi-v{n}.json`, versionado. Uma diferença falha o teste e grava `*.recebido.json` com o comando para revisar (`git diff --no-index …`). A atualização só acontece com aceite explícito (`ATUALIZAR_SNAPSHOTS=1`). Sem snapshot, o teste falha em vez de criar um em silêncio (testado).
2. **Cobertura e resultados publicados como artefatos**, sem usar cobertura como meta. O TRX, o `coverage.cobertura.xml`, os logs e o resumo sobem sempre. A cobertura (90,8% de linhas) aparece como **informação**: os testes foram escritos para comportamentos e contratos, e não para atingir um número.
3. **Testes em paralelo sem estado compartilhado.** As coleções já rodam em paralelo. Esta atividade eliminou o último estado implícito: o banco por fábrica com nome aleatório e o canal de captura por fábrica. A ordem aleatória confirmou isso.

## 5. Resultado no GitHub Actions

*(preenchido após a execução — ver resposta final)*

## 6. Referências

> Mantenha somente as fontes que você realmente consultou.

MICROSOFT. **Integration tests in ASP.NET Core**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/aspnet/core/test/integration-tests. Acesso em: 14 nov. 2026.

MICROSOFT. **Testing against your production database system / Choosing a testing strategy (EF Core)**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/ef/core/testing/choosing-a-testing-strategy. Acesso em: 14 nov. 2026.

MICROSOFT. **SQLite in-memory databases (Microsoft.Data.Sqlite)**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/dotnet/standard/data/sqlite/in-memory-databases. Acesso em: 14 nov. 2026.

MICROSOFT. **Code analysis in .NET: configuration and rule severity**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/dotnet/fundamentals/code-analysis/configuration-options. Acesso em: 14 nov. 2026.

MICROSOFT. **dotnet format**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/dotnet/core/tools/dotnet-format. Acesso em: 14 nov. 2026.

MICROSOFT. **Use code coverage for unit testing (coverlet)**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/dotnet/core/testing/unit-testing-code-coverage. Acesso em: 14 nov. 2026.

GITHUB. **Building and testing .NET**. GitHub Docs, 2025. Disponível em: https://docs.github.com/actions/use-cases-and-examples/building-and-testing/building-and-testing-net. Acesso em: 14 nov. 2026.

FOWLER, M. **The Practical Test Pyramid** (VOCKE, H.). martinfowler.com, 2018. Disponível em: https://martinfowler.com/articles/practical-test-pyramid.html. Acesso em: 14 nov. 2026.
