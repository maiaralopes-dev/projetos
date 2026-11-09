using HubPedidos.Application.Classificacao;
using HubPedidos.Domain.Pedidos;

namespace HubPedidos.Infrastructure.Persistencia;

// Modelo de PERSISTÊNCIA, separado do domínio. O domínio continua imutável e sem
// referência ao EF Core (verificado por teste arquitetural); estas classes têm
// setters porque o EF precisa materializá-las e rastrear mudanças.

public sealed class PedidoRegistro
{
    public Guid Id { get; set; }

    public string ClienteId { get; set; } = "";

    public Regiao Regiao { get; set; }

    public Prioridade Prioridade { get; set; }

    public StatusPedido Status { get; set; }

    public CategoriaProcessamento Categoria { get; set; }

    /// <summary>Desnormalizado: o pedido é imutável depois de criado (só o status muda).</summary>
    public decimal Total { get; set; }

    public DateTime CriadoEmUtc { get; set; }

    /// <summary>Tipo complexo opcional, mapeado em colunas da própria tabela.</summary>
    public EnderecoRegistro? Entrega { get; set; }

    /// <summary>Tipo complexo mapeado como JSON ou como colunas (ver <see cref="MapeamentoMetadados"/>).</summary>
    public MetadadosRegistro Metadados { get; set; } = new();

    public List<ItemPedidoRegistro> Itens { get; set; } = [];
}

public sealed class ItemPedidoRegistro
{
    public long Id { get; set; }

    public Guid PedidoId { get; set; }

    public string Sku { get; set; } = "";

    public int Quantidade { get; set; }

    public decimal PrecoUnitario { get; set; }
}

public sealed class EnderecoRegistro
{
    public string Logradouro { get; set; } = "";

    public string Cidade { get; set; } = "";

    public string Uf { get; set; } = "";

    public string Cep { get; set; } = "";
}

public sealed class MetadadosRegistro
{
    public string Canal { get; set; } = "api";

    public string? Origem { get; set; }

    public string? Campanha { get; set; }
}

internal static class MapeadorRegistro
{
    public static PedidoRegistro ParaRegistro(Pedido pedido, CategoriaProcessamento categoria) => new()
    {
        Id = pedido.Id.Valor,
        ClienteId = pedido.ClienteId,
        Regiao = pedido.Regiao,
        Prioridade = pedido.Prioridade,
        Status = pedido.Status,
        Categoria = categoria,
        Total = pedido.Total,
        CriadoEmUtc = pedido.CriadoEm.UtcDateTime,
        Entrega = pedido.Entrega is { } e
            ? new EnderecoRegistro { Logradouro = e.Logradouro, Cidade = e.Cidade, Uf = e.Uf, Cep = e.Cep }
            : null,
        Metadados = new MetadadosRegistro
        {
            Canal = pedido.Metadados.Canal,
            Origem = pedido.Metadados.Origem,
            Campanha = pedido.Metadados.Campanha,
        },
        Itens = [.. pedido.Itens.Select(i => new ItemPedidoRegistro
        {
            PedidoId = pedido.Id.Valor,
            Sku = i.Sku,
            Quantidade = i.Quantidade,
            PrecoUnitario = i.PrecoUnitario,
        })],
    };
}
