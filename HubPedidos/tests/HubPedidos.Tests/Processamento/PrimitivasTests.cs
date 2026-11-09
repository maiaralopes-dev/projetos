using HubPedidos.Application.Classificacao;
using HubPedidos.Application.Processamento;

namespace HubPedidos.Tests.Processamento;

public class PrimitivasTests
{
    [Fact]
    public async Task Limitador_nao_libera_slot_que_nao_obteve()
    {
        var limitador = new LimitadorBanco(1);
        var portao = new TaskCompletionSource();
        var ocupando = limitador.ExecutarAsync(_ => portao.Task, default);
        Assert.Equal(0, limitador.Disponiveis);

        using var cts = new CancellationTokenSource(20);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => limitador.ExecutarAsync(_ => Task.CompletedTask, cts.Token));

        portao.SetResult();
        await ocupando;
        Assert.Equal(1, limitador.Disponiveis); // não 2: o cancelado nunca chamou Release
    }

    [Fact]
    public async Task Limitador_libera_slot_mesmo_com_excecao()
    {
        var limitador = new LimitadorBanco(2);

        await Assert.ThrowsAsync<InvalidOperationException>(() => limitador.ExecutarAsync(_ => throw new InvalidOperationException(), default));

        Assert.Equal(2, limitador.Disponiveis);
        Assert.Equal(0, limitador.EmUso);
    }

    [Fact]
    public void Metricas_por_categoria_sao_exatas_sob_concorrencia()
    {
        var metricas = new MetricasFila();
        var categorias = new[] { CategoriaProcessamento.Padrao, CategoriaProcessamento.Expresso, CategoriaProcessamento.AltoValor };
        var job = categorias.Select(c => new ProcessarPedidoJob(Construtores.Pedido(), c, 0)).ToArray();

        Parallel.For(0, 30_000, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
        {
            metricas.Iniciou(TimeSpan.Zero);
            metricas.Concluiu(job[i % 3], TimeSpan.Zero, TimeSpan.Zero, sucesso: true);
        });

        Assert.Equal(30_000, metricas.Processados);
        Assert.All(categorias, c => Assert.Equal(10_000, metricas.ProcessadosPorCategoria[c.ToString()]));
        Assert.Equal(0, metricas.EmAndamento);
    }
}
