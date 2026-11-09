# PROGRAMAÇÃO APLICADA EM .NET — RELATÓRIO DE AULAS PRÁTICAS

**Nome:** ______________________ **RA:** ______________ **Polo:** ______________
**Local da realização da aula prática:** ______________ **Ano da postagem:** 2026

---

## Relatório 3 — Consultas previsíveis, projeções, tracking, consultas compiladas e JSON

**Unidade 3 – LINQ/EF Core avançado e mapeamentos**

**Objetivo:** persistir e consultar pedidos com EF Core, analisando SQL, roundtrips, tracking e impacto dos mapeamentos adotados.

## 1. Entrega e ambiente

- **Repositório:** `HubPedidos/`. Novo projeto `src/HubPedidos.Infrastructure`, com DbContext, migrations, repositório e consultas. A Application ganhou só **interfaces** (`IRepositorioPedidos`, `IConsultasPedidos`) e DTOs de leitura, e continua sem referência ao EF Core (teste arquitetural).
- **Reproduzir:**
  - `dotnet tool restore` e `dotnet test` (135 testes);
  - `dotnet run --project src/HubPedidos.Api --launch-profile http`: em Development, aplica as migrations e semeia 20.000 pedidos;
  - `dotnet run -c Release --project benchmarks/HubPedidos.Benchmarks -- --filter "*ConsultasBenchmarks*"`.

| Item | Versão |
|---|---|
| SDK / runtime | .NET SDK 10.0.114 / .NET 10.0.14 |
| EF Core | Microsoft.EntityFrameworkCore.Sqlite 10.0.14, Design 10.0.14, `dotnet-ef` 10.0.14 (ferramenta local) |
| Banco | SQLite 3.53.3 (SQLitePCLRaw.bundle_e_sqlite3 2.1.13), arquivo local, sem servidor |
| Massa | `GeradorMassa`, semente 123: **20.000 pedidos e 59.750 itens**, datas ao longo de 300 dias, 6 regiões, 4 status |
| Benchmark | BenchmarkDotNet 0.15.8, Release, Warmup=3, Iterations=15; SQLite **em memória** com a mesma massa |
| SO / CPU | Ubuntu 24.04.5 LTS; Xeon 2,30 GHz, 4 núcleos (VM) *(trocar pelo seu ambiente)* |
| IDE | Visual Studio 2026 versão ______ *(preencher)* |

**Por que SQLite:** não exige servidor nem credencial, então o projeto roda igual em qualquer máquina. Os testes usam o mesmo provedor em memória. As limitações que isso impõe estão registradas na seção 3.5.

## 2. Resumo teórico aplicado à atividade

**Tradução LINQ → SQL e avaliação no cliente.** O EF converte a árvore de expressão do LINQ em SQL. Só a **projeção final** pode ter partes avaliadas no cliente, e o EF ainda tenta traduzir o máximo possível. Dois exemplos reais deste projeto mostram isso:

1. `p.Regiao.ToString()` dentro do `Select` **não** rodou no cliente. O EF 10 gerou `CASE "p"."Regiao" WHEN 1 THEN 'Sul' ...` no SQL (ver `sql-endpoints.txt`).
2. `p.Id.ToString()` virou `CAST("p"."Id" AS TEXT)` e devolveu o GUID em **maiúsculas**, que é como o SQLite o guarda. A resposta ficou diferente da do `POST` (em minúsculas). A correção foi projetar o `Guid` e deixar o `System.Text.Json` formatar.

Conclusão: um `ToString()` na projeção pode ir para o banco, e o comportamento muda conforme o provedor.

**Projeções, paginação e N+1.** Projetar direto para o DTO faz o SQL trazer apenas as colunas usadas. A listagem não traz `Metadados`, `Entrega_*` nem `Prioridade`, e conta os itens com uma subconsulta `COUNT(*)` sem carregá-los. A paginação exige **ordem determinística**. Por isso há um desempate por `Id` depois de `CriadoEmUtc`: sem ele, dois pedidos no mesmo instante poderiam trocar de página entre execuções. **N+1** é buscar a lista (1 comando) e depois um comando por item da lista (N comandos). Uma projeção com coleção aninhada resolve isso num único comando com `LEFT JOIN`. `Include` também carregaria os itens, mas traria todas as colunas das entidades.

