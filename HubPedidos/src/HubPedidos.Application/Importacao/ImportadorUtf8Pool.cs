using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Unicode;
using HubPedidos.Application.Contratos;
using static HubPedidos.Application.Importacao.FormatoLote;

namespace HubPedidos.Application.Importacao;

/// <summary>
/// Etapas 3 e 4: lê o stream em blocos para um buffer alugado de
/// <see cref="ArrayPool{T}"/>, localiza as linhas diretamente nos bytes UTF-8 e só
/// decodifica para string o que vai para o DTO (cliente e SKU).
/// <list type="bullet">
/// <item>Leitura incremental com limite total e de linha, e CancellationToken.</item>
/// <item>O buffer é devolvido em <c>finally</c>: vale para fim normal, exceção,
/// cancelamento e também quando o consumidor abandona o <c>await foreach</c>.</item>
/// <item>Só a fatia válida [inicio, fim) é lida; o pool não entrega buffer zerado.</item>
/// <item>Os separadores são ASCII, então quebrar um bloco no meio de um caractere
/// multibyte não corrompe nada: só linhas completas são decodificadas.</item>
/// </list>
/// </summary>
public sealed class ImportadorUtf8Pool(OpcoesImportacao? opcoes = null, ArrayPool<byte>? pool = null) : IImportadorLote
{
    private static readonly byte[] EspacosAscii = " \t\r\f\v"u8.ToArray();

    private readonly OpcoesImportacao _opcoes = opcoes ?? new OpcoesImportacao();
    private readonly ArrayPool<byte> _pool = pool ?? ArrayPool<byte>.Shared;

    public async IAsyncEnumerable<ResultadoLinha> LerAsync(Stream origem, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var buffer = _pool.Rent(Math.Min(_opcoes.TamanhoBufferInicial, _opcoes.TamanhoMaximoLinha));
        try
        {
            int inicio = 0, fim = 0, numero = 0;
            long totalLido = 0;
            var terminou = false;

            while (true)
            {
                // 1) Consome todas as linhas completas que já estão no buffer.
                int posicao;
                while ((posicao = buffer.AsSpan(inicio, fim - inicio).IndexOf((byte)'\n')) >= 0)
                {
                    // Sem esta verificação, linhas já bufferizadas seriam processadas
                    // mesmo após o cancelamento (o token só seria visto no próximo ReadAsync).
                    cancellationToken.ThrowIfCancellationRequested();
                    numero++;
                    var resultado = Interpretar(buffer.AsSpan(inicio, posicao), numero);
                    inicio += posicao + 1;
                    if (resultado is not null)
                    {
                        yield return resultado;
                    }
                }

                if (terminou)
                {
                    // Última linha sem '\n' final ainda é uma linha.
                    if (fim > inicio)
                    {
                        var resultado = Interpretar(buffer.AsSpan(inicio, fim - inicio), ++numero);
                        if (resultado is not null)
                        {
                            yield return resultado;
                        }
                    }

                    yield break;
                }

                // 2) Move o resto (linha incompleta) para o começo do buffer.
                if (inicio > 0)
                {
                    buffer.AsSpan(inicio, fim - inicio).CopyTo(buffer);
                    fim -= inicio;
                    inicio = 0;
                }

                // 3) Linha não coube: cresce até o limite de linha ou falha.
                if (fim == buffer.Length)
                {
                    if (buffer.Length >= _opcoes.TamanhoMaximoLinha)
                    {
                        throw new LoteInvalidoException($"Linha {numero + 1} excede {_opcoes.TamanhoMaximoLinha} bytes.");
                    }

                    var maior = _pool.Rent(Math.Min(buffer.Length * 2, _opcoes.TamanhoMaximoLinha));
                    buffer.AsSpan(0, fim).CopyTo(maior);
                    var antigo = buffer;
                    buffer = maior;
                    _pool.Return(antigo, _opcoes.LimparBuffer);
                }

                // 4) Lê mais um bloco. Memory<byte> (e não Span) pode atravessar o await.
                var lidos = await origem.ReadAsync(buffer.AsMemory(fim), cancellationToken);
                totalLido += lidos;
                if (totalLido > _opcoes.LimiteBytes)
                {
                    throw new LoteInvalidoException($"Lote excede o limite de {_opcoes.LimiteBytes} bytes.");
                }

                fim += lidos;
                terminou = lidos == 0;
            }
        }
        finally
        {
            if (_opcoes.LimparBuffer)
            {
                // Custo: um memset do buffer inteiro por lote. Só vale para dados sensíveis.
                _pool.Return(buffer, clearArray: true);
            }
            else
            {
                _pool.Return(buffer);
            }
        }
    }

    internal static ResultadoLinha? Interpretar(ReadOnlySpan<byte> linha, int numero)
    {
        if (linha is [.., (byte)'\r'])
        {
            linha = linha[..^1];
        }

        if (linha.Trim(EspacosAscii).IsEmpty)
        {
            return null;
        }

        if (!Utf8.IsValid(linha))
        {
            return ResultadoLinha.Falha(numero, ErroUtf8);
        }

        Span<Range> campos = stackalloc Range[4];
        if (Dividir(linha, campos, (byte)SeparadorCampo) != 4)
        {
            return ResultadoLinha.Falha(numero, ErroCampos);
        }

        var textoItens = linha[campos[3]];
        ItemPedidoRequest[] itens = textoItens.IsEmpty ? [] : new ItemPedidoRequest[textoItens.Count((byte)SeparadorItem) + 1];
        Span<Range> partes = stackalloc Range[3];
        var i = 0;
        if (!textoItens.IsEmpty)
        {
            foreach (var intervalo in textoItens.Split((byte)SeparadorItem))
            {
                var item = textoItens[intervalo];
                if (Dividir(item, partes, (byte)SeparadorParte) != 3)
                {
                    return ResultadoLinha.Falha(numero, ErroItem(i + 1));
                }

                // Parsing numérico direto dos bytes UTF-8 (IUtf8SpanParsable).
                if (!int.TryParse(item[partes[1]], NumberStyles.None, CultureInfo.InvariantCulture, out var quantidade))
                {
                    return ResultadoLinha.Falha(numero, ErroQuantidade(i + 1, Encoding.UTF8.GetString(item[partes[1]])));
                }

                if (!decimal.TryParse(item[partes[2]], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var preco))
                {
                    return ResultadoLinha.Falha(numero, ErroPreco(i + 1, Encoding.UTF8.GetString(item[partes[2]])));
                }

                itens[i++] = new ItemPedidoRequest(Encoding.UTF8.GetString(item[partes[0]]), quantidade, preco);
            }
        }

        return ResultadoLinha.Ok(numero, new CriarPedidoRequest(
            Encoding.UTF8.GetString(linha[campos[0]]),
            NomeConhecido(linha[campos[1]], NomesRegiao),
            NomeConhecido(linha[campos[2]], NomesPrioridade),
            itens));
    }

    private static string NomeConhecido(ReadOnlySpan<byte> texto, string[] nomes)
    {
        foreach (var nome in nomes)
        {
            if (Ascii.Equals(texto, nome))
            {
                return nome;
            }
        }

        return Encoding.UTF8.GetString(texto);
    }
}
