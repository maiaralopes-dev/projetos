using HubPedidos.Application.Classificacao;
using HubPedidos.Domain.Pedidos;

namespace HubPedidos.Application.Processadores.Implementacoes;

// Estratégias de processamento. Nesta unidade o "processamento" apenas decide o
// destino; persistência e fila entram nas unidades 3 e 4.

[ProcessadorPedido(CategoriaProcessamento.Padrao)]
[ProcessadorPedido(CategoriaProcessamento.Atacado)]
public sealed class ProcessadorPadrao : IProcessadorPedido
{
    public ValueTask<ResultadoProcessamento> ProcessarAsync(Pedido pedido, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new ResultadoProcessamento(nameof(ProcessadorPadrao), 1,
            $"Pedido com {pedido.Itens.Length} item(ns) enviado para separação padrão (SLA 48h)."));
}

[ProcessadorPedido(CategoriaProcessamento.Expresso, versao: 2)]
public sealed class ProcessadorExpresso : IProcessadorPedido
{
    public ValueTask<ResultadoProcessamento> ProcessarAsync(Pedido pedido, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new ResultadoProcessamento(nameof(ProcessadorExpresso), 2,
            $"Prioridade {pedido.Prioridade}: separação expressa (SLA 4h)."));
}

[ProcessadorPedido(CategoriaProcessamento.AltoValor)]
[ProcessadorPedido(CategoriaProcessamento.RevisaoManual)]
public sealed class ProcessadorAntifraude : IProcessadorPedido
{
    public ValueTask<ResultadoProcessamento> ProcessarAsync(Pedido pedido, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new ResultadoProcessamento(nameof(ProcessadorAntifraude), 1,
            $"Total {pedido.Total:N2} retido para análise antifraude antes da separação."));
}

[ProcessadorPedido(CategoriaProcessamento.Internacional)]
public sealed class ProcessadorInternacional : IProcessadorPedido
{
    public ValueTask<ResultadoProcessamento> ProcessarAsync(Pedido pedido, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new ResultadoProcessamento(nameof(ProcessadorInternacional), 1,
            "Documentação aduaneira gerada; envio internacional agendado."));
}
