using System.Text;
using HubPedidos.Application.Importacao;
using HubPedidos.Benchmarks;

namespace HubPedidos.Tests.Importacao;

public class ImportadorTests
{
    public static TheoryData<string> Implementacoes => [nameof(ImportadorBaseline), nameof(ImportadorSpanChar), nameof(ImportadorUtf8Pool)];

    private static IImportadorLote Criar(string nome, OpcoesImportacao? opcoes = null) => nome switch
    {
        nameof(ImportadorBaseline) => new ImportadorBaseline(),
        nameof(ImportadorSpanChar) => new ImportadorSpanChar(),
        nameof(ImportadorUtf8Pool) => new ImportadorUtf8Pool(opcoes),
        _ => throw new ArgumentOutOfRangeException(nameof(nome)),
    };

    // ---------- Equivalência funcional ----------

    [Theory]
    [InlineData(1, 1)]
    [InlineData(500, 7)]
    [InlineData(3_000, 42)]
    public async Task Tres_implementacoes_produzem_o_mesmo_resultado(int linhas, int semente)
    {
        var texto = GeradorLote.Gerar(linhas, semente, incluirInvalidas: true);

        var baseline = Importar.Normalizar(await Importar.TudoAsync(new ImportadorBaseline(), texto));
        var spanChar = Importar.Normalizar(await Importar.TudoAsync(new ImportadorSpanChar(), texto));
        var utf8 = Importar.Normalizar(await Importar.TudoAsync(new ImportadorUtf8Pool(), texto));

        Assert.Equal(baseline, spanChar);
        Assert.Equal(baseline, utf8);
    }

    [Theory]
    [InlineData(16)]   // menor array do ArrayPool.Shared: força crescimento
    [InlineData(37)]
    [InlineData(4096)]
    public async Task Utf8_e_equivalente_com_qualquer_tamanho_de_buffer(int bufferInicial)
    {
        var texto = GeradorLote.Gerar(400, 3, incluirInvalidas: true, quebra: "\r\n");
        var esperado = Importar.Normalizar(await Importar.TudoAsync(new ImportadorBaseline(), texto));

        var pool = new PoolRastreado();
        var importador = new ImportadorUtf8Pool(new OpcoesImportacao { TamanhoBufferInicial = bufferInicial }, pool);
        var obtido = Importar.Normalizar(await Importar.TudoAsync(importador, new StreamGotejador(Encoding.UTF8.GetBytes(texto), 7)));

        Assert.Equal(esperado, obtido);
        Assert.Equal(0, pool.Pendentes);
    }

    // ---------- Entradas de borda ----------

    [Theory]
    [MemberData(nameof(Implementacoes))]
    public async Task Entrada_vazia_ou_so_com_linhas_em_branco_nao_gera_resultado(string impl)
    {
        Assert.Empty(await Importar.TudoAsync(Criar(impl), ""));
        Assert.Empty(await Importar.TudoAsync(Criar(impl), "\n\n   \r\n\t\n"));
    }

    [Theory]
    [MemberData(nameof(Implementacoes))]
    public async Task Ultima_linha_sem_quebra_e_processada_e_numeracao_conta_linhas_em_branco(string impl)
    {
        var resultados = await Importar.TudoAsync(Criar(impl), "A;Sul;Normal;X:1:1.00\r\n\r\nB;Norte;Alta;Y:2:3.50");

        Assert.Collection(resultados,
            r => Assert.Equal((1, "A"), (r.Linha, r.Pedido!.ClienteId)),
            r => Assert.Equal((3, "B", 3.50m), (r.Linha, r.Pedido!.ClienteId, r.Pedido.Itens![0].PrecoUnitario)));
    }

