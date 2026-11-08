# PROGRAMAÇÃO APLICADA EM .NET — RELATÓRIO DE AULAS PRÁTICAS

**Nome:** ______________________ **RA:** ______________ **Polo:** ______________
**Local da realização da aula prática:** ______________ **Ano da postagem:** 2026

---

## Relatório 1 — Pattern matching, atributos, reflexão controlada, records e DTOs imutáveis

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