**Tracking, AsNoTracking e resolução de identidade.** Com tracking, o `DbContext` guarda cada entidade materializada e um "instantâneo" dos valores originais. No `SaveChanges`, compara os dois e gera `UPDATE` só das colunas alteradas. Ele também resolve identidade: a mesma linha lida duas vezes vira a mesma instância. `AsNoTracking` evita esse custo em leituras, mas a mesma linha pode virar duas instâncias (`AsNoTrackingWithIdentityResolution` resolve isso sem rastrear). Projeções para DTO nunca são rastreadas. Por isso o `AsNoTracking` na listagem é redundante, e mantive-o só para deixar a intenção explícita. Regra adotada: **atualização pelo mesmo contexto usa tracking**.

**Cache de consultas e consultas compiladas.** O EF guarda em cache a tradução de cada *forma* de consulta, cuja chave é a árvore de expressão sem os valores dos parâmetros. Mesmo assim, a cada execução ele precisa calcular essa chave e procurá-la no cache. `EF.CompileAsyncQuery` pula essa etapa e devolve um delegate pronto. Restrições: o delegate só aceita parâmetros escalares e fica preso ao **primeiro modelo** com que foi executado (comprovado por teste, seção 3.4).

**SQL, parâmetros, comandos e índices.** Valores vindos de variáveis viram **parâmetros** (`@clienteId`), e não literais. Isso mantém o mesmo texto SQL para valores diferentes, reaproveita o plano de execução e impede injeção de SQL. Constantes escritas direto no LINQ viram literais: no experimento inicial com o provedor, `Where(p => p.Meta.Canal == "web")` gerou `= 'web'` no SQL. Os testes do repositório usam variável e geram `= @canal`. A quantidade de **comandos** por operação foi medida com um `DbCommandInterceptor` (`ContadorComandos`). A ligação entre consulta e índice foi verificada com `EXPLAIN QUERY PLAN`.

**Tipos complexos e JSON.** O EF 10 mapeia *complex types* de duas formas: em colunas da própria tabela (`Entrega_Cidade`, ...) ou num documento JSON (`ToJson`). O suporte depende do provedor e da versão. Antes de adotar JSON, testei no SQLite com EF 10.0.14:

- filtro, que vira `"Metadados" ->> 'Canal'`;
- projeção campo a campo;
- `ExecuteUpdate`, que vira `json_set(...)`;
- coleção dentro do JSON, que vira `json_each`.

## 3. Código-fonte, decisões e evidências

### 3.1 Modelo, configuração e migration

O domínio continua imutável e sem EF. A persistência usa classes próprias (`PedidoRegistro`, `ItemPedidoRegistro`, `EnderecoRegistro`, `MetadadosRegistro`), convertidas por `MapeadorRegistro`. O domínio ganhou `StatusPedido` (máquina de estados com padrão de tupla), `Endereco` e `MetadadosPedido`.

Decisões revisadas no script da migration (`docs/evidencias/atividade3/migration-inicial.sql`) **antes** de aplicá-la:

| Decisão | Motivo |
|---|---|
| Dinheiro em **centavos INTEGER** (`TotalCentavos`, `PrecoUnitarioCentavos`) por `ValueConverter<decimal,long>` | O SQLite não tem `decimal`. Por padrão o EF grava TEXT e traduz `SUM`/`ORDER BY` com funções próprias (`ef_sum`, `COLLATE EF_DECIMAL`), que só existem em conexões abertas pelo EF. Testei as duas formas. Com centavos, `SUM` é nativo e indexável. |
| `Total` desnormalizado no pedido | O pedido é imutável depois de criado (só o status muda). Assim a ordenação e o painel não precisam de `JOIN` nos itens. |
| `Entrega` como tipo complexo **opcional** em colunas `Entrega_*` (todas `NULL`) | Endereço é um valor sem identidade. O EF 10 detecta "nulo" por `"Entrega_Cep" IS NULL`. |
| `Metadados` como tipo complexo **em JSON** (coluna `Metadados TEXT`) | Ver 3.5. |
| Índice único `(PedidoId, Sku)` | Garante a regra "sem SKU repetido" e já serve de índice da FK. O EF não criou um índice duplicado. |
| `HasMaxLength` | O SQLite **ignora** (tudo é TEXT). O limite real é garantido pelo domínio. |
| Índices `(CriadoEmUtc, Id)`, `(ClienteId, CriadoEmUtc, Id)`, `(Status, CriadoEmUtc, Id)`, `(Regiao)` | Derivados das consultas reais. A segunda migration (`IndicesComDesempate`) acrescentou o `Id` nos dois índices de filtro depois da análise dos planos (3.6). |