    [Theory]
    [InlineData("A;Sul;Normal", "Esperados 4 campos separados por ';'.")]
    [InlineData("A;Sul;Normal;X:1:1;extra", "Esperados 4 campos separados por ';'.")]
    [InlineData("A;Sul;Normal;X:1", "Item 1: formato esperado sku:quantidade:preco.")]
    [InlineData("A;Sul;Normal;X:1:1|Y:1:1:1", "Item 2: formato esperado sku:quantidade:preco.")]
    [InlineData("A;Sul;Normal;X:um:1", "Item 1: quantidade 'um' inválida.")]
    [InlineData("A;Sul;Normal;X:-1:1", "Item 1: quantidade '-1' inválida.")]
    [InlineData("A;Sul;Normal;X:1:2,50", "Item 1: preço '2,50' inválido.")]
    [InlineData("A;Sul;Normal;X:1:", "Item 1: preço '' inválido.")]
    public async Task Linha_incompleta_ou_malformada_gera_o_mesmo_erro_nas_tres(string linha, string erro)
    {
        foreach (var impl in Implementacoes)
        {
            var r = Assert.Single(await Importar.TudoAsync(Criar(impl), linha));
            Assert.True(erro == r.Erro, $"{impl}: '{r.Erro}'");
            Assert.Null(r.Pedido);
        }
    }

    [Fact]
    public async Task Utf8_multibyte_cortado_em_qualquer_byte_e_preservado()
    {
        const string texto = "JOÃO-客户;Sul;Normal;CAFÉ-ÇÃ:1:19.90\n";
        var bytes = Encoding.UTF8.GetBytes(texto);
        Assert.True(bytes.Length > texto.Length); // confirma que há caracteres multibyte

        // 1 byte por leitura e buffer mínimo: todos os cortes possíveis acontecem.
        var importador = new ImportadorUtf8Pool(new OpcoesImportacao { TamanhoBufferInicial = 16 });
        var r = Assert.Single(await Importar.TudoAsync(importador, new StreamGotejador(bytes, 1)));

        Assert.Equal("JOÃO-客户", r.Pedido!.ClienteId);
        Assert.Equal("CAFÉ-ÇÃ", r.Pedido.Itens![0].Sku);
    }

    [Fact]
    public async Task Utf8_invalido_e_rejeitado_na_linha_sem_derrubar_o_lote()
    {
        // 0xFF nunca é válido em UTF-8; aqui ele está dentro do SKU da linha 1.
        byte[] dados = [.. "A;Sul;Normal;X"u8, 0xFF, .. ":1:1\nB;Sul;Normal;Y:1:1\n"u8];

        var resultados = await Importar.TudoAsync(new ImportadorUtf8Pool(), new MemoryStream(dados));

        Assert.Equal("Linha contém UTF-8 inválido.", resultados[0].Erro);
        Assert.Equal("B", resultados[1].Pedido!.ClienteId);
    }

    [Fact]
    public async Task Regiao_e_prioridade_conhecidas_reutilizam_a_string_do_enum()
    {
        var resultados = await Importar.TudoAsync(new ImportadorUtf8Pool(), "A;Sudeste;Urgente;X:1:1\nB;Sudeste;Urgente;Y:1:1\nC;sudeste;Urgente;Z:1:1");

        // Mesma instância em linhas diferentes = nenhuma string nova alocada para o campo.
        Assert.Same(resultados[0].Pedido!.Regiao, resultados[1].Pedido!.Regiao);
        Assert.Same(resultados[0].Pedido!.Prioridade, resultados[1].Pedido!.Prioridade);
        // Grafia diferente ("sudeste") não é canonizada aqui: preserva a equivalência com a baseline.
        Assert.Equal("sudeste", resultados[2].Pedido!.Regiao);
    }

    // ---------- Limites e ciclo de vida do buffer ----------

    [Fact]
    public async Task Lote_acima_do_limite_falha_e_devolve_o_buffer()
    {
        var pool = new PoolRastreado();
        var importador = new ImportadorUtf8Pool(new OpcoesImportacao { LimiteBytes = 1_000 }, pool);

        var ex = await Assert.ThrowsAsync<LoteInvalidoException>(() =>
            Importar.TudoAsync(importador, new MemoryStream(GeradorLote.GerarUtf8(100))));

        Assert.Contains("limite de 1000 bytes", ex.Message);
        Assert.Equal(0, pool.Pendentes);
    }

