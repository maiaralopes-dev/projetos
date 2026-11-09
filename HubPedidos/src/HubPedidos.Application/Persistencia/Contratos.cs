using HubPedidos.Application.Classificacao;
using HubPedidos.Application.Contratos;
using HubPedidos.Domain.Pedidos;

namespace HubPedidos.Application.Persistencia;

/// <summary>Escrita: recebe e altera o agregado. A implementação (EF Core) fica na infraestrutura.</summary>
public interface IRepositorioPedidos
{
    Task AdicionarAsync(Pedido pedido, CategoriaProcessamento categoria, CancellationToken cancellationToken);

    Task<ResultadoAlteracaoStatus> AlterarStatusAsync(Guid id, StatusPedido novo, CancellationToken cancellationToken);
}

public enum ResultadoAlteracaoStatus
{
    Alterado,
    NaoEncontrado,
    TransicaoInvalida,
}

/// <summary>Leitura: devolve DTOs prontos, nunca entidades rastreadas.</summary>
public interface IConsultasPedidos
{
    Task<PaginaResponse<PedidoResumoResponse>> ListarAsync(FiltroPedidos filtro, CancellationToken cancellationToken);

    Task<PaginaCursorResponse<PedidoResumoResponse>> ListarPorCursorAsync(string? cursor, int tamanho, CancellationToken cancellationToken);

    Task<PedidoResumoResponse?> ObterResumoAsync(Guid id, CancellationToken cancellationToken);

    Task<PedidoDetalheResponse?> ObterDetalheAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<PedidoComItensResponse>> RelatorioComItensAsync(int pagina, int tamanho, CancellationToken cancellationToken);

    Task<IReadOnlyList<ResumoRegiaoResponse>> PainelPorRegiaoAsync(CancellationToken cancellationToken);
}

public sealed record FiltroPedidos
{
    public const int TamanhoMaximo = 100;

    public string? ClienteId { get; init; }

    public Regiao? Regiao { get; init; }

    public StatusPedido? Status { get; init; }

    public int Pagina { get; init => field = Math.Max(1, value); } = 1;

    public int Tamanho { get; init => field = Math.Clamp(value, 1, TamanhoMaximo); } = 20;
}