Um teste (`Migration_corresponde_ao_modelo...`) usa `HasPendingModelChanges()` para falhar se o modelo mudar sem migration.

### 3.2 Consultas enxutas (SQL real capturado em `sql-endpoints.txt`)

Listagem paginada (`GET /pedidos?regiao=Sul&status=Concluido&pagina=2&tamanho=5`), que executa **2 comandos**, `COUNT` e página:

```sql
SELECT "p"."Id", "p"."ClienteId", CASE "p"."Regiao" WHEN 1 THEN 'Sul' ... END, ..., "p"."TotalCentavos",
       (SELECT COUNT(*) FROM "ItensPedido" AS "i" WHERE "p"."Id" = "i"."PedidoId"), "p"."CriadoEmUtc"
FROM "Pedidos" AS "p"
WHERE "p"."Regiao" = @regiao AND "p"."Status" = @status
ORDER BY "p"."CriadoEmUtc" DESC, "p"."Id" DESC
LIMIT @p1 OFFSET @p
```

Relatório de pedidos com itens (`GET /relatorios/pedidos-itens`), com **1 comando** para qualquer tamanho de página:

```sql
SELECT "p0"."Id", "p0"."ClienteId", "p0"."TotalCentavos", "i"."Sku", "i"."Quantidade",
       CAST("i"."Quantidade" AS INTEGER) * "i"."PrecoUnitarioCentavos", "i"."Id"
FROM (SELECT ... FROM "Pedidos" AS "p" ORDER BY "p"."CriadoEmUtc" DESC, "p"."Id" DESC LIMIT @p1 OFFSET @p) AS "p0"
LEFT JOIN "ItensPedido" AS "i" ON "p0"."Id" = "i"."PedidoId"
ORDER BY "p0"."CriadoEmUtc" DESC, "p0"."Id" DESC, "i"."Sku"
```

O subtotal `Quantidade * PrecoUnitario` é calculado **no banco**, em centavos, e convertido de volta pelo mesmo `ValueConverter`. Um teste compara a soma dos subtotais com o total gravado.

Painel por região: 2 comandos `GROUP BY` com `SUM("TotalCentavos")` e `SUM("Quantidade")`. O resultado bate com o cálculo feito em memória sobre os 2.000 pedidos do teste.

**Atualização com tracking** (`PUT /pedidos/{id}/status`):

```sql
SELECT "p"."Id", "p"."Categoria", ... , "p"."Metadados" FROM "Pedidos" AS "p" WHERE "p"."Id" = @id LIMIT 1
UPDATE "Pedidos" SET "Status" = @p0 WHERE "Id" = @p1 RETURNING 1;
```

O change tracker detectou que só `Status` mudou. Também implementei e testei a alternativa **sem tracking** (`AlterarStatusSemTrackingAsync`). Ela lê só o status (`SELECT "p"."Status"`), anexa um *stub* com a chave e marca `IsModified` só em `Status`. O `UPDATE` é o mesmo, a leitura é menor, e o teste confirma que nenhuma outra coluna foi sobrescrita. Não é a estratégia padrão por um motivo: o stub contém valores falsos nas demais propriedades, e marcar a propriedade errada os gravaria.

Os logs saem com `Parameters=[@regiao='?' ...]`, sem os valores dos parâmetros, porque `EnableSensitiveDataLogging` não está habilitado.

### 3.3 Carregamento relacionado: projeção, consulta única e dividida

| Estratégia | Comandos | Colunas | Observação |
|---|---|---|---|
| Laço ingênuo (anti-exemplo, só no teste) | **21** para 20 pedidos (1 + N) | todas | A "guarda" `ContadorComandos.NoMaximoAsync(1, ...)` falha com a lista dos comandos |
| `Include(p => p.Itens)` | 1 | **todas** as das duas entidades | Usado só no teste do painel, para comparar com o cálculo em memória |
| Projeção, consulta única (**adotada**) | 1 | só as do DTO | |
| Projeção + `AsSplitQuery` | 2 | só as do DTO | Mesmo resultado (testado) |

