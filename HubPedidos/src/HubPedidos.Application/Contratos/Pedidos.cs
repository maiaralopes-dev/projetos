namespace HubPedidos.Application.Contratos;

// DTOs de fronteira: só tipos primitivos/strings e outros DTOs, nunca tipos do domínio.
// Campos de entrada são anuláveis de propósito: o JSON do cliente pode omitir qualquer
// coisa, e a validação acontece no mapeador, antes de criar o objeto de domínio.

public sealed record ItemPedidoRequest(string? Sku, int Quantidade, decimal PrecoUnitario);

public sealed record EnderecoDto(string? Logradouro, string? Cidade, string? Uf, string? Cep);

public sealed record MetadadosDto(string? Canal, string? Origem, string? Campanha);

public sealed record CriarPedidoRequest(
    string? ClienteId,
    string? Regiao,
    string? Prioridade,
    IReadOnlyList<ItemPedidoRequest>? Itens,
    EnderecoDto? Entrega = null,
    MetadadosDto? Metadados = null);

public sealed record ItemPedidoResponse(string Sku, int Quantidade, decimal PrecoUnitario, decimal Subtotal);

public sealed record PedidoResponse(
    string Id,
    string ClienteId,
    string Regiao,
    string Prioridade,
    decimal Total,
    string Categoria,
    string Processador,
    int VersaoProcessador,
    string Mensagem,
    IReadOnlyList<ItemPedidoResponse> Itens);

public sealed record ProcessadorInfoResponse(string Categoria, int Versao, string Tipo);

/// <summary>Uma linha da resposta NDJSON de importação de lote.</summary>
public sealed record ResultadoImportacaoResponse(
    int Linha,
    string Status,
    string? Id,
    string? Categoria,
    IReadOnlyList<string>? Erros);
