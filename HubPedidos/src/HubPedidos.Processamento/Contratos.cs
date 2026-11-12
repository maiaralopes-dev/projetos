using HubPedidos.Application.Classificacao;
using HubPedidos.Application.Contratos;
using HubPedidos.Domain.Pedidos;
using HubPedidos.Processamento.Processadores;

namespace HubPedidos.Processamento;

// Contratos do MÓDULO de processamento. Ficam aqui (e não no módulo Pedidos) porque
// dependem de conceitos de processamento: o módulo Pedidos não conhece processadores.

public sealed record PedidoEnfileiradoResponse(Guid Id, string Categoria, string Status);

public sealed record MetricasFilaResponse(
    int Profundidade,
    int Capacidade,
    string ModoCheio,
    long Aceitos,
    long RejeitadosFilaCheia,
    long RejeitadosEncerrando,
    long Descartados,
    long Processados,
    long Falhas,
    long Abandonados,
    int EmAndamento,
    int LimiteBanco,
    int BancoEmUso,
    int MaximoBancoObservado,
    IReadOnlyDictionary<string, long> ProcessadosPorCategoria);

public static class MapeadorResultado
{
    public static PedidoResponse ParaResposta(Pedido pedido, CategoriaProcessamento categoria, ResultadoProcessamento processamento) =>
        new(
            Id: pedido.Id.ToString(),
            ClienteId: pedido.ClienteId,
            Regiao: pedido.Regiao.ToString(),
            Prioridade: pedido.Prioridade.ToString(),
            Total: pedido.Total,
            Categoria: categoria.ToString(),
            Processador: processamento.Processador,
            VersaoProcessador: processamento.Versao,
            Mensagem: processamento.Mensagem,
            Itens: [.. pedido.Itens.Select(i => new ItemPedidoResponse(i.Sku, i.Quantidade, i.PrecoUnitario, i.Subtotal))]);}
