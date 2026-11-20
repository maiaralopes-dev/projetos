::: {custom-style="Capa Titulo"}
PROGRAMAÇÃO APLICADA EM .NET
:::

::: {custom-style="Capa Titulo"}
ANÁLISE E DESENVOLVIMENTO DE SISTEMAS
:::

::: {custom-style="Capa Titulo"}
RELATÓRIO DE AULAS PRÁTICAS
:::

::: {custom-style="Capa Subtitulo"}
Relatórios 1 a 8 — Hub de Processamento de Pedidos
:::

::: {custom-style="Capa Campo"}
Nome: ___________________________________________
:::

::: {custom-style="Capa Campo"}
RA: ______________________________________________
:::

::: {custom-style="Capa Campo"}
Polo de matrícula: __________________________________
:::

::: {custom-style="Capa Campo"}
Local da realização da Aula Prática: _____________________
:::

::: {custom-style="Capa Campo"}
Ano da postagem: 2026
:::


```{=openxml}
<w:p><w:r><w:br w:type="page"/></w:r></w:p>
```


```{=openxml}
<w:sdt><w:sdtPr><w:docPartObj><w:docPartGallery w:val="Table of Contents"/><w:docPartUnique/></w:docPartObj></w:sdtPr><w:sdtContent>
<w:p><w:pPr><w:pStyle w:val="TOCHeading"/></w:pPr><w:r><w:t>SUMÁRIO</w:t></w:r></w:p>
<w:p><w:r><w:fldChar w:fldCharType="begin" w:dirty="true"/></w:r><w:r><w:instrText xml:space="preserve"> TOC \o "1-1" \h \z \u </w:instrText></w:r><w:r><w:fldChar w:fldCharType="separate"/></w:r><w:r><w:t>Atualize este campo (Word: clique com o botão direito → Atualizar campo) para montar o sumário.</w:t></w:r><w:r><w:fldChar w:fldCharType="end"/></w:r></w:p>
</w:sdtContent></w:sdt>
```


```{=openxml}
<w:p><w:r><w:br w:type="page"/></w:r></w:p>
```

# Apresentação

Este documento reúne os oito relatórios de aulas práticas da disciplina **Programação Aplicada em .NET**.
As atividades são cumulativas: todas evoluem a mesma aplicação, o **Hub de Processamento de Pedidos**,
uma API em .NET 10 / C# 14 / ASP.NET Core 10 / EF Core 10. Cada atividade parte da versão executável
da anterior.

| Atividade | Eixo |
|---|---|
| 1 | Pattern matching, atributos, reflexão controlada, records e DTOs imutáveis |
| 2 | Span&lt;T&gt;, Memory&lt;T&gt;, pooling e caminhos críticos limitados por E/S e medidos |
| 3 | Consultas previsíveis, projeções, tracking, consultas compiladas e JSON |
| 4 | Channels, SemaphoreSlim, coleções concorrentes e backpressure |
| 5 | BenchmarkDotNet, counters, traces, p95/p99 e testes de carga |
| 6 | Modularização, IoC, fronteiras e versionamento compatível de APIs |
| 7 | SignalR, reconexão, streaming, cache de resposta e ETags |
| 8 | WebApplicationFactory, bancos efêmeros, analisadores e revisão automatizada |

**Repositório e reprodução.** O código, os testes, os scripts e as evidências de execução estão na pasta
`HubPedidos/` do repositório entregue. As evidências citadas em cada capítulo ficam em
`HubPedidos/docs/evidencias/atividadeN/`. Para reproduzir: `dotnet test` (suíte completa) ou `./revisao.sh`
(revisão completa, igual à do CI).

**Ambiente.** Cada capítulo registra as versões que influenciam o resultado. As medições de desempenho
(atividades 2 a 5 e 7) foram feitas numa máquina virtual Linux com 4 núcleos e devem ser repetidas no
ambiente do(a) aluno(a) antes da entrega final, trocando os números das tabelas.


```{=openxml}
<w:p><w:r><w:br w:type="page"/></w:r></w:p>
```

# Relatório 1 — Pattern matching, atributos, reflexão controlada, records e DTOs imutáveis

**Unidade 1 – Linguagem avançada em C# e .NET**

**Objetivo:** modelar os contratos e o domínio do Hub de Pedidos com tipos imutáveis, classificação por padrões e descoberta controlada de processadores.

## 1. Entrega e ambiente

- **Repositório:** pasta `HubPedidos/` (solução `HubPedidos.slnx`). Para reproduzir: `dotnet build` e `dotnet test`; para executar: `dotnet run --project src/HubPedidos.Api --launch-profile http`.
- **Projetos:** `HubPedidos.Domain`, `HubPedidos.Application`, `HubPedidos.Api` e `HubPedidos.Tests`, todos em `net10.0`.

| Item | Versão usada |
|---|---|
| SDK .NET | 10.0.114 (runtime Microsoft.NETCore.App 10.0.14) |
| ASP.NET Core | Microsoft.AspNetCore.App 10.0.14 |
| Linguagem | C# 14 (`LangVersion` 14.0 em `Directory.Build.props`) |
| IDE | Visual Studio 2026 versão ______ *(preencher)* |
| Testes | xUnit 2.9.3, Microsoft.NET.Test.Sdk 17.14.1, Microsoft.AspNetCore.Mvc.Testing 10.0.14 |
| SO das evidências | Ubuntu 24.04.5 LTS x86_64 *(trocar pelo seu, se executar de novo)* |

O projeto não usa senha, token nem connection string. Os warnings são tratados como erro (`TreatWarningsAsErrors`), então um braço de `switch` inalcançável impede a compilação.

## 2. Resumo teórico aplicado à atividade

**Pattern matching.** A classificação de pedidos (`ClassificadorPedidos.Classificar`) é uma expressão `switch` que combina vários tipos de padrão. Os **padrões de propriedade** (`{ Regiao: Regiao.Internacional }`) testam campos sem precisar de cast. Os **relacionais** (`>= 5_000m`, `<= 0`) definem os limites. Os **lógicos** (`or`, `and`, `not`) juntam condições, como `{ Prioridade: Prioridade.Urgente } or { Prioridade: Prioridade.Alta, Valor: >= 500m }`. O validador do DTO usa **padrões de lista** (`case null or []:`) para reconhecer uma lista vazia. Numa expressão `switch`, a **ordem dos braços** decide o resultado, porque vale o primeiro braço que casa. Por isso a ordem segue a regra de negócio, do caso mais restritivo para o mais geral: inválido → revisão manual → internacional → alto valor → expresso → atacado → padrão. O compilador verifica duas coisas. Primeiro, a **exaustividade**: o descarte `_` garante que toda entrada recebe uma categoria. Segundo, a **alcançabilidade**: quando incluímos de propósito um braço depois do `_`, o build falhou com `CS8510: The pattern is unreachable` (evidência em `docs/evidencias/atividade1/compilador-braco-inalcancavel.txt`).

**Atributos e reflexão.** Um atributo é um metadado declarativo gravado no assembly, que não executa nada sozinho. `[ProcessadorPedido(CategoriaProcessamento.Expresso, versao: 2)]` apenas *declara* que uma classe atende a uma categoria. A reflexão é o mecanismo que lê esses metadados em tempo de execução (`GetTypes`, `GetCustomAttributes`) para montar o catálogo. Uma varredura ampla, como percorrer `AppDomain.CurrentDomain.GetAssemblies()`, traz riscos. A inicialização fica mais lenta. Tipos inesperados podem entrar no catálogo, como os de testes ou de bibliotecas de terceiros. O resultado passa a depender de quais assemblies já foram carregados, e algumas falhas (`ReflectionTypeLoadException`) só aparecem em produção. Por isso a varredura foi limitada a **um assembly conhecido e um namespace exato**, e acontece **uma única vez**, no início da aplicação.

**Records.** Um `record class` é um tipo de referência com igualdade por valor, `ToString` gerado e cópia não destrutiva com `with`. Um `record struct` tem a mesma semântica, mas é um tipo de valor, sem alocação separada no heap. Por isso foi usado em `PedidoId` e em `DadosClassificacao`. Numa classe comum, a igualdade é por referência. A expressão `with` clona o objeto e reexecuta apenas os `init` das propriedades alteradas.

**Imutabilidade superficial × profunda.** Um record com propriedade `List<T>` é imutável só na superfície: a referência não muda, mas a lista pode receber itens. Em `Pedido`, os itens ficam em um `ImmutableArray<ItemPedido>`, e o próprio `ItemPedido` também é imutável. Isso torna a imutabilidade profunda. Além disso, o mapeador **copia** a lista do DTO, de modo que alterar a lista do cliente depois da criação não afeta o pedido (testado).

**Entidade, value object e DTOs.** `PedidoId` e `ItemPedido` são *value objects*: dois itens com mesmo SKU, quantidade e preço são o mesmo item. `Pedido` é uma **entidade**. Ele é declarado como `record` para aproveitar o `with`, mas a igualdade foi sobrescrita para comparar só o `Id`: dois pedidos com dados iguais e ids diferentes são pedidos distintos. `CriarPedidoRequest` (entrada) e `PedidoResponse` (saída) são DTOs. Eles contêm apenas tipos primitivos e strings, e a conversão entre DTO e domínio é **explícita** em `MapeadorPedidos`.

## 3. Código-fonte, decisões e evidências

### 3.1 Contratos imutáveis e fronteira de validação

| Tipo | Forma | Decisão |
|---|---|---|
| `PedidoId` | `readonly record struct` + `IParsable<PedidoId>` | Usa UUID v7 (`Guid.CreateVersion7`), que é ordenável no tempo. `TryParse` rejeita `Guid.Empty`. `default(PedidoId)` existe, por ser struct, mas `EhValido` é falso e `Pedido` o rejeita. |
| `ItemPedido` | `sealed record` (value object) | Validação dentro dos `init`, usando a palavra-chave **`field` do C# 14**. Assim nem o construtor nem um `with { Quantidade = 0 }` geram item inválido. O SKU é normalizado (trim + maiúsculas). |
| `Pedido` | `sealed record` com igualdade por `Id` | Itens em `ImmutableArray`, com 1 a 100 itens e sem SKU repetido. O total é calculado, não armazenado. |
| DTOs | `sealed record` com campos anuláveis | O JSON pode omitir qualquer campo. A validação ocorre no mapeador. |

A validação acontece em **duas camadas** com papéis diferentes. `MapeadorPedidos.ParaDominio` valida o DTO por completo, acumula **todos** os erros e devolve `Resultado<Pedido>`, sem lançar exceção. O endpoint transforma esse resultado em `400 ValidationProblem` por campo. As validações nos `init` do domínio são a última defesa: lançam `DomainException`, que nunca deve chegar ao cliente. Os nomes de enum também são validados: `Enum.TryParse` aceita `"2"` e `"Sul,Norte"`, então o mapeador só aceita nomes definidos (testado).

### 3.2 Classificação por pattern matching

Trecho principal (`ClassificadorPedidos.cs`):

```csharp
public static CategoriaProcessamento Classificar(DadosClassificacao dados) => dados switch
{
    { Valor: <= 0m } or { QuantidadeItens: <= 0 }
        or { Regiao: < Regiao.Sul or > Regiao.Internacional }
        or { Prioridade: < Prioridade.Normal or > Prioridade.Urgente } => Invalido,
    { Regiao: Regiao.Internacional, Valor: >= 10_000m } => RevisaoManual,
    { Regiao: Regiao.Internacional }                     => Internacional,
    { Valor: >= 5_000m }                                  => AltoValor,
    { Prioridade: Prioridade.Urgente }
        or { Prioridade: Prioridade.Alta, Valor: >= 500m } => Expresso,
    { QuantidadeItens: >= 20 }                            => Atacado,
    _                                                     => Padrao,
};
```

A classificação recebe `DadosClassificacao`, uma projeção da entidade, e não o `Pedido`. Assim o braço de **inválido** pode ser testado com valores que o domínio nunca deixaria existir, como enum `99`, valor negativo ou zero itens. Isso é defesa em profundidade: se um dia o pedido vier do banco ou de outra fonte, a regra continua segura.

**Tabela de casos cobertos** (teste parametrizado `Classifica_cada_classe_de_equivalencia`, 21 casos):

| Classe de equivalência / limite | Valor | Itens | Região | Prioridade | Resultado |
|---|---|---|---|---|---|
| Normal nacional | 100 | 1 | Sudeste | Normal | Padrao |
| Menor valor positivo | 0,01 | 1 | Norte | Normal | Padrao |
| Abaixo / no limite de atacado | 300 | 19 / **20** | Sul | Normal | Padrao / **Atacado** |
| Urgente barato | 10 | 1 | Nordeste | Urgente | Expresso |
| Alta abaixo / no limite | 499,99 / **500** | 1 | Sul | Alta | Padrao / **Expresso** |
| Urgente com 20 itens (ordem dos braços) | 300 | 20 | Sul | Urgente | Expresso |
| Abaixo / no limite de alto valor | 4.999,99 / **5.000** | 1 | Centro-Oeste | Normal | Padrao / **AltoValor** |
| Alto valor vence urgência | 8.000 | 1 | Sudeste | Urgente | AltoValor |
| Internacional (normal e urgente) | 100 | 1 | Internacional | Normal/Urgente | Internacional |
| Internacional abaixo / no limite | 9.999,99 / **10.000** | 1 | Internacional | Normal | Internacional / **RevisaoManual** |
| Inválidos: valor 0, valor −1, 0 itens, região 99, região 0, prioridade 42 | — | — | — | — | **Invalido** |