### 3.4 Consulta compilada × normal

A consulta repetida escolhida foi o "resumo por id", que tem um único parâmetro escalar (`Guid`). O benchmark tem três variantes: normal, compilada e ADO.NET puro, com o mesmo SQL e um comando preparado. A versão ADO.NET separa o **tempo do banco** do **custo do EF**.

| Método | Média | Erro (IC 99,9%) | Ratio | Alocado |
|---|---:|---:|---:|---:|
| `PorId_Normal` (EF, LINQ) | 39,91 µs | ±1,57 µs | 1,00 | 12,11 KB |
| `PorId_Compilada` (EF.CompileAsyncQuery) | 25,23 µs | ±0,24 µs | 0,63 | 4,13 KB |
| `PorId_AdoNet` (SQL direto, preparado) | 3,99 µs | ±0,03 µs | 0,10 | 1,16 KB |

Leitura dos números:

- O SQLite em memória responde em menos de 4 µs. O restante é custo do cliente: cerca de 36 µs na versão normal e cerca de 21 µs na compilada.
- A compilação economiza **14,7 µs e 8 KB por chamada**.
- O ADO.NET puro não traduz os enums com `CASE`, então o SQL não é exatamente igual. O número dele é um limite inferior.
- O BDN avisou que a distribuição da versão normal pode ser multimodal.

**Decisão: manter a versão simples na API**, deixando a compilada implementada e testada. Os motivos:

1. O ganho é absoluto e pequeno (15 µs), sem medição de latência HTTP que mostre que ele importa. Isso será verificado sob carga na Atividade 5.
2. A restrição de **modelo único** é real neste projeto. O teste `Consulta_compilada_fica_presa_ao_primeiro_modelo` mostra que, depois de compilada com o modelo JSON, a consulta lança `InvalidOperationException: ... Compiled queries can only be used with a single model` ao ser usada com o modelo relacional. A versão normal funciona nos dois.

### 3.5 Mapeamento relacional × JSON para `Metadados`

Os dois mapeamentos convivem no código (`MapeamentoMetadados.Json | Relacional`). Como o modelo muda conforme a opção, foi preciso uma `IModelCacheKeyFactory` customizada. Sem ela, o EF reaproveitaria o primeiro modelo (testado). Comparação com a mesma massa (300 pedidos):

| Aspecto | JSON (adotado) | Relacional (colunas) |
|---|---|---|
| Esquema | 1 coluna `Metadados TEXT` | `Metadados_Canal`, `Metadados_Origem`, `Metadados_Campanha` |
| Filtro `Canal == "web"` | `WHERE "p"."Metadados" ->> 'Canal' = @canal` | `WHERE "p"."Metadados_Canal" = @canal` |
| Resultado do filtro | 50 linhas | 50 linhas (igual ao filtro em memória nos dois) |
| Plano | `SCAN p` (sem índice) | indexável com `CREATE INDEX` comum |
| Alterar 1 campo via tracking | `UPDATE ... SET "Metadados" = @p0` (**documento inteiro**) | `UPDATE ... SET "Metadados_Canal" = @p0` |
| Alterar via `ExecuteUpdate` | `json_set("p"."Metadados", '$.Campanha', @p)` (no banco) | `SET "Metadados_Campanha" = @p` |
| Tracking | funciona por propriedade, mas grava o documento todo | por coluna |
| Evolução | novo campo opcional **sem migration** (documentos antigos não têm o campo e são lidos como `null`) | migration a cada campo |
| Portabilidade | funções JSON variam: `->>`/`json_set` no SQLite, `JSON_VALUE` no SQL Server, `jsonb` no PostgreSQL. O EF abstrai a consulta, mas não índices nem scripts manuais | SQL padrão |
| Índice | exigiria índice de expressão `(Metadados ->> 'Canal')` via `migrationBuilder.Sql`, que o EF não modela | nativo |

**Decisão.** JSON para `Metadados`, porque nenhuma consulta quente filtra por esses campos. Eles são exibidos no detalhe e mudam pouco, e o formato evolui sem migration. **Critério de revisão:** se o painel passar a filtrar ou agrupar por canal, `Canal` vira coluna (ou ganha índice de expressão). O endereço ficou em colunas, porque cidade e UF são candidatos naturais a filtro.

