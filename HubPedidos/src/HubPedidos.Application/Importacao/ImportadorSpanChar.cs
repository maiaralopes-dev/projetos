using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using HubPedidos.Application.Contratos;
using static HubPedidos.Application.Importacao.FormatoLote;

namespace HubPedidos.Application.Importacao;

/// <summary>
/// Etapa 2: mesma leitura por StreamReader (ainda aloca a string da linha), mas o
/// recorte é feito com <see cref="ReadOnlySpan{T}"/> de char: sem arrays de Split e sem
/// strings intermediárias para quantidade, preço, região e prioridade.
/// </summary>
public sealed class ImportadorSpanChar : IImportadorLote
{
    public async IAsyncEnumerable<ResultadoLinha> LerAsync(Stream origem, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var leitor = new StreamReader(origem, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var numero = 0;
        while (await leitor.ReadLineAsync(cancellationToken) is { } linha)
        {
            numero++;
            if (string.IsNullOrWhiteSpace(linha))
            {
                continue;
            }

            // O span vive só dentro de Interpretar (método síncrono): nunca atravessa await.
            yield return Interpretar(linha, numero);
        }
    }

    internal static ResultadoLinha Interpretar(ReadOnlySpan<char> linha, int numero)
    {
        Span<Range> campos = stackalloc Range[4];
        if (Dividir(linha, campos, SeparadorCampo) != 4)
        {
            return ResultadoLinha.Falha(numero, ErroCampos);
        }

        var textoItens = linha[campos[3]];
        ItemPedidoRequest[] itens = textoItens.IsEmpty ? [] : new ItemPedidoRequest[textoItens.Count(SeparadorItem) + 1];
        Span<Range> partes = stackalloc Range[3];
        var i = 0;
        if (!textoItens.IsEmpty)
        {
            foreach (var intervalo in textoItens.Split(SeparadorItem))
            {
                var item = textoItens[intervalo];
                if (Dividir(item, partes, SeparadorParte) != 3)
                {
                    return ResultadoLinha.Falha(numero, ErroItem(i + 1));
                }

                if (!int.TryParse(item[partes[1]], NumberStyles.None, CultureInfo.InvariantCulture, out var quantidade))
                {
                    return ResultadoLinha.Falha(numero, ErroQuantidade(i + 1, item[partes[1]].ToString()));
                }

                if (!decimal.TryParse(item[partes[2]], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var preco))
                {
                    return ResultadoLinha.Falha(numero, ErroPreco(i + 1, item[partes[2]].ToString()));
                }

                itens[i++] = new ItemPedidoRequest(item[partes[0]].ToString(), quantidade, preco);
            }
        }

        return ResultadoLinha.Ok(numero, new CriarPedidoRequest(
            linha[campos[0]].ToString(),
            NomeConhecido(linha[campos[1]], NomesRegiao),
            NomeConhecido(linha[campos[2]], NomesPrioridade),
            itens));
    }

    /// <summary>Reaproveita a string constante do enum quando o texto é idêntico.</summary>
    private static string NomeConhecido(ReadOnlySpan<char> texto, string[] nomes)
    {
        foreach (var nome in nomes)
        {
            if (texto.SequenceEqual(nome))
            {
                return nome;
            }
        }

        return texto.ToString();
    }
}
