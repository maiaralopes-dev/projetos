# PROGRAMAÇÃO APLICADA EM .NET — RELATÓRIO DE AULAS PRÁTICAS

**Nome:** ______________________ **RA:** ______________ **Polo:** ______________
**Local da realização da aula prática:** ______________ **Ano da postagem:** 2026

---

## Relatório 2 — Span&lt;T&gt;, Memory&lt;T&gt;, pooling e caminhos críticos limitados por E/S e medidos

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
