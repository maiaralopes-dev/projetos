using HubPedidos.Application.Contratos;

namespace HubPedidos.Application.Importacao;

/// <summary>
/// Formato do lote (texto UTF-8, um pedido por linha):
/// <c>clienteId;regiao;prioridade;sku:quantidade:preco|sku:quantidade:preco</c>
/// O importador faz só a análise sintática; a validação de negócio continua no
/// <see cref="Mapeamento.MapeadorPedidos"/>.
/// </summary>
public sealed record ResultadoLinha(int Linha, CriarPedidoRequest? Pedido, string? Erro)
{
    public static ResultadoLinha Ok(int linha, CriarPedidoRequest pedido) => new(linha, pedido, null);

    public static ResultadoLinha Falha(int linha, string erro) => new(linha, null, erro);
}

public sealed record OpcoesImportacao
{
    /// <summary>Tamanho pedido ao pool no início; cresce até <see cref="TamanhoMaximoLinha"/>.</summary>
    public int TamanhoBufferInicial { get; init; } = 16 * 1024;

    public int TamanhoMaximoLinha { get; init; } = 64 * 1024;

    /// <summary>Limite total do corpo lido (proteção contra lotes enormes).</summary>
    public long LimiteBytes { get; init; } = 10 * 1024 * 1024;

    /// <summary>Zera o buffer antes de devolvê-lo ao pool (dados sensíveis).</summary>
    public bool LimparBuffer { get; init; }

    /// <summary>Quantos resultados acumular antes de cada flush da resposta.</summary>
    public int ItensPorFlush { get; init; } = 256;
}

public sealed class LoteInvalidoException(string mensagem) : Exception(mensagem);

public interface IImportadorLote
{
    IAsyncEnumerable<ResultadoLinha> LerAsync(Stream origem, CancellationToken cancellationToken = default);
}

/// <summary>Mensagens e utilitários comuns: garantem que as três versões sejam equivalentes.</summary>
internal static class FormatoLote
{
    public const char SeparadorCampo = ';';
    public const char SeparadorItem = '|';
    public const char SeparadorParte = ':';

    public const string ErroCampos = "Esperados 4 campos separados por ';'.";
    public const string ErroUtf8 = "Linha contém UTF-8 inválido.";

    public static string ErroItem(int item) => $"Item {item}: formato esperado sku:quantidade:preco.";

    public static string ErroQuantidade(int item, string texto) => $"Item {item}: quantidade '{texto}' inválida.";

    public static string ErroPreco(int item, string texto) => $"Item {item}: preço '{texto}' inválido.";

    public static readonly string[] NomesRegiao = Enum.GetNames<Domain.Pedidos.Regiao>();
    public static readonly string[] NomesPrioridade = Enum.GetNames<Domain.Pedidos.Prioridade>();

    /// <summary>
    /// Preenche <paramref name="destino"/> com os intervalos entre separadores e devolve a
    /// quantidade encontrada (destino.Length + 1 indica "mais partes do que o esperado").
    /// </summary>
    public static int Dividir<T>(ReadOnlySpan<T> origem, Span<Range> destino, T separador) where T : IEquatable<T>
    {
        var n = 0;
        foreach (var intervalo in origem.Split(separador))
        {
            if (n == destino.Length)
            {
                return n + 1;
            }

            destino[n++] = intervalo;
        }

        return n;
    }
}
