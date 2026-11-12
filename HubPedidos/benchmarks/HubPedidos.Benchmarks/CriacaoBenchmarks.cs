using HubPedidos.Processamento;
using BenchmarkDotNet.Attributes;
using HubPedidos.Application.Classificacao;
using HubPedidos.Application.Contratos;
using HubPedidos.Application.Mapeamento;
using HubPedidos.Processamento.Processadores;
using HubPedidos.Processamento.Fila;

namespace HubPedidos.Benchmarks;

/// <summary>
/// Atividade 5: o caminho de CPU do POST /pedidos (validação + domínio + classificação +
/// resposta) e o custo do LimitadorBanco sem disputa. Se estes números forem de
/// microssegundos, um p95 de 300 ms não pode ser explicado por CPU do nosso código.
/// </summary>
[MemoryDiagnoser]
public class CriacaoBenchmarks
{
    private static readonly CriarPedidoRequest Request = new("CLI-00042", "Sudeste", "Normal",
        [new("SKU-0001-0", 3, 19.90m), new("SKU-0002-1", 1, 250m)]);

    private static readonly ResultadoProcessamento Processamento = new("ProcessadorPadrao", 1, "ok");
    private readonly LimitadorBanco _limitador = new(1);

    [Benchmark]
    public PedidoResponse CaminhoCpuDoPost()
    {
        var pedido = MapeadorPedidos.ParaDominio(Request).Valor!;
        var categoria = ClassificadorPedidos.Classificar(pedido);
        return MapeadorResultado.ParaResposta(pedido, categoria, Processamento);
    }

    [Benchmark(Baseline = true)]
    public Task OperacaoDireta() => Task.CompletedTask;

    [Benchmark]
    public Task OperacaoViaLimitador() => _limitador.ExecutarAsync(static _ => Task.CompletedTask, CancellationToken.None);
}