### 3.6 Plano de execução e ajuste de índices

`EXPLAIN QUERY PLAN` sobre o banco com 20.000 pedidos (`planos-execucao.txt`):

| Consulta | Migration `Inicial` | Depois de `IndicesComDesempate` |
|---|---|---|
| Filtro por status + ordenação | `SEARCH USING INDEX IX_Pedidos_Status_CriadoEm` + **`USE TEMP B-TREE FOR LAST TERM OF ORDER BY`** | `SEARCH USING INDEX IX_Pedidos_Status_CriadoEm_Id` (sem ordenação temporária) |
| Filtro por cliente + ordenação | `SEARCH ... IX_Pedidos_Cliente_CriadoEm` + `TEMP B-TREE` | `SEARCH USING COVERING INDEX IX_Pedidos_Cliente_CriadoEm_Id` |
| Página 900 (offset) | `SCAN USING COVERING INDEX IX_Pedidos_CriadoEm_Id` (percorre 17.980 entradas para descartá-las) | igual |
| Cursor na mesma posição | `SCAN USING COVERING INDEX IX_Pedidos_CriadoEm_Id` (começa no ponto certo) | igual |
| Relatório com itens | `SEARCH i USING COVERING INDEX UX_ItensPedido_Pedido_Sku (PedidoId=?)` | igual |
| Filtro JSON por canal | `SCAN p` | igual (ver 3.5) |

O desempate por `Id`, necessário para a paginação ser determinística, não estava nos índices de filtro. Por isso o SQLite precisava ordenar o resultado numa árvore temporária. A segunda migration (`DROP INDEX` + `CREATE INDEX` com `Id`) foi aplicada pela própria API sobre o banco existente.

### 3.7 Extensões: offset × cursor e consulta única × dividida

| Método | Média | Erro | Alocado |
|---|---:|---:|---:|
| Offset, página 1 (+ `COUNT`) | 125,9 µs | ±4,9 µs | 32,3 KB |
| Offset, página 900 (+ `COUNT`) | 583,5 µs | ±45,3 µs | 32,3 KB |
| Cursor na posição da página 900 | 138,2 µs | ±0,4 µs | 33,3 KB |
| Relatório 100 pedidos, consulta única | 967,0 µs | ±26,4 µs | 259,9 KB |
| Relatório 100 pedidos, `AsSplitQuery` | 905,3 µs | ±3,0 µs | 257,0 KB |

- **Offset × cursor.** O custo do offset cresce com a profundidade: pular 17.980 linhas custa cerca de 458 µs a mais que a página 1. O cursor custa praticamente o mesmo em qualquer posição e foi **4,2× mais rápido** na página 900. A comparação não é perfeitamente justa, e por um motivo que é também a principal diferença funcional: o offset inclui o `COUNT(*)` do total, que o cursor não oferece. **Decisão:** manter os dois. O offset serve telas com número de página e total. O cursor serve rolagem infinita e integrações, e evita duplicar ou pular itens quando chegam pedidos novos. Um teste garante que os dois percorrem a mesma sequência. O cursor é opaco (base64url), e um cursor inválido é tratado como primeira página.
- **Única × dividida.** A dividida foi cerca de 6% mais rápida com 1 a 5 itens por pedido. Mas o banco em memória não tem latência de rede, e com um servidor real o segundo roundtrip custaria mais que isso. Além disso, os dois comandos da consulta dividida não formam uma leitura consistente sem transação. **Decisão:** consulta única. A explosão cartesiana é pequena com até 5 itens. Reavaliar se o grafo crescer.
- **Guarda de comandos:** `ContadorComandos.NoMaximoAsync(n, operacao)` falha listando os comandos SQL executados. Ela é usada nos testes do relatório (máximo de 1 comando) e prova que o laço ingênuo seria pego.

## 4. Testes e validações obrigatórias

Resultado: **135 testes, 135 aprovados** (27 novos nesta atividade), lista em `docs/evidencias/atividade3/testes.txt`. Os testes de banco usam SQLite **em memória** com a conexão mantida aberta, e a API em teste usa um banco em memória exclusivo por fábrica (`HubApiFactory`).

