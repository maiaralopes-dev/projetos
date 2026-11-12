namespace HubPedidos.Application.Contratos;

// DTOs de leitura (atividade 3). Cada um tem exatamente os campos que a tela/cliente
// usa: a consulta projeta direto para eles, e o SQL só traz essas colunas.

public sealed record PedidoResumoResponse(
    Guid Id,
    string ClienteId,
    string Regiao,
    string Status,
    string Categoria,
    decimal Total,
    int QuantidadeItens,
    DateTime CriadoEmUtc);

public sealed record PedidoDetalheResponse(
    Guid Id,
    string ClienteId,
    string Regiao,
    string Prioridade,
    string Status,
    string Categoria,
    decimal Total,
    DateTime CriadoEmUtc,
    EnderecoDto? Entrega,
    MetadadosDto Metadados,
    IReadOnlyList<ItemPedidoResponse> Itens);

public sealed record PaginaResponse<T>(IReadOnlyList<T> Itens, int Pagina, int Tamanho, int Total)
{
    public int TotalPaginas => Tamanho == 0 ? 0 : (Total + Tamanho - 1) / Tamanho;
}

public sealed record PaginaCursorResponse<T>(IReadOnlyList<T> Itens, string? ProximoCursor);

public sealed record ItemRelatorioResponse(string Sku, int Quantidade, decimal Subtotal);

public sealed record PedidoComItensResponse(Guid Id, string ClienteId, decimal Total, IReadOnlyList<ItemRelatorioResponse> Itens);

public sealed record ResumoRegiaoResponse(string Regiao, int Pedidos, int Unidades, decimal Total, decimal TicketMedio);

public sealed record AlterarStatusRequest(string? Status);