Um teste extra (`Toda_categoria_exceto_invalido_e_alcancavel`) confirma que todas as categorias do enum aparecem na tabela. Assim nenhuma regra fica sem teste.

### 3.3 Atributo e catálogo com reflexão restrita

`CatalogoProcessadores.Construir(assembly, prefixoNamespace)` faz o seguinte:

1. inspeciona **somente** o assembly recebido e o namespace `HubPedidos.Application.Processadores.Implementacoes`. A comparação é exata (`ns == prefixo || ns.StartsWith(prefixo + ".")`), e um teste com o namespace "vizinho" `...VersionadosExtra` comprova que ele não é incluído;
2. ordena os tipos por `FullName`, o que torna o resultado **determinístico**;
3. rejeita tipo que não é classe, que é abstrato, que é genérico aberto, que não implementa `IProcessadorPedido` ou que não tem construtor público, além de categoria `Invalido` e versão menor que 1;
4. detecta duplicidade de (categoria, versão) e, para cada categoria, escolhe a **maior versão** (o que permite evoluir sem apagar a versão anterior);
5. exige que todas as categorias processáveis tenham processador;
6. reúne **todos** os problemas numa única `CatalogoInvalidoException` com mensagem legível, por exemplo: `Categoria Padrao v1 declarada por mais de um tipo: PadraoA, PadraoB.`

No `Program.cs`, o catálogo é construído uma vez e cada tipo é registrado como **keyed service** (`AddKeyedScoped(typeof(IProcessadorPedido), categoria, tipo)`), com `ValidateOnBuild = true`. Com isso, o contêiner também valida os construtores na inicialização: se houver erro, a API não sobe. Processadores implementados: `ProcessadorPadrao` (Padrão e Atacado), `ProcessadorExpresso` (v2), `ProcessadorAntifraude` (AltoValor e RevisaoManual) e `ProcessadorInternacional`.

### 3.4 Fluxo integrado (endpoint)

`POST /pedidos` segue quatro passos: DTO → `MapeadorPedidos.ParaDominio` (400 se inválido) → `ClassificadorPedidos.Classificar` → processador obtido pela categoria → `PedidoResponse`. A resposta contém apenas strings e números. Um teste arquitetural garante que nenhum DTO expõe tipo do namespace `HubPedidos.Domain`. Execução real (trecho de `docs/evidencias/atividade1/http.txt`):

```
POST /pedidos  {"regiao":"Sul","prioridade":"Urgente","itens":[{"sku":"notebook","quantidade":1,"precoUnitario":3999.90}], ...}
HTTP/1.1 200 OK
{"id":"01a1b93d-ea5b-7a53-...","total":3999.90,"categoria":"Expresso","processador":"ProcessadorExpresso","versaoProcessador":2, ...}

POST /pedidos  {"clienteId":"","regiao":"Marte","prioridade":"99","itens":[{"sku":"x","quantidade":0,"precoUnitario":1.999}]}
HTTP/1.1 400 Bad Request
{"errors":{"clienteId":[...],"regiao":[...],"prioridade":[...],"itens[0].quantidade":[...],"itens[0].precoUnitario":[...]}}
```

### 3.5 Registro explícito × descoberta por reflexão

| Critério | Registro explícito | Reflexão restrita (adotada) |
|---|---|---|
| Legibilidade | Tudo visível no `Program.cs` | Exige ler o atributo em cada classe |
| Esquecer um processador | Falha só em tempo de execução, na 1ª requisição daquela categoria | Falha **na inicialização** ("Nenhum processador para a categoria X") |
| Duplicidade | Fica silenciosa (o último registro vence) | Detectada e reportada |
| Custo de inicialização | Nenhum | Uma varredura de um único assembly, uma vez |
| Trimming/AOT | Compatível | Exige cuidado, porque o trimmer pode remover tipos só achados por reflexão |

Com apenas 4 processadores, o registro explícito também seria aceitável. A reflexão foi escolhida porque o **metadado fica junto da implementação** e porque as validações de consistência (cobertura, duplicidade, versão) ficam centralizadas. Os limites da escolha: um assembly, um namespace, execução única e falha imediata.

### 3.6 Onde imutabilidade e reflexão ajudaram ou complicaram