| Validação exigida | Teste(s) |
|---|---|
| Consulta paginada só com colunas e linhas necessárias | `Pagina_traz_somente_colunas_e_linhas_necessarias` (verifica `LIMIT @`/`OFFSET @` e ausência de `Metadados`, `Entrega_`, `Prioridade`, colunas de itens no SQL capturado); `Ordenacao_e_deterministica_e_paginas_cobrem_tudo_sem_repetir` |
| Atualização pelo mesmo contexto usa tracking; alternativa documentada e testada | `Alteracao_com_tracking_gera_update_somente_do_status`; `Alternativa_sem_tracking_anexa_explicitamente_e_nao_corrompe_outras_colunas`; `Transicao_invalida_e_pedido_inexistente_nao_gravam_nada` |
| Sem N+1 no relatório de pedidos e itens | `Relatorio_de_pedidos_e_itens_usa_um_unico_comando` (5 e 50 pedidos → 1 comando); `Abordagem_ingenua_gera_N_mais_1_e_a_guarda_detecta` (21 comandos); `Consulta_dividida_usa_dois_comandos...` |
| Parâmetros não mudam a forma da expressão | `Valores_do_usuario_viram_parametros_e_nao_mudam_a_forma_do_sql` (SQL idêntico para `CLI-00001` e `x' OR 1=1 --`; nenhum valor aparece no texto) |
| JSON testado só com provedor e versão registrados | `Migration_corresponde_ao_modelo_e_ao_provedor_registrado` (provedor `Microsoft.EntityFrameworkCore.Sqlite`, EF 10); `Json_e_relacional_filtram_igual_mas_geram_sql_diferente` (×2); `ExecuteUpdate_em_json_usa_json_set...`; `Modelos_json_e_relacional_coexistem...` |
| Consulta compilada | `Consulta_compilada_devolve_o_mesmo_que_a_normal`; `Consulta_compilada_fica_presa_ao_primeiro_modelo` |
| Fluxo HTTP com persistência | `ConsultasEndpointTests` (criação → detalhe com endereço e JSON; máquina de estados 204/409/400/404; filtro por cliente e limite de tamanho) |

Ajustes feitos para os testes ficarem determinísticos (registrados para a Atividade 8):

- O teste de memória da Atividade 2 passou a rodar numa coleção **sem paralelismo**. `GC.GetTotalMemory` mede o processo inteiro, e os testes que semeiam bancos em paralelo o tornavam instável.
- Um teste de atualização usava o valor `"loja"`, que podia já ser o valor gravado. Nesse caso o change tracker não detectava mudança e não gerava `UPDATE`. O teste passou a usar um valor que não existe na massa.

### Aprimoramentos (as três extensões do roteiro)

1. **Paginação por deslocamento × cursor**: implementada, testada e medida (seção 3.7).
2. **Consulta dividida × única em grafo controlado**: medida e com decisão justificada (seção 3.7).
3. **Teste que falha com comandos SQL em excesso**: `ContadorComandos.NoMaximoAsync` (seções 3.3 e 3.7).
4. Extras: dois modelos no mesmo processo com `IModelCacheKeyFactory`; ajuste de índices guiado por `EXPLAIN QUERY PLAN`, com migration incremental.

## 5. Referências

> Mantenha somente as fontes que você realmente consultou.

MICROSOFT. **Efficient querying (EF Core)**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/ef/core/performance/efficient-querying. Acesso em: 9 nov. 2026.

MICROSOFT. **Tracking vs. No-Tracking Queries**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/ef/core/querying/tracking. Acesso em: 9 nov. 2026.

MICROSOFT. **Advanced performance topics: compiled queries**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/ef/core/performance/advanced-performance-topics. Acesso em: 9 nov. 2026.

MICROSOFT. **Single vs. split queries**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/ef/core/querying/single-split-queries. Acesso em: 9 nov. 2026.

MICROSOFT. **Pagination (EF Core)**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/ef/core/querying/pagination. Acesso em: 9 nov. 2026.

MICROSOFT. **What's new in EF Core 10: complex types and JSON**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/ef/core/what-is-new/ef-core-10.0/whatsnew. Acesso em: 9 nov. 2026.

MICROSOFT. **SQLite EF Core database provider limitations**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/ef/core/providers/sqlite/limitations. Acesso em: 9 nov. 2026.

SQLITE. **EXPLAIN QUERY PLAN**. 2025. Disponível em: https://www.sqlite.org/eqp.html. Acesso em: 9 nov. 2026.