    [Fact]
    public async Task Linha_acima_do_limite_falha_e_devolve_todos_os_buffers_inclusive_os_do_crescimento()
    {
        var pool = new PoolRastreado();
        var importador = new ImportadorUtf8Pool(new OpcoesImportacao { TamanhoBufferInicial = 16, TamanhoMaximoLinha = 256 }, pool);
        var linhaGigante = "A;Sul;Normal;" + string.Join('|', Enumerable.Range(0, 100).Select(i => $"S{i}:1:1"));

        var ex = await Assert.ThrowsAsync<LoteInvalidoException>(() => Importar.TudoAsync(importador, "B;Sul;Normal;X:1:1\n" + linhaGigante));

        Assert.Equal("Linha 2 excede 256 bytes.", ex.Message);
        Assert.True(pool.Alugueis > 1, "deveria ter crescido o buffer antes de falhar");
        Assert.Equal(0, pool.Pendentes);
    }

    [Fact]
    public async Task Cancelamento_interrompe_e_devolve_o_buffer()
    {
        var pool = new PoolRastreado();
        var importador = new ImportadorUtf8Pool(pool: pool);
        using var cts = new CancellationTokenSource();
        var processadas = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in importador.LerAsync(new MemoryStream(GeradorLote.GerarUtf8(1_000)), cts.Token))
            {
                if (++processadas == 10)
                {
                    await cts.CancelAsync();
                }
            }
        });

        Assert.Equal(10, processadas); // nenhuma linha bufferizada é processada após o cancelamento
        Assert.Equal(0, pool.Pendentes);
    }

    [Fact]
    public async Task Abandonar_o_await_foreach_tambem_devolve_o_buffer()
    {
        var pool = new PoolRastreado();
        var importador = new ImportadorUtf8Pool(pool: pool);

        await foreach (var _ in importador.LerAsync(new MemoryStream(GeradorLote.GerarUtf8(100))))
        {
            Assert.Equal(1, pool.Pendentes); // em uso durante a iteração
            break;
        }

        Assert.Equal(0, pool.Pendentes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Limpeza_do_buffer_segue_a_opcao(bool limpar)
    {
        var pool = new PoolRastreado();
        await Importar.TudoAsync(new ImportadorUtf8Pool(new OpcoesImportacao { LimparBuffer = limpar }, pool), new MemoryStream(GeradorLote.GerarUtf8(5)));

        Assert.Equal(limpar, pool.UltimoClear);
    }

    [Fact]
    public async Task Fluxo_grande_e_processado_sem_carregar_o_corpo_inteiro()
    {
        const long tamanho = 64L * 1024 * 1024; // 64 MiB gerados sob demanda
        var pool = new PoolRastreado();
        var origem = new StreamGerador(tamanho);
        var importador = new ImportadorUtf8Pool(new OpcoesImportacao { LimiteBytes = tamanho }, pool);
        long linhas = 0, itens = 0;

        var memoriaAntes = GC.GetTotalMemory(forceFullCollection: true);
        await foreach (var r in importador.LerAsync(origem))
        {
            linhas++;
            itens += r.Pedido?.Itens?.Count ?? 0;
        }

        var memoriaDepois = GC.GetTotalMemory(forceFullCollection: true);

        Assert.Equal(tamanho, origem.BytesGerados);
        Assert.Equal((tamanho + StreamGerador.TamanhoLinha - 1) / StreamGerador.TamanhoLinha, linhas);
        Assert.Equal(16 * 1024, pool.MaiorTamanho);            // nunca precisou de buffer maior que o inicial
        Assert.Equal(1, pool.Alugueis);
        Assert.True(memoriaDepois - memoriaAntes < 8 * 1024 * 1024, $"memória retida cresceu {memoriaDepois - memoriaAntes} bytes");
        Assert.True(itens > linhas);
    }
}
