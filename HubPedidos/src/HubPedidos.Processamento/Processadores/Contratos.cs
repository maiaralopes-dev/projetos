using HubPedidos.Application.Classificacao;
using HubPedidos.Domain.Pedidos;

namespace HubPedidos.Processamento.Processadores;

public interface IProcessadorPedido
{
    ValueTask<ResultadoProcessamento> ProcessarAsync(Pedido pedido, CancellationToken cancellationToken);
}

public sealed record ResultadoProcessamento(string Processador, int Versao, string Mensagem);

/// <summary>
/// Metadado declarativo que associa uma implementação a uma categoria e versão.
/// AllowMultiple permite que um processador atenda mais de uma categoria.
/// Inherited = false evita que uma subclasse "herde" o registro sem querer.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ProcessadorPedidoAttribute(CategoriaProcessamento categoria, int versao = 1) : Attribute
{
    public CategoriaProcessamento Categoria { get; } = categoria;

    public int Versao { get; } = versao;
}