- **Ajudou:** `with` + validação no `init` (C# 14, `field`) impedem que uma "cópia alterada" quebre invariantes. Um teste confirma que `pedido with { Itens = [] }` lança `DomainException`.
- **Ajudou:** a cópia defensiva para `ImmutableArray` eliminou o risco de o cliente alterar a lista depois da validação.
- **Complicou:** `ImmutableArray<T>` compara a **referência** do array interno, e não o conteúdo. O teste de determinismo do catálogo falhou na primeira versão por esse motivo, mesmo com dois catálogos idênticos. A correção foi comparar como sequência (`AsEnumerable()`). Pelo mesmo motivo, a igualdade gerada de `Pedido` não funcionaria por valor, o que reforçou a decisão de usar igualdade por `Id`.
- **Complicou:** `default(PedidoId)` sempre existe, por ser struct. Foi preciso criar `EhValido` e rejeitar o valor no domínio.
- **Complicou (reflexão):** a ordem de `GetTypes()` não é garantida. Sem a ordenação explícita, o catálogo poderia variar entre execuções.

## 4. Testes e validações obrigatórias

Resultado: **72 testes, 72 aprovados, 0 falhas** (`dotnet test`; lista completa em `docs/evidencias/atividade1/testes.txt`).

| Validação exigida | Teste(s) que comprovam |
|---|---|
| Classificação normal, prioritário, inválido e caso-limite | `ClassificadorTests.Classifica_cada_classe_de_equivalencia` (21 casos), `Pedido_nulo_e_invalido` |
| `switch` sem braço inalcançável e com saída explícita para entrada desconhecida | build com warnings como erro (CS8510 demonstrado) + casos `região 99`, `prioridade 42` → `Invalido` |
| Record copiado com `with` sem alterar o original | `ImutabilidadeTests.Copia_com_with_nao_altera_o_original`, `Pedido_usa_igualdade_por_identidade` |
| DTO inválido rejeitado antes de criar o domínio | `MapeadorTests.Dto_invalido_e_rejeitado_antes_do_dominio_com_todos_os_erros`, `EndpointTests.Dto_invalido_retorna_400_com_erros_por_campo` |
| Reflexão limitada ao assembly/namespace, sem duplicar processadores | `CatalogoProcessadoresTests` (8 testes: assembly único, prefixo exato, duplicidade, metadados inválidos, maior versão, cobertura, determinismo) |
| Igualdade e validação | `ItemPedido_tem_igualdade_por_valor`, `ItemPedido_rejeita_invariantes_quebradas`, `With_no_pedido_nao_permite_estado_invalido`, `Lista_do_dto_alterada_depois_nao_afeta_o_pedido` |
| Fluxo completo | `EndpointTests.Fluxo_completo_classifica_e_seleciona_processador` (5 categorias via HTTP com `WebApplicationFactory`) |

### Aprimoramentos (extensões realizadas)

1. **`IParsable<T>` / `TryParse` em value object:** `PedidoId` implementa `IParsable<PedidoId>`. Os testes cobrem ida e volta por texto, rejeição de texto inválido e de `Guid.Empty` sem exceção, e uso via restrição genérica `where T : IParsable<T>`.
2. **Teste arquitetural** (`ArquiteturaTests`, feito só com reflexão, sem biblioteca externa). Ele verifica quatro regras: o domínio não referencia Application, ASP.NET Core nem EF Core; a camada de contratos não referencia ASP.NET Core nem EF Core; nenhuma propriedade de DTO usa tipo do domínio (inclusive dentro de genéricos, como `IReadOnlyList<T>`); todo DTO é `sealed record` com propriedades *init-only*.
3. **Validação de propriedade com `field` (C# 14)**, que fecha a brecha do `with` sobre invariantes.
4. *(Não realizado)* O *source generator* do catálogo fica como trabalho futuro: ele eliminaria a reflexão em tempo de execução e tornaria o catálogo compatível com Native AOT.

## 5. Referências

> Mantenha somente as fontes que você realmente consultou.

MICROSOFT. **Pattern matching overview**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/dotnet/csharp/fundamentals/functional/pattern-matching. Acesso em: 8 nov. 2026.

MICROSOFT. **Records (C# reference)**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/record. Acesso em: 8 nov. 2026.

MICROSOFT. **The field keyword (C# reference)**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/field. Acesso em: 8 nov. 2026.

MICROSOFT. **Attributes and reflection**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/dotnet/csharp/advanced-topics/reflection-and-attributes/. Acesso em: 8 nov. 2026.

MICROSOFT. **Dependency injection in .NET: keyed services**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/dotnet/core/extensions/dependency-injection. Acesso em: 8 nov. 2026.

MICROSOFT. **ImmutableArray&lt;T&gt; Struct**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/dotnet/api/system.collections.immutable.immutablearray-1. Acesso em: 8 nov. 2026.

MICROSOFT. **IParsable&lt;TSelf&gt; Interface**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/dotnet/api/system.iparsable-1. Acesso em: 8 nov. 2026.


```{=openxml}
<w:p><w:r><w:br w:type="page"/></w:r></w:p>
```

# Relatório 2 — Span&lt;T&gt;, Memory&lt;T&gt;, pooling e caminhos críticos limitados por E/S e medidos

**Unidade 2 – Performance e alocação eficiente**

**Objetivo:** reduzir cópias e alocações no recebimento de lotes de pedidos, comprovando ganhos e custos com benchmarks reproduzíveis.

## 1. Entrega e ambiente

- **Repositório:** `HubPedidos/`, continuação da Atividade 1. Novos itens: `src/HubPedidos.Application/Importacao/`, o endpoint `POST /pedidos/lote` e o projeto `benchmarks/HubPedidos.Benchmarks`.
- **Reproduzir:** `dotnet test` (108 testes) e `dotnet run -c Release --project benchmarks/HubPedidos.Benchmarks -- --filter "*"`.

| Item | Valor registrado |
|---|---|
| SDK / runtime | .NET SDK 10.0.114 / .NET 10.0.14, X64 RyuJIT x86-64-v4 |
| BenchmarkDotNet | 0.15.8, job `Hub`: WarmupCount=3, IterationCount=15, LaunchCount=1, `MemoryDiagnoser`, build Release fora do depurador |
| SO / CPU | Ubuntu 24.04.5 LTS; Intel Xeon 2,30 GHz, 4 núcleos lógicos (VM em nuvem) *(trocar pelo seu ambiente se reexecutar)* |
| GC | Concurrent Workstation |
| Dados | `GeradorLote`, semente 42: lotes de 100 e 10.000 linhas, de 1 a 5 itens por linha, nomes com UTF-8 multibyte (`JOÃO`, `客户`, `SKU-Ç`) e cerca de 5% de linhas inválidas |
| IDE | Visual Studio 2026 versão ______ *(preencher)* |

**Formato do lote:** texto UTF-8, um pedido por linha, no formato `clienteId;regiao;prioridade;sku:qtd:preco|sku:qtd:preco`. O importador faz só a análise **sintática**. A validação de negócio continua no `MapeadorPedidos` da Atividade 1.

## 2. Resumo teórico aplicado à atividade

**Span&lt;T&gt; / ReadOnlySpan&lt;T&gt;.** São *visões* sobre uma região de memória contígua: um array, um trecho de string, memória da pilha (`stackalloc`) ou memória nativa. Fatiar um span (`linha[campos[1]]`) não copia nada, só ajusta ponteiro e tamanho. Por isso o `Split` de span devolve `Range`s, e não strings. Como um span pode apontar para a pilha, ele é um `ref struct`: só existe na pilha, não pode virar campo de classe nem ser capturado por lambda, e **não pode atravessar `await` ou `yield`**. Quando tentamos fazer isso de propósito, o compilador recusou com `CS4007: Instance of type 'System.Span<byte>' cannot be preserved across 'await' or 'yield' boundary` (`docs/evidencias/atividade2/compilador-span-await.txt`). Por isso toda a análise de bytes ficou em métodos **síncronos** (`Interpretar`), chamados entre um `await` e outro.

**Memory&lt;T&gt; / ReadOnlyMemory&lt;T&gt;.** Representam a mesma ideia de "região de memória", mas são `struct`s comuns, que podem ficar no heap. Por isso podem atravessar `await`: `origem.ReadAsync(buffer.AsMemory(fim), ct)`. A regra adotada foi usar `Memory` para guardar e passar a região entre operações assíncronas, e `.Span` só dentro do trecho síncrono que lê os bytes. Quem recebe um `Memory` não é dono dele. Por isso a **propriedade** do buffer precisa ser explícita: quem aluga devolve.

**Pooling.** `ArrayPool<byte>.Shared.Rent(n)` entrega um array de **pelo menos** n bytes, reaproveitado de um pool. Ele pode ser maior que n e vir **sujo**, com dados de quem usou antes. Três consequências guiaram o código: (1) só a fatia válida `[inicio, fim)` é lida; (2) o `Return` fica em `finally`, para valer também em exceção, cancelamento e abandono do `await foreach`; (3) depois do `Return` o array não pode mais ser usado (um teste detecta devolução duplicada). `MemoryPool<byte>` faz o mesmo por meio de um `IMemoryOwner<byte>` com `Dispose`, o que torna a propriedade explícita no tipo. Escolhi `ArrayPool` porque o buffer nunca sai do método que o alugou. Para dados sensíveis, `Return(array, clearArray: true)` zera o buffer antes de devolvê-lo. Reter um array alugado por muito tempo, ou não devolvê-lo, não quebra o programa: o pool simplesmente aloca outro. Mas o benefício do pool se perde, e esse erro é silencioso. Por isso foi testado.

**Alocação, cópia e GC.** Cada `string` e cada array do `Split` é uma alocação no heap. Mais alocação gera mais coletas Gen0 (a coluna `Gen0` do benchmark) e mais pressão de cache. Ainda assim, **otimizar microcódigo não substitui remover E/S**: um *flush* a mais por item custa mais que todo o ganho do parser (ver 3.4).

**Hot path limitado por E/S.** O corpo da requisição é lido **incrementalmente**, em blocos, com três limites: tamanho máximo de linha, tamanho máximo do lote e `CancellationToken`. A escrita da resposta é agrupada, sem flush por item. A versão baseline (`StreamReader.ReadLineAsync`) é correta, mas não tem limite de linha: uma linha de 1 GB seria lida inteira para a memória.

**Benchmark como experimento.** Há uma linha de base explícita (`[Benchmark(Baseline = true)]`), aquecimento, várias iterações, processo isolado, `MemoryDiagnoser` e a mesma massa de dados para todas as variantes. A origem dos dados é um `MemoryStream`, de propósito, para isolar o custo de CPU do parser. Isso significa que o resultado vale **para o parser, nesta máquina**. Ele não autoriza afirmar que "a API ficou 29% mais rápida".

## 3. Código-fonte, decisões e evidências

### 3.1 Três implementações funcionalmente equivalentes

| Implementação | Leitura | Recorte e conversão | Etapa do roteiro |
|---|---|---|---|
| `ImportadorBaseline` | `StreamReader.ReadLineAsync` → `string` | `string.Split` + `int/decimal.TryParse(string)` | 1 — linha de base |
| `ImportadorSpanChar` | igual (ainda aloca a string da linha) | `ReadOnlySpan<char>.Split` em `Range`s, `TryParse(ReadOnlySpan<char>)`, reuso das strings do enum | 2 — spans |
| `ImportadorUtf8Pool` | `Stream.ReadAsync` em buffer do `ArrayPool` | linhas achadas nos **bytes** (`IndexOf((byte)'\n')`), `int/decimal.TryParse(ReadOnlySpan<byte>)` (UTF-8 direto), só cliente e SKU viram string | 3 e 4 — pooling e fluxo assíncrono |

As três compartilham as mesmas mensagens de erro (`FormatoLote`) e são comparadas por um teste de equivalência. Trecho central da versão otimizada:

```csharp
var buffer = _pool.Rent(_opcoes.TamanhoBufferInicial);
try
{
    while (true)
    {
        while ((posicao = buffer.AsSpan(inicio, fim - inicio).IndexOf((byte)'\n')) >= 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resultado = Interpretar(buffer.AsSpan(inicio, posicao), ++numero); // síncrono: o span não cruza await
            inicio += posicao + 1;
            if (resultado is not null) yield return resultado;
        }
        // ... compacta o resto, cresce até TamanhoMaximoLinha ou lança LoteInvalidoException ...
        var lidos = await origem.ReadAsync(buffer.AsMemory(fim), cancellationToken); // Memory cruza o await
        if ((totalLido += lidos) > _opcoes.LimiteBytes) throw new LoteInvalidoException(...);
        fim += lidos; terminou = lidos == 0;
    }
}
finally
{
    _pool.Return(buffer, clearArray: _opcoes.LimparBuffer);
}
```

Decisões de segurança no código:

- **UTF-8 multibyte em blocos:** os separadores (`\n ; | :`) são ASCII e nunca aparecem dentro de um caractere multibyte. Por isso um bloco pode terminar no meio de `Ã` sem corromper nada: só linhas completas são decodificadas. O teste usa um stream que entrega **1 byte por leitura**, o que força todos os cortes possíveis.
- **Crescimento do buffer:** se uma linha não cabe, o código aluga um buffer com o dobro do tamanho (até o limite), copia o conteúdo e devolve o antigo. Os buffers do crescimento também são devolvidos em caso de falha (testado).
- **Cancelamento verificado por linha:** sem essa verificação, as linhas que já estavam no buffer continuariam sendo processadas depois do cancelamento, porque o token só seria visto no próximo `ReadAsync`. O teste confirma que exatamente 10 linhas são processadas quando o cancelamento ocorre na 10ª.
- **Limite com `Content-Length`:** um corpo declarado acima do limite recebe `413` sem ler nenhum byte. Sem `Content-Length` (chunked), a resposta já começou com `200`, então o erro vira a última linha do NDJSON (`"status":"abortado"`). Evidência real com um lote de 12 MB em `http.txt`.

### 3.2 Benchmark do parser (execução 2, código final)

| Método | Linhas | Média | Erro (IC 99,9%) | Ratio | Gen0 / 1000 op | Alocado | Alloc ratio | Throughput* |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| Baseline | 100 | 59,68 µs | ±0,53 µs | 1,00 | 0,61 | 157,2 KB | 1,00 | 1,68 M linhas/s |
| SpanChar | 100 | 45,71 µs | ±0,90 µs | 0,77 | 0,24 | 71,3 KB | 0,45 | 2,19 M linhas/s |
| Utf8Pool | 100 | 45,43 µs | ±1,61 µs | 0,76 | 0,12 | 44,0 KB | 0,28 | 2,20 M linhas/s |
| Utf8Pool + limpeza | 100 | 46,62 µs | ±1,34 µs | 0,78 | 0,12 | 44,0 KB | 0,28 | 2,14 M linhas/s |
| Baseline | 10.000 | 7,286 ms | ±0,350 ms | 1,00 | 54,7 | 14.761 KB | 1,00 | 1,37 M linhas/s |
| SpanChar | 10.000 | 5,748 ms | ±0,395 ms | 0,79 | 23,4 | 6.593 KB | 0,45 | 1,74 M linhas/s |
| Utf8Pool | 10.000 | 5,154 ms | ±0,186 ms | 0,71 | 15,6 | 4.216 KB | 0,29 | 1,94 M linhas/s |
| Utf8Pool + limpeza | 10.000 | 4,769 ms | ±0,060 ms | 0,66 | 15,6 | 4.216 KB | 0,29 | 2,10 M linhas/s |

\* Throughput = linhas ÷ média. Fonte: `docs/evidencias/atividade2/benchmark-execucao2.md`.

**Leitura dos resultados:**

- O ganho mais **estável** está na alocação, que foi de 1,48 KB para 0,42 KB por linha (−71%). Esse número é determinístico: deu o mesmo nas duas execuções. O tempo das variantes `Utf8Pool` caiu de 22% a 34%.
- O que ainda é alocado é necessário: strings de cliente e SKU, os records do DTO e os arrays de itens.
- A variante **com limpeza saiu mais rápida** que a sem limpeza, o que não faz sentido físico. A diferença (4,77 × 5,15 ms) está dentro da variação. Em outra execução (`benchmark-execucao1-joined.md`), a baseline de 10.000 linhas mediu 6,75 ms, contra 7,29 ms nesta, cerca de 8% de diferença no mesmo código. A conclusão honesta é que **o custo da limpeza não é mensurável neste cenário**: é um único *memset* de 16 KB por lote.
- `SpanChar` × `Utf8Pool` (extensão "UTF-8 direto × decodificação intermediária"): decodificar a linha inteira para `string` antes de recortar custa cerca de 2,4 MB a mais a cada 10.000 linhas (+56%). O ganho de tempo é pequeno (5,75 ± 0,40 × 5,15 ± 0,19 ms), e os intervalos quase se sobrepõem.

### 3.3 Benchmark de escrita: microcódigo × E/S

2.000 resultados NDJSON gravados em arquivo real (`FileStream`), variando a frequência de flush:

| Itens por flush | Média | Alocado | Flushes |
|---:|---:|---:|---:|
| 1 (flush por item) | 5.042,5 µs | 895,7 KB | 2.000 |
| 16 | 1.908,1 µs | 178,1 KB | 125 |
| 256 (adotado) | 884,6 µs | 160,3 KB | 8 |

Agrupar a escrita deixou o processo **5,7× mais rápido**. Esse ganho é muito maior que o de todo o trabalho com spans. Em números: o flush por item acrescenta cerca de 2,1 µs por resultado, enquanto o parser otimizado economiza cerca de 0,2 µs por linha. É a demonstração prática de que, num caminho limitado por E/S, o primeiro passo é **reduzir chamadas de E/S**. O BenchmarkDotNet avisou que a distribuição é bimodal (mValue = 3,5), o que é típico de E/S de disco numa VM. Por isso os números dessa tabela devem ser lidos como ordem de grandeza.

### 3.4 Tabela de decisão

| Otimização | Evidência | Custo de manutenção | Decisão |
|---|---|---|---|
| Spans de `char` (`SpanChar`) | −21% de tempo e −55% de alocação (10 mil linhas) | Baixo: mesmo fluxo da baseline | Seria aceita sozinha, mas foi superada |
| UTF-8 direto + `ArrayPool` (`Utf8Pool`) | −29% de tempo e −71% de alocação; **além disso, ganha limites de linha e de lote**, que a baseline não tem | Médio: ~190 linhas, gerenciamento manual do buffer, coberto por 36 testes | **Aceita** no endpoint `/pedidos/lote` |
| Limpar buffer ao devolver | Custo dentro do ruído | Uma linha de configuração | Mantida como **opção desligada**: o lote não tem dado sensível (cartão, senha). Ligar se isso mudar. |
| Flush agrupado (256) | 5,7× mais rápido e −82% de alocação | Baixo | **Aceita**. O custo é que o cliente recebe a primeira linha só depois de 256 resultados (ou do fim do lote). |
| Reuso das strings dos enums | Faz parte do ganho do `SpanChar`/`Utf8Pool` | Baixo | Aceita só para grafia **exata**: canonizar `"sudeste"` mudaria a saída e quebraria a equivalência com a baseline |

**Diferenças conhecidas entre as versões:** a `Utf8Pool` é *mais rígida* em dois pontos, ambos documentados. (1) Rejeita bytes UTF-8 inválidos na linha, enquanto o `StreamReader` os troca silenciosamente por `U+FFFD`. (2) Considera "linha em branco" apenas a que tem espaços ASCII, enquanto `string.IsNullOrWhiteSpace` também aceita espaços Unicode. Os testes de equivalência usam UTF-8 válido. A rejeição de UTF-8 inválido tem um teste próprio.

## 4. Testes e validações obrigatórias

Resultado: **108 testes, 108 aprovados** (36 novos nesta atividade), lista em `docs/evidencias/atividade2/testes.txt`.

| Validação exigida | Teste(s) |
|---|---|
| Equivalência funcional baseline × otimizada | `Tres_implementacoes_produzem_o_mesmo_resultado` (1, 500 e 3.000 linhas com inválidas; resultados comparados como JSON); `Utf8_e_equivalente_com_qualquer_tamanho_de_buffer` (buffer de 16, 37 e 4.096 bytes; CRLF; leitura de 7 em 7 bytes) |
| Entrada vazia | `Entrada_vazia_ou_so_com_linhas_em_branco_nao_gera_resultado` (×3 implementações) |
| Entrada incompleta | `Ultima_linha_sem_quebra_e_processada...`; `Linha_incompleta_ou_malformada_gera_o_mesmo_erro_nas_tres` (8 casos: 3 e 5 campos, item com 2 e 4 partes, quantidade `um`/`-1`, preço `2,50`/vazio) |
| Acima do limite | `Lote_acima_do_limite_falha_e_devolve_o_buffer`; `Linha_acima_do_limite_falha_e_devolve_todos_os_buffers...`; endpoint: `Content_length_acima_do_limite_retorna_413...` e `Corpo_chunked_acima_do_limite_termina_com_linha_abortado` |
| UTF-8 multibyte | `Utf8_multibyte_cortado_em_qualquer_byte_e_preservado` (`JOÃO-客户`, `CAFÉ-ÇÃ`, 1 byte por leitura); `Utf8_invalido_e_rejeitado_na_linha_sem_derrubar_o_lote` |
| Buffer devolvido em exceção e cancelamento | `PoolRastreado` (pool de teste que registra pendências e falha em devolução duplicada): `Cancelamento_interrompe_e_devolve_o_buffer`, `Abandonar_o_await_foreach_tambem_devolve_o_buffer`, testes de limite; `Limpeza_do_buffer_segue_a_opcao` |
| Benchmark em Release, fora do depurador, com baseline | `dotnet run -c Release`; `[Benchmark(Baseline = true)]`; relatórios do BDN salvos |
| Fluxo grande sem carregar o corpo inteiro | `Fluxo_grande_e_processado_sem_carregar_o_corpo_inteiro`: 64 MiB gerados sob demanda por um stream não pesquisável, processados com **um único** aluguel de 16 KB e menos de 8 MB de memória retida |
| Escrita agrupada | `Flush_agrupado_reduz_chamadas_sem_mudar_a_saida` (2.000/125/8 flushes; saída idêntica) |

### Aprimoramentos

1. **Parsing UTF-8 direto × decodificação intermediária** (extensão do roteiro): comparação `SpanChar` × `Utf8Pool` no mesmo benchmark (seção 3.2).
2. **Comparação de E/S (flush por item × agrupado)** com arquivo real, que mostra quantitativamente o peso de E/S sobre microcódigo (seção 3.3).
3. **Robustez extra:** limites de linha e de lote, `413` antecipado e testes de UTF-8 inválido e de devolução duplicada no pool.
4. *(Não realizado)* `MemoryPool<T>` com `IMemoryOwner` e limite adaptativo de lote ficam como trabalho futuro.

## 5. Referências

> Mantenha somente as fontes que você realmente consultou.

MICROSOFT. **Memory&lt;T&gt; and Span&lt;T&gt; usage guidelines**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/dotnet/standard/memory-and-spans/memory-t-usage-guidelines. Acesso em: 8 nov. 2026.

MICROSOFT. **ArrayPool&lt;T&gt; Class**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/dotnet/api/system.buffers.arraypool-1. Acesso em: 8 nov. 2026.

MICROSOFT. **Fundamentals of garbage collection**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/dotnet/standard/garbage-collection/fundamentals. Acesso em: 8 nov. 2026.

MICROSOFT. **Use the IUtf8SpanParsable interface / UTF-8 parsing**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/dotnet/api/system.iutf8spanparsable-1. Acesso em: 8 nov. 2026.

MICROSOFT. **How to customize character encoding with System.Text.Json**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/character-encoding. Acesso em: 8 nov. 2026.

BENCHMARKDOTNET. **BenchmarkDotNet documentation: Jobs, Diagnosers, Baseline**. 2025. Disponível em: https://benchmarkdotnet.org/articles/overview.html. Acesso em: 8 nov. 2026.


```{=openxml}
<w:p><w:r><w:br w:type="page"/></w:r></w:p>
```

# Relatório 3 — Consultas previsíveis, projeções, tracking, consultas compiladas e JSON

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


```{=openxml}
<w:p><w:r><w:br w:type="page"/></w:r></w:p>
```

# Relatório 4 — Channels, SemaphoreSlim, coleções concorrentes e backpressure

**Unidade 4 – Concorrência e pipelines**

**Objetivo:** processar pedidos por uma fila limitada, controlando produtores, consumidores, acesso ao banco e encerramento da aplicação.

## 1. Entrega e ambiente

- **Repositório:** `HubPedidos/`. A pasta nova é `src/HubPedidos.Application/Processamento/`, com os seguintes tipos:
  - `ProcessarPedidoJob` (o item da fila);
  - `OpcoesFila`;
  - `FilaPedidos` (o canal limitado);
  - `LimitadorBanco` (`SemaphoreSlim`);
  - `MetricasFila` (`Interlocked`, `ConcurrentDictionary` e `System.Diagnostics.Metrics`);
  - `ConsumidorPedidos` (o `BackgroundService`).
- **Endpoints novos:** `POST /pedidos/fila` (produtor) e `GET /fila/metricas`. O `POST /pedidos/lote` passou a enfileirar.
- **Reproduzir:**
  - `dotnet test` (158 testes);
  - `dotnet run -c Release --project benchmarks/HubPedidos.Benchmarks -- experimento-fila linha-tempo.csv`.

| Item | Versão / valor |
|---|---|
| SDK / runtime | .NET SDK 10.0.114 / .NET 10.0.14 (`System.Threading.Channels` do runtime) |
| Banco | SQLite 3.53.3 em arquivo, via EF Core 10.0.14 |
| Máquina | Ubuntu 24.04.5 LTS; Xeon 2,30 GHz, 4 núcleos (VM) *(trocar pelo seu ambiente)* |
| Experimento | 1.500 pedidos/s oferecidos por 6 s; 2 ms de "trabalho externo" simulado por item (fora do semáforo); banco novo por cenário; 1 rodada de aquecimento descartada + 2 rodadas medidas |
| IDE | Visual Studio 2026 versão ______ *(preencher)* |

Configuração adotada (`appsettings.json`, seção `Fila`): `Capacidade=500`, `ModoCheio=Wait`, `TempoMaximoEsperaMs=200`, `Consumidores=4`, `LimiteBanco=1`, `TamanhoLote=32`, `Encerramento=Drenar` e `PrazoDrenagemSegundos=20`.

## 2. Resumo teórico aplicado à atividade

**Produtor/consumidor.** Quem recebe o pedido (o endpoint HTTP) não é quem o processa (o consumidor em segundo plano). A fila entre os dois absorve picos curtos. Uma fila concorrente simples (`ConcurrentQueue`) só guarda itens: o consumidor precisaria ficar consultando a fila em laço, e não haveria limite de tamanho. O `Channel<T>` acrescenta três coisas: **espera assíncrona** dos dois lados (`ReadAllAsync`, `WriteAsync`), **sinal de fim** (`Writer.Complete`) e, na versão *bounded*, um **limite de capacidade** com política definida para quando a fila enche.

**Canal limitado e backpressure.** Com capacidade fixa, a fila não cresce sem limite, e com isso a memória e a latência ficam controladas. O `FullMode` decide o que acontece quando a fila enche:

- `Wait` faz o produtor esperar. Isso é o **backpressure**: a lentidão dos consumidores "empurra de volta" para quem produz.
- `DropOldest`, `DropNewest` e `DropWrite` aceitam sempre e **descartam** algum item. O produtor não fica sabendo.

Há dois resultados diferentes aqui. **Rejeitado** significa que o produtor sabe e recebe 503. **Descartado** significa que o item foi aceito e depois se perdeu.

**SemaphoreSlim.** O `SemaphoreSlim` limita a **concorrência lógica**: quantas tarefas executam um trecho ao mesmo tempo. Ele não aumenta a capacidade do banco. Paralelismo permitido além do que o recurso aguenta só cria disputa. O **SQLite tem um único escritor**: duas transações de escrita simultâneas disputam o mesmo lock, e o `Microsoft.Data.Sqlite` repete a tentativa com espera. No SQL Server, o limite relevante é o **pool de conexões** (padrão `Max Pool Size=100`), compartilhado com as rotas HTTP de leitura. Por isso o semáforo da fila precisa ser uma fração do pool, para não esgotá-lo e causar timeout nas consultas.

**Coleções concorrentes e atomicidade.** Cada operação isolada de uma `ConcurrentDictionary` é segura. Uma **composição** de operações não é: `if (!d.ContainsKey(k)) d[k] = 0; d[k]++` é um "verifica e depois age", com uma leitura, uma soma e uma escrita separadas, e perde incrementos. `AddOrUpdate` faz a atualização de forma atômica em relação ao dicionário. O delegate pode até rodar mais de uma vez, então precisa ser puro. Para contadores simples, `Interlocked.Increment` basta. Para "máximo observado", foi usado um laço de compare-and-swap (`Interlocked.CompareExchange`).

**Cancelamento, conclusão e encerramento gracioso.** Ao parar, o host chama `StopAsync`. A sequência adotada:

1. `Writer.TryComplete()`: produtores passam a receber "encerrando" (503).
2. **Drenar**: os consumidores continuam lendo até o canal esvaziar, porque `ReadAllAsync` termina sozinho quando o writer foi completado e a fila acabou. Isso vale até o prazo do host (`HostOptions.ShutdownTimeout`).
3. Esgotado o prazo, o `stoppingToken` é cancelado. O item em andamento é interrompido e o que sobrou na fila é contado como **abandonado**.

Na política alternativa, **Abandonar**, a leitura para imediatamente.

**Fila, consumidores, pool, latência e throughput.** Pela **Lei de Little**, *L = λ·W*: os itens na fila são o throughput vezes o tempo de espera. Com o throughput limitado pelo banco, uma fila maior **não aumenta o throughput, só aumenta a espera**. Por isso a capacidade deve ser escolhida pela latência aceitável, e a rejeição controlada (503 com `Retry-After`) é melhor do que uma espera longa que o cliente nem vai aguardar.

## 3. Código-fonte, decisões e evidências

### 3.1 Canal limitado e produtor

```csharp
_canal = Channel.CreateBounded<ProcessarPedidoJob>(
    new BoundedChannelOptions(opcoes.Capacidade)
    {
        FullMode = opcoes.ModoCheio,
        SingleReader = opcoes.Consumidores == 1,
        AllowSynchronousContinuations = false, // consumidor não roda na thread da requisição
    },
    itemDropped: _ => metricas.RegistrarDescarte()); // só ocorre nos modos Drop*
```

`FilaPedidos.EnfileirarAsync` tenta `TryWrite` primeiro. Se a fila estiver cheia no modo `Wait`, há duas possibilidades:

- com `TempoMaximoEsperaMs = 0`, a recusa é imediata;
- com tempo de espera, o produtor faz `WriteAsync` com um token ligado ao cancelamento da requisição e a um limite de tempo. Se o tempo esgota, o pedido é rejeitado. Se o **cliente** cancela, a exceção é propagada e **não** conta como rejeição (testado).

**Decisões sobre o item da fila (`ProcessarPedidoJob`):**

- É um `record` imutável com o `Pedido` do domínio (também imutável), a categoria e o instante em que entrou na fila. Não guarda `DbContext` nem entidade rastreada (teste por reflexão).
- **O pedido viaja inteiro, e não só o id, de propósito.** O produtor valida e classifica, mas **não grava**. Com isso, o 503 de fila cheia não deixa nenhum pedido "órfão" no banco, e o cliente pode repetir com segurança.
- O custo dessa decisão é a **consistência eventual**: logo após o `202 Accepted`, `GET /pedidos/{id}` responde 404 até o consumidor gravar. O teste de integração espera o status `Concluido` (com prazo máximo, sem atraso fixo).
- Se o processo cair, os itens que estavam só na memória se perdem. Uma *outbox* persistida resolveria isso e fica registrada como limitação.

Evidência real (`http-e-encerramento.txt`), com a fila pausada e capacidade 2:

```
POST /pedidos/fila → 202 Accepted   Location: /pedidos/01a1be61-cf12-...
POST /pedidos/fila → 202 Accepted
POST /pedidos/fila → 503 Service Unavailable   Retry-After: 1   {"title":"Fila cheia", ...}
GET /fila/metricas → {"profundidade":2,"capacidade":2,"aceitos":2,"rejeitadosFilaCheia":1,"descartados":0, ...}
```

No `POST /pedidos/lote`, cada linha válida é enfileirada **dentro do laço de leitura do corpo**. Com `Wait`, quando os consumidores atrasam, o `await` do enfileiramento segura a leitura do corpo, e o backpressure chega até o cliente HTTP pela própria conexão.

### 3.2 Consumidores

`ConsumidorPedidos` (`BackgroundService`) inicia N tarefas que leem o mesmo canal com `ReadAllAsync`. Para cada item:

1. cria um **escopo de DI próprio**, com um `DbContext` novo, que nunca é compartilhado entre threads;
2. grava o pedido como `EmProcessamento` **dentro do semáforo**;
3. executa o processador da categoria (keyed service) **fora do semáforo**, para não ocupar o slot do banco com trabalho não relacionado;
4. marca o pedido como `Concluido` dentro do semáforo.

Sucesso, falha e duração são registrados com o `PedidoId` num *scope* de log e em mensagens geradas por `[LoggerMessage]`. **Uma falha de item não derruba o consumidor**: o `catch` registra o erro e o laço segue (testado com 5 falhas em 30 itens e um único consumidor).

**Dois bugs reais encontrados pelos testes:**

1. **Estouro de pilha.** No `LimitadorBanco.ExecutarAsync<T>`, a lambda `async ct => resultado = await operacao(ct)` é uma *expressão de atribuição* que devolve `T`. Por isso a resolução de sobrecarga escolhia o próprio método genérico de novo, numa recursão infinita, e o processo de teste caiu. A correção foi usar um corpo em bloco (`{ resultado = await ...; }`), que faz a lambda virar `Func<CancellationToken, Task>`.
2. **Filtro de cancelamento falhando.** O código usava `catch (OperationCanceledException) when (leitura.IsCancellationRequested)`, com `leitura` sendo um token *ligado* (`CreateLinkedTokenSource`). `Cancel()` executa os callbacks em **ordem inversa e de forma síncrona**. O callback do item em andamento, registrado por último, roda primeiro, e a exceção sobe pela pilha **dentro** do próprio `Cancel()`, antes de o token ligado ser marcado. Resultado: o filtro dava falso, e a tarefa dos consumidores terminava como *Canceled*. A correção foi testar os tokens de **origem** (`stoppingToken`, `_pararLeitura`).

Um terceiro ajuste veio da leitura do comportamento do `BackgroundService`. Quando o prazo de drenagem já esgotou, `base.StopAsync` recebe um token cancelado e **não espera** o `ExecuteTask`. Por isso há uma espera curta e independente (5 s) para os consumidores registrarem os itens interrompidos. Sem ela, as métricas eram lidas antes de serem atualizadas: o teste falhou 1 vez em 3 execuções.

### 3.3 Limite do banco e estado compartilhado

```csharp
public async Task ExecutarAsync(Func<CancellationToken, Task> operacao, CancellationToken ct)
{
    await _semaforo.WaitAsync(ct);          // se cancelar aqui, não obteve: não libera
    AtualizarMaximo(Interlocked.Increment(ref _emUso));
    try { await operacao(ct); }
    finally { Interlocked.Decrement(ref _emUso); _semaforo.Release(); }
}
```

- O `Release` fica no `finally` e só é chamado se o `WaitAsync` concluiu. Um teste confirma que um `WaitAsync` cancelado **não** devolve um slot que nunca obteve: `Disponiveis` continua 1, e não passa a 2.
- `MetricasFila` usa `Interlocked` nos contadores e `ConcurrentDictionary.AddOrUpdate` na contagem por categoria. Os mesmos valores são publicados em `System.Diagnostics.Metrics` (meter `HubPedidos.Fila`: contador `hub.fila.itens` por resultado, histogramas `hub.fila.espera` e `hub.fila.processamento` em ms, e medidores `hub.fila.profundidade` e `hub.fila.em_andamento`) para uso com `dotnet-counters` na Atividade 5.
- **Demonstração de check-then-act** (no experimento, 4 threads e 800.000 incrementos em 4 chaves): a versão ingênua contou **778.243 (perdeu 21.757)**, e `AddOrUpdate` contou 800.000. Na execução anterior a versão ingênua perdeu 26.311: o erro muda a cada execução, o que é típico de uma condição de corrida.
- **Observação:** cada contador é exato, mas o *retrato* de vários contadores em `GET /fila/metricas` não é atômico. Numa leitura sob carga, `processados` apareceu como 2.498 e `processadosPorCategoria` como 2.501, porque foram lidos em instantes ligeiramente diferentes.
- **Limite e pool.** Com SQLite, o `LimiteBanco` padrão é **1**, porque o banco aceita um escritor por vez e o pool do `Microsoft.Data.Sqlite` não tem teto. As leituras HTTP não passam pelo semáforo, então ele limita só o consumo da fila. Com SQL Server e pool de 100 conexões, um valor de 8 a 16 deixaria margem para as rotas de leitura. O valor seria escolhido por medição, como no 3.4.

### 3.4 Demonstração de backpressure

![Figura 1: profundidade da fila ao longo do tempo, rodada 1 (`profundidade-fila.svg/png`). Os painéis usam escalas verticais independentes porque C chega a cerca de 5.800 itens enquanto os demais param em 200.](../HubPedidos/docs/evidencias/atividade4/profundidade-fila.png)

Resultados (rodada 1 / rodada 2; `experimento-backpressure.txt`). Em todos os cenários foram oferecidos cerca de 9.000 pedidos.

| Cenário | Throughput | Aceitos | Rejeitados (503) | Descartados (silêncio) | Espera na fila p95 / p99 | Operação no banco p99 |
|---|---|---|---|---|---|---|
| **A** cap 200, 4 cons., banco 1, Wait 100 ms | 562 / 533 /s | 3.630 / 3.460 | 5.363 / 5.535 | 0 | 540 / 545 ms · 573 / 583 ms | 1,4 / 1,8 ms |
| **B** cap 200, 16 cons., **banco 8** | **428 / 617** /s | 3.039 / 4.055 | 5.958 / 4.924 | 0 | 818 / 894 ms · 522 / 609 ms | **301 / 2,3 ms** |
| **C** cap **10.000**, 4 cons., banco 1 | 515 / 509 /s | 8.995 / 8.995 | 0 | 0 | **10,9 / 11,4 s · 11,1 / 11,5 s** | 1,7 / 1,6 ms |
| **D** cap 200, **DropOldest** | 538 / 539 /s | 8.997 / 8.997 | 0 | **5.557 / 5.563 (62%)** | 167 / 341 ms · 165 / 334 ms | 1,5 / 1,4 ms |
| **E** cap 200, Wait **0** (TryWrite) | 525 / 537 /s | 3.427 / 3.424 | 5.569 / 5.573 | 0 | 495 / 514 ms · 466 / 482 ms | 1,7 / 1,5 ms |
| **F** = A + **lote de 32** (extensão) | **913 / 913 /s** | 5.762 / 5.844 | 3.139 / 3.154 | 0 | 352 / 407 ms · 330 / 405 ms | 40 / 17 ms (por lote) |

**Leitura dos resultados:**

- **A × C (capacidade):** o throughput é praticamente o mesmo (cerca de 520 a 560/s), porque o gargalo é o processamento e não a fila. A fila de 10.000 só transformou rejeição em **espera de 11 segundos** e manteve cerca de 5.900 pedidos na memória. A fila continuou drenando até 17,5 s, muito depois do fim da oferta (Figura 1). Nenhum cliente HTTP real esperaria isso.
- **A × D (política):** `DropOldest` parece ótimo na espera (p95 de 165 ms), mas **62% dos pedidos foram descartados depois de o cliente receber 202**. Para pedidos, isso é perda silenciosa de dados. A política só é aceitável para dados descartáveis, como telemetria.
- **A × E (esperar 100 ms ou recusar na hora):** throughput e rejeições semelhantes. Esperar 100 ms ajudou pouco porque a fila ficou cheia o tempo todo (média de 193 itens). No cenário E, a Lei de Little se confirma: cerca de 190 itens ÷ cerca de 531/s ≈ 360 ms, e a mediana medida foi de 355 ms. No cenário A, a espera inclui também os até 100 ms do produtor aguardando vaga.
- **A × B (mais paralelismo):** ver 3.5.
- **A × F (lote, extensão):** gravar até 32 pedidos por transação reduziu as operações no banco de **2,0 para cerca de 0,06 por pedido**. O throughput subiu **68%** (913/s, igual nas duas rodadas), a espera p95 caiu de cerca de 555 ms para cerca de 340 ms e as rejeições caíram de cerca de 61% para cerca de 35%. Cada operação de lote é mais lenta (p99 de 17 a 40 ms), mas é feita uma vez a cada 32 pedidos. No SQLite, o custo dominante de cada escrita é confirmar a transação no disco, e o lote divide esse custo.

### 3.5 Por que aumentar o paralelismo pode piorar o banco

No cenário B, os consumidores foram de 4 para 16 e o limite do banco de 1 para 8. O resultado foi **instável**:

- **rodada 1:** 428/s (pior que A), com a operação de banco em **p99 = 301 ms**, contra 1,4 ms em A;
- **rodada 2:** 617/s, com p99 de 2,3 ms;
- na execução anterior do experimento, as rodadas deram 426/s (p99 de 155 ms) e 637/s.

O SQLite aceita um escritor por vez. Com 8 transações tentando escrever ao mesmo tempo, as que não conseguem o lock recebem `SQLITE_BUSY`, e o `Microsoft.Data.Sqlite` repete a tentativa **com espera**. Daí a cauda de centenas de milissegundos. Na média, B não ganhou throughput, piorou a cauda e ficou imprevisível. Em bancos com servidor, o mecanismo é parecido: mais conexões simultâneas significam mais disputa por locks e por I/O, e o pool se esgota para as outras rotas. **Escala-se o que é gargalo**. Aqui o gargalo era o custo por transação, e o lote (F) atacou exatamente isso.

### 3.6 Configuração escolhida

`Wait` com espera curta (200 ms), capacidade 500, 4 consumidores, `LimiteBanco = 1` e **lote de até 32**. Os motivos:

- é a única combinação **sem perda silenciosa**;
- tem latência na fila limitada a algumas centenas de milissegundos;
- quando sobrecarregada, avisa o cliente com 503 + `Retry-After`;
- e tem o melhor throughput medido.

O lote é *oportunista*: junta só o que **já está** na fila (`TryRead`, sem esperar). Com fila vazia, o comportamento é igual ao do processamento item a item.

## 4. Testes e validações obrigatórias

Resultado: **158 testes, 158 aprovados** (23 novos), sem falhas em execuções repetidas (6 seguidas antes da extensão de lote e 3 depois); lista em `docs/evidencias/atividade4/testes.txt`. Os testes usam uma "bancada" com a fila e o consumidor **reais** e um repositório falso que mede a concorrência por conta própria. Não há atrasos fixos: as esperas são por condição, com prazo.

| Validação exigida | Teste(s) |
|---|---|
| Modo `Wait`: produtor aguarda | `Modo_wait_faz_o_produtor_aguardar_ate_haver_espaco`; `Espera_esgotada_vira_rejeicao_explicita`; `Cancelamento_do_cliente_propaga_e_nao_conta_como_rejeicao` |
| Modos de descarte observados | `Modos_de_descarte_aceitam_tudo_e_descartam_conforme_a_politica` (DropOldest → restam 3,4,5; DropNewest → 1,2,5; DropWrite → 1,2,3) |
| Rejeição explícita: `TryWrite` e resposta HTTP | `Sem_espera_TryWrite_rejeita_imediatamente`; `Fila_cheia_responde_503_com_retry_after_sem_gravar` |
| Operações no banco nunca excedem o limite | `Concorrencia_no_banco_nunca_passa_do_limite` (8 consumidores, limite 3 → máximo **3**, medido pelo limitador e pelo próprio repositório); `Limitador_nao_libera_slot_que_nao_obteve`; `Limitador_libera_slot_mesmo_com_excecao` |
| Falha de um item não encerra os consumidores | `Falha_de_um_item_nao_derruba_o_consumidor_nem_perde_slot` |
| Cancelamento impede novos itens; estratégia para pendentes | `Encerramento_drenar_recusa_novos_e_processa_os_pendentes`; `Encerramento_abandonar_interrompe_e_contabiliza_os_pendentes`; `Prazo_de_drenagem_esgotado_nao_trava_o_encerramento`. Evidência real: SIGTERM com 480 itens na fila, todos os 2.998 aceitos gravados como `Concluido` |
| Nenhum pedido processado duas vezes | `Nenhum_pedido_e_processado_duas_vezes` (500 itens, 4 consumidores); `Lote_processa_todos_uma_vez...`; `Falha_no_lote_cai_para_item_a_item...` |
| Contadores corretos sob concorrência | `Metricas_por_categoria_sao_exatas_sob_concorrencia` (8 threads, 30.000 registros) |
| Item da fila imutável e sem EF | `Job_e_imutavel_e_nao_carrega_tipos_do_ef` |
| Fluxo completo HTTP → fila → banco | `Pedido_enfileirado_e_processado_e_gravado_como_concluido` |

Todos os testes do consumidor terminam verificando um **balanço**: aceitos = processados + falhas + abandonados + descartados + na fila + em andamento. Nenhum item fica sem destino conhecido.

### Aprimoramentos

1. **Processamento em lote, comparando throughput e latência** (extensão do roteiro): implementado com *fallback* item a item quando a transação do lote falha. Testado com repositório falso e com SQLite real (`Lote_com_item_invalido_nao_grava_nenhum` prova que a transação é desfeita por inteiro). Medido no cenário F: +68% de throughput.
2. Métricas publicadas em `System.Diagnostics.Metrics`, prontas para a Atividade 5.
3. *(Não realizados)* Filas por prioridade e idempotência persistida ficam como trabalho futuro. A idempotência também resolveria a perda de itens em memória quando o processo cai.

## 5. Referências

> Mantenha somente as fontes que você realmente consultou.

MICROSOFT. **System.Threading.Channels library**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/dotnet/core/extensions/channels. Acesso em: 9 nov. 2026.

MICROSOFT. **BoundedChannelFullMode Enum**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/dotnet/api/system.threading.channels.boundedchannelfullmode. Acesso em: 9 nov. 2026.

MICROSOFT. **SemaphoreSlim Class**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/dotnet/api/system.threading.semaphoreslim. Acesso em: 9 nov. 2026.

MICROSOFT. **ConcurrentDictionary&lt;TKey,TValue&gt;.AddOrUpdate**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/dotnet/api/system.collections.concurrent.concurrentdictionary-2.addorupdate. Acesso em: 9 nov. 2026.

MICROSOFT. **Background tasks with hosted services in ASP.NET Core**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/aspnet/core/fundamentals/host/hosted-services. Acesso em: 9 nov. 2026.

MICROSOFT. **Cancellation in managed threads**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/dotnet/standard/threading/cancellation-in-managed-threads. Acesso em: 9 nov. 2026.

MICROSOFT. **Microsoft.Data.Sqlite: database errors and busy handling**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/dotnet/standard/data/sqlite/database-errors. Acesso em: 9 nov. 2026.

LITTLE, J. D. C. A proof for the queuing formula: L = λW. **Operations Research**, v. 9, n. 3, p. 383-387, 1961.


```{=openxml}
<w:p><w:r><w:br w:type="page"/></w:r></w:p>
```

# Relatório 5 — BenchmarkDotNet, counters, traces, p95/p99 e testes de carga

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


```{=openxml}
<w:p><w:r><w:br w:type="page"/></w:r></w:p>
```

# Relatório 6 — Modularização, IoC, fronteiras e versionamento compatível de APIs

**Unidade 6 – Arquitetura aplicada**

**Objetivo:** separar responsabilidades do Hub e evoluir o contrato HTTP sem romper um cliente que depende da versão anterior.

## 1. Entrega e ambiente

- **Repositório:** `HubPedidos/`. Mudanças desta atividade:
  - novos assemblies `src/HubPedidos.Processamento` e `src/HubPedidos.Notificacoes`;
  - API reorganizada em `Modulos/` (Pedidos, Processamento, Notificações);
  - `Program.cs` reduzido a um *composition root* de 45 linhas;
  - documentação: `docs/arquitetura/modulos.svg` (diagrama), `docs/adr/0001` a `0004` (decisões) e `docs/api/versionamento.md` (política e matriz de mudanças).
- **Reproduzir:** `dotnet test` (185 testes). A API documenta cada versão em `/openapi/v1.json` e `/openapi/v2.json`.

| Item | Versão |
|---|---|
| SDK / runtime | .NET SDK 10.0.114 / .NET 10.0.14 |
| Pacotes novos | Microsoft.AspNetCore.OpenApi 10.0.14; Microsoft.Extensions.DependencyInjection.Abstractions e Configuration.Binder 10.0.14 (nos módulos) |
| Banco | SQLite 3.53.3 / EF Core 10.0.14 |
| IDE | Visual Studio 2026 versão ______ *(preencher)* |

## 2. Resumo teórico aplicado à atividade

**Módulo como fronteira.** Um módulo é uma fronteira de **responsabilidade**: tem dono, contratos públicos e dependências explícitas. Organizar por pasta não é o mesmo que controlar a dependência. Duas pastas no mesmo projeto podem se referenciar livremente, e a fronteira passa a depender de disciplina. Aqui, cada módulo é um **assembly**: o compilador não deixa usar um tipo de outro módulo sem referência explícita, e um teste impede adicionar referências proibidas.

**Inversão de controle e composition root.** As classes declaram o que precisam no construtor, e um único ponto (o `Program.cs`) decide qual implementação usar. Dois riscos aparecem nesse modelo:

- **Service locator:** pedir serviços a um `IServiceProvider` dentro da regra de negócio. Isso esconde dependências e dificulta testes.
- **Dependência cativa:** um singleton que recebe um serviço scoped no construtor. O serviço scoped (por exemplo, o `DbContext`) passa a viver para sempre e a ser compartilhado entre requisições e threads.

Os **tempos de vida** são: *singleton* (uma instância por aplicação), *scoped* (uma por requisição ou escopo) e *transient* (uma por resolução).

**Boundaries, contratos internos e eventos.** Entre módulos circulam **contratos** (DTOs, interfaces e eventos com dados mínimos), nunca entidades nem detalhes de persistência. A interface fica no módulo que **consome** o serviço (porta) e a implementação fica fora (adaptador). Assim, quem consome não depende de quem implementa.

**Mudanças compatíveis e incompatíveis.**

- **Compatível:** adicionar um campo opcional, um endpoint novo ou um valor tolerado pelo cliente.
- **Incompatível:** remover ou renomear um campo, mudar seu **tipo**, mudar o **significado** de um valor, mudar valores de enum, mudar o status HTTP ou uma ordenação documentada.

Uma mudança de semântica sem mudança de formato, como "total" passar a incluir frete, é a mais perigosa, porque o cliente continua funcionando com o valor errado.

**Estratégias de versionamento.** A versão pode ir na URL (`/v2/`), num cabeçalho (`api-version`), na query string ou no *media type*. É preciso definir uma **política**: qual versão vale quando nenhuma é informada, o que acontece com uma versão inexistente, como avisar a depreciação (cabeçalhos `Deprecation`, RFC 9745, e `Sunset`, RFC 8594) e quando remover.

**Compatibilidade para front-ends.** O contrato publicado é uma promessa. As versões **coexistem** por um período, o **uso de cada versão é medido** (telemetria) e a remoção é **planejada** com critérios objetivos, e não por data arbitrária.

## 3. Código-fonte, decisões e evidências

### 3.1 Módulos e direção das dependências (Etapa 1)

![Figura 1: módulos (assemblies) e direção das dependências (`docs/arquitetura/modulos.svg`).](../HubPedidos/docs/arquitetura/modulos.png)

| Módulo (assembly) | Responsabilidade | Pode depender de |
|---|---|---|
| `Domain` | entidade e *value objects* | nada |
| **Pedidos** (`Application`) | contratos v1/v2, mapeador, classificação, importação, **portas** `IRepositorioPedidos`/`IConsultasPedidos` | Domain |
| **Processamento** | processadores, catálogo, fila, consumidor, seletor, **porta** `INotificadorPedidos` | Pedidos, Domain |
| **Notificações** | `RegistroNotificacoes` (adaptador da porta) | Processamento, Pedidos, Domain |
| `Infrastructure` | EF Core/SQLite: implementa as portas de Pedidos | Pedidos, Domain |
| `Api` | *composition root* e endpoints HTTP | todos |

**Vazamento corrigido.** `MapeadorPedidos.ParaResposta` (módulo Pedidos) dependia de `ResultadoProcessamento` (módulo Processamento), uma dependência "para cima". O mapeamento passou para o Processamento (`MapeadorResultado`), junto com os DTOs que só fazem sentido lá: `PedidoEnfileiradoResponse` e `MetricasFilaResponse`.

**Portas movidas para quem consome.** `INotificadorPedidos` é declarada no Processamento, porque é ele que avisa que algo mudou, e é implementada em Notificações. Sem o módulo de notificações, vale o `NotificadorNulo` (registrado com `TryAdd`). Na Atividade 7, o SignalR entra **dentro** do módulo de Notificações sem mexer no processamento.

**A direção é verificável no projeto, e não só no diagrama:**

1. **Compilador.** Uma tentativa de usar `NotificadorNulo` dentro do módulo Pedidos falhou com `error CS0234: The type or namespace name 'Processamento' does not exist in the namespace 'HubPedidos'` (`fronteiras-verificaveis.txt`).
2. **Testes arquiteturais entre assemblies** (`ArquiteturaTests`, extensão do roteiro). A matriz acima é verificada em dois níveis: nas `<ProjectReference>` de cada `.csproj` (o que o módulo **pode** usar) e nas referências gravadas no assembly compilado (o que ele **realmente** usa). Um terceiro teste confere que cada porta é declarada no módulo consumidor e implementada só no módulo esperado. Também é verificado que EF Core, SQLite e ASP.NET não aparecem nos módulos Domain, Pedidos, Processamento e Notificações.

**O teste já pegou uma violação real.** Durante a reorganização, um `dotnet remove reference` executado com o caminho errado falhou em silêncio, e a Infrastructure continuou referenciando o Processamento. O build passava; o teste falhou com `HubPedidos.Infrastructure.csproj referencia HubPedidos.Processamento (proibido pela matriz)`. Reproduzi isso de propósito para registrar a evidência.

**Decisões registradas** em ADRs curtos, cada um com as alternativas descartadas:

- **0001:** módulos como assemblies. Descartados: pastas com teste por reflexão, que não enxerga o corpo dos métodos; e microsserviços.
- **0002:** portas declaradas por quem consome. Descartado: um projeto "Contracts" compartilhado por todos.
- **0003:** versão no segmento de URL. Descartados: cabeçalho, query string, *media type* e a biblioteca Asp.Versioning.
- **0004:** seletor de processador como único ponto de resolução por chave.

### 3.2 IoC organizado (Etapa 2)

Cada módulo tem a própria extensão de registro, e o `Program.cs` só compõe:

```csharp
builder.Host.UseDefaultServiceProvider(o => { o.ValidateOnBuild = true; o.ValidateScopes = true; });
builder.Services
    .AddModuloPedidos(builder.Configuration)
    .AddModuloProcessamento(builder.Configuration)
    .AddModuloNotificacoes()
    .AddInfraestrutura(builder.Configuration)
    .AddProblemDetails();
```

| Serviço | Tempo de vida | Justificativa |
|---|---|---|
| `CatalogoProcessadores`, `OpcoesFila`, `OpcoesImportacao`, `OpcoesEscritaHttp` | Singleton | imutáveis, montados uma vez na inicialização |
| `FilaPedidos`, `LimitadorBanco`, `MetricasFila` | Singleton | estado **compartilhado** de propósito: o teto de escrita no banco precisa ser global (Atividade 5) |
| `RegistroNotificacoes` / `INotificadorPedidos` | Singleton (a mesma instância nas duas interfaces) | guarda as notificações recentes de todas as requisições |
| `HubDbContext`, `RepositorioPedidos`, `ConsultasPedidos` | Scoped | o `DbContext` não é thread-safe; uma unidade de trabalho por requisição ou item da fila |
| Processadores (keyed) e `ISeletorProcessador` | Scoped | podem depender de serviços scoped; o seletor resolve no escopo de quem o recebeu |
| `ConsumidorPedidos` (hosted) | Singleton | cria **um escopo por item** com `IServiceScopeFactory`, em vez de capturar serviços scoped |

**Service locator removido da regra.** O endpoint de criação e o consumidor pediam `GetRequiredKeyedService<IProcessadorPedido>(categoria)` ao `IServiceProvider`. Agora recebem `ISeletorProcessador` por injeção. A resolução por chave dinâmica ficou confinada nessa única classe (`SeletorProcessadorKeyed`, ADR 0004). O endpoint também deixou de ler `IConfiguration` a cada requisição: recebe `OpcoesEscritaHttp`, montado uma vez.

**Validação do grafo** (`ComposicaoTests`):

- **Inicialização:** o host é construído com `ValidateOnBuild` e `ValidateScopes`. Um grafo inválido impede a aplicação de subir.
- **Analisador de dependência cativa.** Percorre o `IServiceCollection` final da aplicação e verifica, no construtor de cada singleton, se algum parâmetro está registrado como scoped. Resultado na aplicação: **nenhuma violação**. Um teste negativo prova que o analisador e o contêiner detectam o problema: um singleton `Cativo` recebendo um scoped `Escopado` produz `Cativo (singleton) recebe Escopado (scoped)` no analisador e `Cannot consume scoped service` no contêiner.
- **Fluxos principais:** cada categoria do catálogo resolve para o tipo declarado no atributo; as portas resolvem para as implementações da infraestrutura; scoped é a mesma instância dentro do escopo e diferente entre escopos; o módulo de notificações substitui o `NotificadorNulo`, e sem ele vale o nulo.

### 3.3 Política de versão (Etapa 3)

- **Endpoint escolhido:** `GET /pedidos/{id}` (detalhe).
- **Consumidores conhecidos:** o painel web (v1), o cliente de teste congelado (v1) e novos integradores, como ERP e aplicativo (v2).

| Item | Regra adotada |
|---|---|
| Leitura da versão | segmento de URL: `/api/v1/pedidos/{id}`, `/api/v2/pedidos/{id}` |
| Versão padrão | a rota legada `/pedidos/{id}` continua sendo **v1** |
| Versão não suportada | `GET /api/v3/...` → **400** `application/problem+json` com `"versoesSuportadas":[1,2]` e cabeçalho `api-supported-versions: 1, 2` |
| Depreciação | a v1 responde `Deprecation: @1794441600` (2026-11-12), `Sunset: Mon, 31 May 2027 00:00:00 GMT` e `Link: </api/v2/pedidos>; rel="successor-version"` |
| Documentação | um documento OpenAPI por versão (`AddOpenApi("v1")`/`("v2")` filtrando por `WithGroupName`). Evidência: o v1 lista só `/api/v1/pedidos/{id}` e o v2 só `/api/v2/pedidos/{id}`, com o schema `DinheiroV2` |

A rota de versão desconhecida usa `/api/v{versao:int}/...`. Os grupos literais `v1` e `v2` têm precedência no roteamento, então só versões inexistentes chegam ao handler de erro.

**Mudança incompatível deliberada da v2** (`docs/api/versionamento.md`):

| Campo | v1 | v2 | Compatível? |
|---|---|---|---|
| `total`, `itens[].precoUnitario`, `itens[].subtotal` | número JSON (`3000000.07`) | `{ "valor": "3000000.07", "moeda": "BRL" }` | **não** (tipo muda) |
| `criadoEmUtc` | `"2026-11-12T00:59:10.02"` (sem fuso) | `criadoEm`: `"2026-11-12T00:59:10.02+00:00"` | **não** (nome e formato) |
| demais campos, status HTTP, ordenação dos itens | — | iguais | sim |

**Motivos da mudança:**

- Um número JSON vira `double` em JavaScript e perde precisão em valores grandes. Por isso o valor vai como string.
- Uma data sem fuso é interpretada de maneiras diferentes por cada cliente.

A v2 é montada a partir do **mesmo resultado de consulta** da v1, por um adaptador (`AdaptadorPedidoV2`). Não há consulta extra, e a v1 nunca é alterada para servir à v2.

### 3.4 Compatibilidade provada (Etapa 4)

**Cliente v1 congelado** (`ClientePainelV1`). Representa o front-end e tem os **próprios tipos**, copiados do contrato publicado. Ele não reaproveita os DTOs do servidor: se o servidor mudar a v1, o cliente quebra, como quebraria um cliente real. A desserialização é **estrita** (`UnmappedMemberHandling.Disallow`): qualquer campo novo, removido ou renomeado na v1 faz o teste falhar.

Testes executados contra a aplicação que também contém a v2:

- **Formato:** o cliente v1 lê `/api/v1/pedidos/{id}` e a rota legada `/pedidos/{id}`.
- **Semântica, e não só formato:**
  - o total é a soma dos subtotais;
  - cada subtotal é quantidade × preço;
  - R$ 3.000.000,07 chega exato e é formatado como `"3.000.000,07"`;
  - `criadoEmUtc` é de fato UTC;
  - o status pertence ao conjunto conhecido.
- **Snapshot do contrato v1:** nomes e tipos JSON de cada campo de topo e de cada item batem com a lista publicada.
- **A v2 quebraria o cliente v1**, o que justifica manter a v1: apontado para `/api/v2`, o cliente v1 lança `JsonException`.

**Ciclo de vida da v1** (`docs/api/versionamento.md`). O uso por versão é contado pelo contador `hub.api.requisicoes_versionadas` (meter `HubPedidos.Api`) e exposto em `GET /api/versoes`. Na evidência: `"uso":{"v1":2,"v2":1}`. Critérios para remover a v1:

1. uso abaixo de 1% das consultas por 30 dias seguidos e nenhum consumidor conhecido ainda na v1;
2. a data de sunset (2027-05-31) foi atingida;
3. os consumidores foram avisados com 60 dias de antecedência.

Depois disso, `/api/v1` passa a responder 400 (versão não suportada) e a rota legada responde 410 por mais um ciclo antes de ser removida.

## 4. Testes e validações obrigatórias

Resultado: **185 testes, 185 aprovados** (26 novos), em execuções repetidas; lista em `docs/evidencias/atividade6/testes.txt`.

| Validação exigida | Teste(s) |
|---|---|
| Domínio compila sem referência à API ou ao EF Core | `Assembly_compilado_so_usa_dependencias_permitidas(HubPedidos.Domain)`; `Dominio_continua_sem_dependencias`; mais a matriz completa para os 6 assemblies, nos `.csproj` e nos binários (12 casos) |
| Nenhum serviço scoped capturado por singleton | `Grafo_da_aplicacao_nao_tem_dependencia_cativa`; `Analisador_e_o_conteiner_detectam_uma_dependencia_cativa_proposital` |
| v1 e v2 coexistem com contratos documentados | `Documentacao_openapi_e_separada_por_versao`; `V2_tem_a_mudanca_incompativel_documentada...`; `V1_anuncia_depreciacao_e_sucessora_e_v2_nao` |
| Cliente v1 interpreta a resposta sem mudança silenciosa de semântica | `Cliente_v1_continua_funcionando_na_aplicacao_que_tem_v2` (×2: `/api/v1` e rota legada); `Contrato_v1_e_identico_ao_publicado` |
| Versão ausente ou não suportada segue política explícita | rota legada = v1 (teste acima); `Versao_nao_suportada_segue_a_politica_explicita`; `Uso_por_versao_e_observavel` |
| Grafo de DI e fluxos | `Fluxos_principais_sao_resolvidos_com_os_tempos_de_vida_esperados`; `Sem_o_modulo_de_notificacoes_o_processamento_usa_o_notificador_nulo` |
| Notificações (novo módulo) | `Cada_pedido_concluido_gera_uma_notificacao_e_falha_no_notificador_nao_desfaz_o_processamento`; `Conclusao_pela_fila_aparece_nas_notificacoes_recentes` |

**Um erro meu que o domínio pegou.** No primeiro teste de versão, usei um preço de R$ 1.234.567,89, acima do limite de R$ 1.000.000 imposto pelo `ItemPedido` desde a Atividade 1. A API recusou com 400, corretamente. O teste foi corrigido para valores válidos e altos (R$ 999.999,99 × 3).

### Aprimoramentos

1. **Teste arquitetural automatizado entre assemblies** (extensão do roteiro), em dois níveis (`.csproj` e assembly compilado), com verificação de portas e de tecnologia proibida. **Já detectou uma violação real** durante esta atividade.
2. **Adaptador entre duas representações de pedido** (`AdaptadorPedidoV2`): traduz a representação v1 para a v2 sem alterar a v1, deixando as duas isoladas. Cobre parcialmente a extensão "adaptador anticorrupção".
3. *(Não realizado)* Geração de clientes v1 e v2 (NSwag/Kiota) e comparação das diferenças de compilação.

## 5. Referências

> Mantenha somente as fontes que você realmente consultou.

MICROSOFT. **Dependency injection in .NET: service lifetimes**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/dotnet/core/extensions/dependency-injection. Acesso em: 12 nov. 2026.

MICROSOFT. **Dependency injection guidelines (captive dependency, service locator)**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/dotnet/core/extensions/dependency-injection-guidelines. Acesso em: 12 nov. 2026.

MICROSOFT. **Generate OpenAPI documents (Microsoft.AspNetCore.OpenApi)**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/aspnet/core/fundamentals/openapi/aspnetcore-openapi. Acesso em: 12 nov. 2026.

MICROSOFT. **Route groups and endpoint filters in Minimal APIs**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis/route-handlers. Acesso em: 12 nov. 2026.

IETF. **RFC 9745: The Deprecation HTTP Response Header Field**. 2025. Disponível em: https://www.rfc-editor.org/rfc/rfc9745. Acesso em: 12 nov. 2026.

IETF. **RFC 8594: The Sunset HTTP Header Field**. 2019. Disponível em: https://www.rfc-editor.org/rfc/rfc8594. Acesso em: 12 nov. 2026.

MICROSOFT. **Web API design best practices: versioning a RESTful web API**. Microsoft Learn (Azure Architecture Center), 2025. Disponível em: https://learn.microsoft.com/azure/architecture/best-practices/api-design. Acesso em: 12 nov. 2026.


```{=openxml}
<w:p><w:r><w:br w:type="page"/></w:r></w:p>
```

# Relatório 7 — SignalR, reconexão, streaming, cache de resposta e ETags

**Unidade 7 – Tempo real e UX**

**Objetivo:** notificar o cliente sobre o processamento de pedidos e reduzir transferências de leitura com cache e validação condicional corretos.

## 1. Entrega e ambiente

- **Repositório:** `HubPedidos/`. Itens novos desta atividade:

| Onde | O quê |
|---|---|
| `src/HubPedidos.Notificacoes/TempoReal/` | hub, autorização por pedido, registro de conexões e revogação |
| `src/HubPedidos.Notificacoes/Canais.cs` | notificador composto e canais |
| `src/HubPedidos.Api/Seguranca/` | autenticação JWT |
| `src/HubPedidos.Api/Http/` | ETag e output cache |
| `clientes/HubPedidos.ClienteTempoReal/` | cliente .NET com reconexão |
| `tests/.../TempoReal/` | testes do hub, incluindo o proxy de falhas |
| `tests/.../Http/` | testes de ETag e cache |

- **Reproduzir:**
  - `dotnet test` (208 testes);
  - `URL_CONSULTA=/painel/regioes carga/medir.sh com-cache 20 20 saida/` e o mesmo comando com `Cache__Habilitado=false`, para o efeito do cache.

| Item | Versão |
|---|---|
| SDK / runtime | .NET SDK 10.0.114 / ASP.NET Core 10.0.14 (SignalR e OutputCaching do framework) |
| Pacotes | Microsoft.AspNetCore.Authentication.JwtBearer 10.0.14; Microsoft.AspNetCore.SignalR.Client 10.0.14 |
| Banco | SQLite 3.53.3 / EF Core 10.0.14 |
| IDE | Visual Studio 2026 versão ______ *(preencher)* |

**Segredos.** A chave de assinatura dos tokens vem de `Autenticacao:ChaveAssinatura`, por variável de ambiente ou cofre, e nunca do repositório. Em Development, sem chave configurada, a API gera uma aleatória por execução e oferece `POST /dev/token` para obter tokens de teste; essa rota não existe fora de Development. Em Production, sem chave, **a API não sobe**. O próprio script de carga esbarrou nisso (`InvalidOperationException: Configure Autenticacao:ChaveAssinatura`) e passou a gerar uma chave aleatória a cada execução.

## 2. Resumo teórico aplicado à atividade

**SignalR.** É uma abstração de comunicação em tempo real. Ele escolhe o transporte (WebSocket, Server-Sent Events ou long polling) e expõe chamadas de método nos dois sentidos:

- **Hub:** a classe no servidor. Uma instância é criada por invocação.
- **Conexão:** um cliente conectado, com `ConnectionId` próprio.
- **Grupo:** um conjunto nomeado de conexões, que serve para endereçar mensagens.
- **Método de cliente:** o que o servidor chama do outro lado. Aqui é tipado pela interface `IClientePedidos`, então não há strings mágicas.

**Grupo não é segurança.** Ele só endereça mensagens. Quem decide quem entra é o servidor, antes do `AddToGroupAsync`.

**Notificação × fonte da verdade.** Uma notificação pode se perder: numa queda, durante uma reconexão ou com o processo reiniciando. Pode chegar duplicada (uma conexão em dois grupos) ou fora de ordem. O SignalR **não faz *replay*** do que foi perdido. Por isso o evento é só um **aviso** com dados mínimos, e o estado de verdade vem de uma consulta **HTTP autoritativa**. O cliente descarta duplicatas pela sequência e, depois de reconectar, **reconcilia** consultando o estado atual.

**Estados da conexão e reconexão.** O cliente passa por *Conectando → Conectado → Reconectando → Conectado/Desconectado*. A reconexão automática usa *backoff*, com intervalos crescentes e um teto. Depois de reconectar, a conexão é **nova**, com outro `ConnectionId` e sem grupos. O cliente precisa pedir de novo, e o servidor precisa **revalidar a autorização**, porque a permissão pode ter sido revogada no meio do caminho. O usuário deve ver o estado, e as ações que dependem da conexão ficam desabilitadas.

**Cache de resposta × cache de dados.**

- O **output cache** guarda a resposta HTTP pronta no servidor. A próxima requisição igual nem chega ao handler.
- O **cache de dados** (`IMemoryCache`, Redis) guarda objetos que o código ainda processa.

O risco do primeiro é servir a um usuário a resposta montada para outro. Por isso só leituras **públicas e iguais para todos** podem ir para o output cache. O cache do ASP.NET também não guarda requisições com `Authorization` ou cookie, e precisa entrar **depois** de `UseAuthentication`/`UseAuthorization`.

**ETag e pré-condições.** A ETag identifica uma **representação**: os bytes enviados para um recurso numa versão. As pré-condições usam essa identificação:

- **`If-None-Match`:** "só me mande se mudou". Se a ETag ainda vale, a resposta é **304 sem corpo**. Usa comparação **fraca** (RFC 9110 §13.1.2).
- **`If-Match`:** "só grave se ninguém mudou desde que eu li". Se a ETag não vale mais, a resposta é **412** e nada é gravado. Usa comparação **forte**.

**Invalidação e coerência.** Cache por tempo é simples, mas fica velho. Cache invalidado por **tag** quando o dado muda é mais coerente, mas só vale para mudanças que passam pela aplicação, **nesta instância**. Também é preciso coerência entre cache, versão da API e tempo real: cada versão tem sua representação (logo sua ETag), e o mesmo evento que vai pelo SignalR também invalida o cache.

## 3. Código-fonte, decisões e evidências

### 3.1 Hub, grupos e evento tipado (Etapa 1)

```csharp
public sealed record PedidoAtualizadoMensagem(Guid PedidoId, string Status, long Sequencia, DateTimeOffset Em);

public interface IClientePedidos
{
    Task PedidoAtualizado(PedidoAtualizadoMensagem mensagem);
    Task AcessoRevogado(AcessoRevogadoMensagem mensagem);
}

[Authorize]
public sealed class PedidosHub(AutorizadorPedidos autorizador, RegistroConexoes conexoes, RegistroRevogacoes revogacoes) : Hub<IClientePedidos>
{
    public async Task AssinarPedido(Guid pedidoId)
    {
        if (!await autorizador.PodeVerAsync(Context.User, pedidoId, Context.ConnectionAborted))
            throw new HubException("Acesso negado ao pedido."); // mesma mensagem p/ inexistente e alheio
        await Groups.AddToGroupAsync(Context.ConnectionId, Grupos.Pedido(pedidoId));
        ...
```

**Decisões:**

- **`[Authorize]` no hub.** Sem token válido, a própria negociação recebe **401** (evidência e teste). O token chega pelo cabeçalho `Authorization` ou, só na rota `/hubs`, pela query `access_token`, porque WebSocket no navegador não envia cabeçalhos.
- **Autorização antes do grupo.** O `AutorizadorPedidos` é a **mesma regra** usada pela consulta HTTP v2: operador vê tudo, cliente só vê os próprios pedidos (`cliente_id` do token igual ao dono do pedido), cliente revogado não vê nada. Pedido alheio e inexistente dão a **mesma resposta**, para não revelar quais ids existem.
- **Publicação depois de persistir.** Quem chama `INotificadorPedidos` já gravou a mudança: o consumidor da fila (Atividade 4), o `POST /pedidos` e o `PUT /pedidos/{id}/status`. Um teste confirma que, no instante em que o evento chega, a consulta HTTP já mostra o novo status.
- **Dados mínimos.** O evento leva id, status, sequência e instante. Não leva o objeto de domínio nem dados do cliente.
- **Canal SignalR dentro de um notificador composto.** O módulo de Notificações implementa a porta da Atividade 6 com um `NotificadorComposto`, que entrega o evento a cada `ICanalNotificacao`: memória, SignalR e invalidação de cache (este último implementado pela API). A falha de um canal não impede os outros nem desfaz a mudança. O processamento **não mudou nada** para ganhar o tempo real, como previsto no ADR 0002.
- **Regra arquitetural ajustada de forma explícita.** O módulo de Notificações passou a usar ASP.NET Core (SignalR), como exceção declarada no teste arquitetural. EF Core continua proibido nele.

### 3.2 Cliente com reconexão e reconciliação (Etapa 2)

`ClientePedidosTempoReal` (`clientes/HubPedidos.ClienteTempoReal`) representa o front-end. Ele tem os próprios tipos e não referencia o servidor.

| Situação | Comportamento |
|---|---|
| Conexão perdida | estado **Reconectando**, `AcoesHabilitadas = false`, mensagem: *"Conexão perdida. Tentando reconectar… As ações estão desabilitadas até voltar."* |
| Política de retentativa | 0 s, 0,25 s, 0,5 s, 1 s, 2 s, 5 s e então 10 s, até 2 minutos; depois disso, **Desconectado**: *"Sem conexão com o servidor. Recarregue a página…"* |
| Reconectou | para cada pedido assinado, chama `AssinarPedido` de novo (o **servidor revalida**; se a permissão foi revogada, o pedido sai da lista local) e **reconcilia por HTTP** (`GET /api/v2/pedidos/{id}` com `If-None-Match`: 304 se nada mudou, 200 com o estado atual se mudou) |
| Evento duplicado ou atrasado | descartado se a sequência for menor ou igual à última vista para aquele pedido |
| Acesso revogado | o servidor tira a conexão dos grupos e envia `AcessoRevogado`; o cliente limpa as assinaturas |

**Teste de reconexão com proxy de falha controlado** (extensão do roteiro). A API sobe em **Kestrel real**, usando o `WebApplicationFactory.UseKestrel` do .NET 10, com um **proxy TCP** entre o cliente e o servidor. O proxy derruba todas as conexões com RST e recusa as novas, simulando uma queda de rede de verdade, inclusive no WebSocket. Roteiro do teste:

1. O cliente assina um pedido (status local: `Recebido`).
2. Queda de rede. O estado vai para **Reconectando** e as ações ficam **desabilitadas**.
3. Durante a queda, o pedido muda para `EmProcessamento`. O evento é **perdido**, porque não há replay.
4. A rede volta. O cliente reconecta, assina de novo e **reconcilia por HTTP**. O estado local passa a `EmProcessamento` e é disparado o evento `Reconciliado`.
5. Uma nova mudança (`Concluido`) **chega pelo hub**, o que prova que a reassinatura funcionou.

O teste passou em 3 execuções seguidas. A primeira versão do proxy falhava com `ObjectDisposedException`, ao configurar o RST num socket que a bomba de cópia já tinha fechado, e foi corrigida.

**Revogação.** `POST /admin/clientes/{id}/revogar` exige o papel operador (cliente recebe 403). A revogação:

- marca o cliente como revogado;
- tira todas as conexões dele de todos os grupos;
- envia `AcessoRevogado`.

Depois disso, eventos daquele pedido não chegam mais, um novo `AssinarPedido` é negado e uma reconexão é encerrada em `OnConnectedAsync`.

### 3.3 ETag (Etapa 3)

A ETag é **forte**: os primeiros 128 bits do SHA-256 dos **bytes JSON exatamente como são enviados**, em base64url. Ela é aplicada ao detalhe na v1, na v2 e na rota legada.

- **Campos que compõem a representação:** todos os do DTO daquela versão. Mesmos valores geram os mesmos bytes, logo a mesma ETag. Mudou o status (ou qualquer campo), mudou a ETag.
- **Cada versão tem a própria ETag**, porque a v1 e a v2 são representações diferentes. A rota legada tem a mesma ETag da v1.
- **`Cache-Control: private, no-cache`:** o navegador pode guardar, mas revalida sempre, e proxies compartilhados não guardam, porque a v2 é de um usuário específico.

Evidência (`http-tempo-real-etag-cache.txt`):

```
GET /api/v2/pedidos/{id}                        → 200  ETag: "r20c1NDQoVfg4YmexkfQ8w"  (430 bytes)
GET ... If-None-Match: "r20c1NDQoVfg4YmexkfQ8w" → 304  sem corpo, mesma ETag
PUT /pedidos/{id}/status If-Match: (atual)       → 204
GET ... If-None-Match: (antiga)                  → 200  ETag: "U1ogeS8gpLMQgqMCoxoo9w"  status EmProcessamento
PUT ... If-Match: (antiga)                       → 412  "O pedido mudou desde a sua última leitura…"
```

O tratamento segue a RFC 9110:

- **`If-None-Match`:** comparação fraca. `W/"x"` casa com `"x"`, `*` casa com qualquer representação e listas são aceitas.
- **`If-Match`:** comparação forte.
- **Valor malformado ou desconhecido:** a resposta é normal (200 com corpo), e não um erro.

### 3.4 Output cache com limites (Etapa 4)

**Leituras escolhidas** (públicas e iguais para todos os usuários):

| Rota | Duração | Tag | Por quê |
|---|---|---|---|
| `GET /processadores` | 5 min | `catalogo` | muda só com deploy |
| `GET /painel/regioes` | 30 s | `painel`, **invalidada** a cada mudança de pedido | agregação cara (cerca de 65 ms) e lida com frequência |

**Rotas excluídas de propósito:**

- detalhe e listagem de pedido: são de um cliente; usam ETag no lugar do cache;
- métricas da fila e notificações: precisam estar atuais;
- toda requisição com `Authorization` ou cookie: a política base do ASP.NET não guarda nem serve do cache.

A ordem dos middlewares é `UseAuthentication()` → `UseAuthorization()` → `UseOutputCache()`. A invalidação é feita pelo `CanalInvalidacaoCache` (`EvictByTagAsync("painel")`), um canal do mesmo notificador que alimenta o SignalR.

**Medição** (gerador da Atividade 5, modelo aberto, API em Release, banco com 20 mil pedidos; `carga-output-cache.md`). As respostas vindas do cache foram identificadas pelo cabeçalho `Age`.

| Cenário (painel/s + criações/s) | Painel p50 | p95 | p99 | Criação p99 | Vindas do cache* |
|---|---:|---:|---:|---:|---:|
| Sem cache, 20 + 20 | 64,8 ms | 87,8 ms | 90,9 ms | 15,8 ms | 0% |
| Com cache, 20 + 20 (invalidação a cada criação) | **11,2 ms** | 75,9 ms | 85,4 ms | 7,9 ms | cerca de 66% |
| Com cache, 200 + 0 (sem invalidação) | **2,2 ms** | **3,8 ms** | **5,4 ms** | — | cerca de 100% |
| Sem cache, 200 + 20 | **colapso**: 5.995 timeouts no painel e 599 na criação | | | | 0% |
| Com cache, 200 + 20 | 16,9 ms | 65,1 ms | 80,9 ms | 7,9 ms | cerca de 95% |

\* Respostas com `Age` sobre o total enviado, incluindo o aquecimento.

**Leitura:**

- O cache **reduz a mediana** de 65 ms para 2 a 17 ms, e é o que **permite** atender 200 leituras/s do painel. Sem ele, a mesma carga derruba a API inteira, inclusive as criações.
- Com invalidação a cada escrita, o **p95 continua perto do custo de uma leitura sem cache** (76 ms contra 88 ms): toda leitura logo depois de uma invalidação paga a consulta inteira. O ganho é proporcional à razão entre leituras e escritas.
- Para reduzir a cauda seria preciso **aceitar dados um pouco desatualizados**: não invalidar por tag e deixar expirar em poucos segundos. **Decisão:** manter a invalidação por tag, porque o painel é usado por operadores para acompanhar mudanças. Se a latência da cauda virar problema, reavaliar com uma janela de 5 s.

**Risco de inconsistência, demonstrado por teste.** A invalidação só vale para mudanças que passam pelo notificador **desta instância**. Uma alteração direta no banco (ou feita por outra instância da API) não invalida nada, e o painel fica desatualizado até expirar (30 s). Com várias instâncias, seriam necessários um cache distribuído (por exemplo, Redis) e a invalidação propagada a todas.

## 4. Testes e validações obrigatórias

Resultado: **208 testes, 208 aprovados** (23 novos), em 3 execuções seguidas; lista em `docs/evidencias/atividade7/testes.txt`.

| Validação exigida | Teste(s) |
|---|---|
| Cliente recebe atualização só depois da autorização validada no servidor; grupo não substitui autorização | `Sem_token_a_negociacao_e_recusada` (401); `Cliente_so_assina_e_recebe_os_proprios_pedidos` (pedido alheio e inexistente → `HubException`; o cliente A não recebe o evento de B, que o operador recebe); `Evento_chega_depois_da_persistencia_e_o_http_confirma` |
| Reconexão revalida a autorização, restaura a assinatura e reconcilia eventos perdidos | `Reconexao_reassina_e_reconcilia_eventos_perdidos_pela_queda` (proxy de falhas); `Revogacao_tira_dos_grupos_avisa_e_impede_reassinar`; `Revogar_exige_operador`; `Duplicatas_e_eventos_fora_de_ordem_sao_descartados` |
| ETag muda quando a representação muda e fica estável quando não muda | `Etag_e_estavel_quando_nada_muda_e_304_vem_sem_corpo` (v1 e v2); `Etag_muda_quando_a_representacao_muda...`; `Cada_versao_tem_a_propria_etag_e_a_rota_legada_e_igual_a_v1` |
| `If-None-Match` válido → 304 sem corpo; obsoleto → representação atual | os testes acima; `If_none_match_invalido_ou_desconhecido_devolve_a_representacao` (×3); `Comparacao_fraca_e_asterisco_e_lista_dao_304` |
| Output cache depois de autenticação/autorização e sem compartilhar respostas privadas | `Requisicao_autenticada_nao_e_guardada_nem_servida_do_cache`; `Detalhe_de_pedido_nunca_e_compartilhado_entre_usuarios`; `Segunda_leitura_vem_do_cache_sem_tocar_no_banco`; `Mudanca_de_pedido_invalida_pela_tag`; `Mudanca_fora_do_fluxo_da_aplicacao_fica_invisivel_ate_expirar`; `Cache_pode_ser_desligado_por_configuracao` |
| Autorização da v2 | `V2_exige_identidade_e_esconde_pedido_alheio` (401 anônimo, 404 alheio ou inexistente, 200 dono e operador) |

### Aprimoramentos (extensões do roteiro)

1. **`If-Match` para atualização otimista, com 412** (`If_match_atual_permite_e_obsoleto_da_412_sem_gravar`). Sem `If-Match`, o comportamento antigo é mantido por compatibilidade. Limitação: verificar e gravar são dois passos, então uma mudança exatamente entre eles não é detectada; o repositório ainda valida a transição de status.
2. **Teste de reconexão com proxy de falha controlado**, com Kestrel real e queda TCP abrupta (seção 3.2).
3. *(Não realizado)* Streaming de progresso com cancelamento (`IAsyncEnumerable` no hub).

### Limitações conhecidas

- A **sequência** dos eventos é por processo e reinicia junto com a aplicação. O cliente zera a sequência ao reconciliar por HTTP, que é a fonte da verdade.
- **Revogações, conexões e output cache ficam na memória de uma instância.** Escalar exige um *backplane* do SignalR (Redis ou Azure SignalR) e armazenamento compartilhado.
- O **token na query string** (exigência do WebSocket no navegador) pode aparecer em logs de acesso. A mitigação é usar tokens de vida curta e não registrar a query da rota `/hubs`.
- A **v1 continua anônima** até o sunset, por compatibilidade. Esse risco foi aceito e registrado.
- O cliente foi implementado em .NET, como modelo do contrato de UX (estados, mensagens, ações habilitadas). Um front-end em JavaScript seguiria o mesmo padrão (`withAutomaticReconnect`, `onreconnecting`, `onreconnected`).

## 5. Referências

> Mantenha somente as fontes que você realmente consultou.

MICROSOFT. **Overview of ASP.NET Core SignalR**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/aspnet/core/signalr/introduction. Acesso em: 13 nov. 2026.

MICROSOFT. **Authentication and authorization in ASP.NET Core SignalR**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/aspnet/core/signalr/authn-and-authz. Acesso em: 13 nov. 2026.

MICROSOFT. **ASP.NET Core SignalR .NET Client: automatic reconnect**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/aspnet/core/signalr/dotnet-client. Acesso em: 13 nov. 2026.

MICROSOFT. **Output caching middleware in ASP.NET Core**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/aspnet/core/performance/caching/output. Acesso em: 13 nov. 2026.

MICROSOFT. **Integration tests in ASP.NET Core (WebApplicationFactory, Kestrel)**. Microsoft Learn, 2025. Disponível em: https://learn.microsoft.com/aspnet/core/test/integration-tests. Acesso em: 13 nov. 2026.

IETF. **RFC 9110: HTTP Semantics** (seções 8.8.3 ETag e 13 Conditional Requests). 2022. Disponível em: https://www.rfc-editor.org/rfc/rfc9110. Acesso em: 13 nov. 2026.

IETF. **RFC 9111: HTTP Caching**. 2022. Disponível em: https://www.rfc-editor.org/rfc/rfc9111. Acesso em: 13 nov. 2026.


```{=openxml}
<w:p><w:r><w:br w:type="page"/></w:r></w:p>
```

# Relatório 8 — WebApplicationFactory, bancos efêmeros, analisadores e revisão automatizada

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

O workflow foi enviado ao GitHub e executado de verdade em 14/11/2026 (resultado na seção 5). Para que um *pull request* não possa ser integrado com a revisão vermelha, falta marcar o check "Revisão automatizada" como obrigatório na proteção da branch principal, nas configurações do repositório.

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

Execuções reais no repositório `maiaralopes-dev/projetos`, branch `ccr-aa861ddb-xsu4bc`:

| Execução | Gatilho | Resultado | Observação |
|---|---|---|---|
| #1 (commit `10a25ad`) | push | **cancelada** | um novo push chegou enquanto estava na fila; `concurrency` com `cancel-in-progress` cancelou a execução obsoleta, como configurado |
| #2 (commit `ea41dcc`) | push | **sucesso** em cerca de 2 min | detalhes abaixo |

Execução #2:

- **Job "Build, analisadores, formatação e testes":** sucesso (66 s de revisão). O mesmo resumo do script local saiu no log do CI:
  ```
  | Testes | Aprovados | Falhas | Cobertura de linhas (informativa) | Duração |
  | 214 | 214 | 0 | 90.8% | 66 s |
  ✔ REVISÃO APROVADA
  ```
- **Artefato `resultados-revisao`:** 8 arquivos (resumo, logs, TRX e `coverage.cobertura.xml`), 141 KB, retidos por 14 dias.
- **Job "Publicar API (só se a revisão passar)":** iniciou **depois** do primeiro e publicou o artefato `hubpedidos-api` (cerca de 20 MB).

Os números do CI (214/214, 90,8%) são **iguais aos locais**, o que confirma que a revisão é reproduzível.

**Aviso registrado:** o runner informou que `actions/checkout@v4`, `actions/setup-dotnet@v4` e `actions/upload-artifact@v4` usam Node.js 20, que está sendo descontinuado. Não quebrou a execução, mas é um item de manutenção: atualizar para as próximas versões maiores das actions e, idealmente, fixá-las por SHA.

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
