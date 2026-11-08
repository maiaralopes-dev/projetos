using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using HubPedidos.Application.Contratos;
using static HubPedidos.Application.Importacao.FormatoLote;

namespace HubPedidos.Application.Importacao;

/// <summary>
/// Implementação de referência: simples e correta. StreamReader decodifica para
/// <c>string</c> e string.Split cria um array e uma string por campo e por parte.
/// Não impõe limites (uma linha gigante seria lida inteira em memória).
/// </summary>
public sealed class ImportadorBaseline : IImportadorLote
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

            yield return Interpretar(linha, numero);
        }
    }

    internal static ResultadoLinha Interpretar(string linha, int numero)
    {
        var campos = linha.Split(SeparadorCampo);
        if (campos.Length != 4)
        {
            return ResultadoLinha.Falha(numero, ErroCampos);
        }

        var itens = new List<ItemPedidoRequest>();
        if (campos[3].Length > 0)
        {
            var textoItens = campos[3].Split(SeparadorItem);
            for (var i = 0; i < textoItens.Length; i++)
            {
                var partes = textoItens[i].Split(SeparadorParte);
                if (partes.Length != 3)
                {
                    return ResultadoLinha.Falha(numero, ErroItem(i + 1));
                }

                if (!int.TryParse(partes[1], NumberStyles.None, CultureInfo.InvariantCulture, out var quantidade))
                {
                    return ResultadoLinha.Falha(numero, ErroQuantidade(i + 1, partes[1]));
                }

                if (!decimal.TryParse(partes[2], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var preco))
                {
                    return ResultadoLinha.Falha(numero, ErroPreco(i + 1, partes[2]));
                }

                itens.Add(new ItemPedidoRequest(partes[0], quantidade, preco));
            }
        }

        return ResultadoLinha.Ok(numero, new CriarPedidoRequest(campos[0], campos[1], campos[2], itens));
    }
}
