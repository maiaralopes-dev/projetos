using System.Threading.Channels;
using HubPedidos.Application.Classificacao;
using HubPedidos.Domain.Pedidos;

namespace HubPedidos.Processamento.Fila;

/// <summary>
/// Item da fila. Imutável e sem nada do EF Core: o <see cref="Pedido"/> do domínio já é
/// imutável, e o consumidor cria o próprio escopo/DbContext para gravá-lo. Levar o pedido
/// (e não só o id) é o que permite recusar com 503 ANTES de gravar qualquer coisa.
/// </summary>
public sealed record ProcessarPedidoJob(Pedido Pedido, CategoriaProcessamento Categoria, long EnfileiradoEm)
{
    public Guid PedidoId => Pedido.Id.Valor;
}

public enum PoliticaEncerramento
{
    /// <summary>Para de aceitar e processa o que já está na fila (até o prazo do host).</summary>
    Drenar,

    /// <summary>Para de aceitar e descarta o que estiver pendente (contados como abandonados).</summary>
    Abandonar,
}

public sealed record OpcoesFila
{
    public int Capacidade { get; init; } = 500;

    /// <summary>Wait = backpressure (produtor espera); Drop* = descarta em silêncio.</summary>
    public BoundedChannelFullMode ModoCheio { get; init; } = BoundedChannelFullMode.Wait;

    /// <summary>Com fila cheia em modo Wait: quanto o produtor espera antes de ser recusado. 0 = recusa imediata (TryWrite).</summary>
    public int TempoMaximoEsperaMs { get; init; } = 200;

    /// <summary>0 = fila pausada (útil em testes de fila cheia).</summary>
    public int Consumidores { get; init; } = 4;

    /// <summary>Operações simultâneas no banco (SemaphoreSlim).</summary>
    public int LimiteBanco { get; init; } = 1;

    /// <summary>Extensão: até quantos itens já disponíveis um consumidor agrupa por transação. 1 = item a item.</summary>
    public int TamanhoLote { get; init; } = 1;

    public PoliticaEncerramento Encerramento { get; init; } = PoliticaEncerramento.Drenar;

    public int PrazoDrenagemSegundos { get; init; } = 20;

    public OpcoesFila Validar()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(Capacidade, 1, nameof(Capacidade));
        ArgumentOutOfRangeException.ThrowIfNegative(Consumidores, nameof(Consumidores));
        ArgumentOutOfRangeException.ThrowIfLessThan(LimiteBanco, 1, nameof(LimiteBanco));
        ArgumentOutOfRangeException.ThrowIfNegative(TempoMaximoEsperaMs, nameof(TempoMaximoEsperaMs));
        ArgumentOutOfRangeException.ThrowIfLessThan(PrazoDrenagemSegundos, 1, nameof(PrazoDrenagemSegundos));
        ArgumentOutOfRangeException.ThrowIfLessThan(TamanhoLote, 1, nameof(TamanhoLote));
        return this;
    }
}

public enum ResultadoEnfileiramento
{
    Aceito,

    /// <summary>Fila cheia e o produtor esgotou o tempo de espera (ou TryWrite falhou).</summary>
    RejeitadoFilaCheia,

    /// <summary>Aplicação encerrando: o writer já foi completado.</summary>
    RejeitadoEncerrando,
}

public sealed record RegistroProcessamento(Guid PedidoId, CategoriaProcessamento Categoria, TimeSpan Espera, TimeSpan Duracao, bool Sucesso);
