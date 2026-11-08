using System.Buffers;
using System.Text;
using System.Text.Json;
using HubPedidos.Application.Importacao;

namespace HubPedidos.Tests.Importacao;

/// <summary>Pool que registra aluguéis e devoluções e falha em devolução duplicada.</summary>
internal sealed class PoolRastreado : ArrayPool<byte>
{
    private readonly HashSet<byte[]> _pendentes = new(ReferenceEqualityComparer.Instance);
    private readonly Lock _trava = new();

    public int Alugueis { get; private set; }

    public int MaiorTamanho { get; private set; }

    public bool? UltimoClear { get; private set; }

    public int Pendentes
    {
        get
        {
            lock (_trava)
            {
                return _pendentes.Count;
            }
        }
    }

    public override byte[] Rent(int minimumLength)
    {
        var array = Shared.Rent(minimumLength);
        lock (_trava)
        {
            _pendentes.Add(array);
            Alugueis++;
            MaiorTamanho = Math.Max(MaiorTamanho, array.Length);
        }

        return array;
    }

    public override void Return(byte[] array, bool clearArray = false)
    {
        lock (_trava)
        {
            if (!_pendentes.Remove(array))
            {
                throw new InvalidOperationException("Devolução de array desconhecido ou duplicada.");
            }

            UltimoClear = clearArray;
        }

        Shared.Return(array, clearArray);
    }
}

/// <summary>Entrega no máximo N bytes por leitura: força cortes no meio de linhas e de caracteres.</summary>
internal sealed class StreamGotejador(byte[] dados, int bytesPorLeitura = 1) : Stream
{
    private int _posicao;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => _posicao; set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var n = Math.Min(Math.Min(buffer.Length, bytesPorLeitura), dados.Length - _posicao);
        dados.AsSpan(_posicao, n).CopyTo(buffer);
        _posicao += n;
        return n;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Read(buffer.Span));
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>Stream não pesquisável que gera linhas sob demanda: o lote nunca existe inteiro na memória.</summary>
internal sealed class StreamGerador(long totalBytes) : Stream
{
    private static readonly byte[] Linha = Encoding.UTF8.GetBytes("JOÃO-1;Sudeste;Normal;CAFÉ:2:9.90|PÃO:1:0.50\n");
    private long _gerados;

    public long BytesGerados => _gerados;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => _gerados; set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var escritos = 0;
        while (escritos < buffer.Length && _gerados < totalBytes)
        {
            var posNaLinha = (int)(_gerados % Linha.Length);
            var n = (int)Math.Min(Math.Min(Linha.Length - posNaLinha, buffer.Length - escritos), totalBytes - _gerados);
            Linha.AsSpan(posNaLinha, n).CopyTo(buffer[escritos..]);
            escritos += n;
            _gerados += n;
        }

        return escritos;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Read(buffer.Span));

    public static int TamanhoLinha => Linha.Length;

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>Conta chamadas de FlushAsync no destino.</summary>
internal sealed class StreamContador : MemoryStream
{
    public int Flushes { get; private set; }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        Flushes++;
        return base.FlushAsync(cancellationToken);
    }
}

internal static class Importar
{
    public static async Task<List<ResultadoLinha>> TudoAsync(IImportadorLote importador, Stream origem, CancellationToken ct = default)
    {
        var lista = new List<ResultadoLinha>();
        await foreach (var r in importador.LerAsync(origem, ct))
        {
            lista.Add(r);
        }

        return lista;
    }

    public static Task<List<ResultadoLinha>> TudoAsync(IImportadorLote importador, string texto) =>
        TudoAsync(importador, new MemoryStream(Encoding.UTF8.GetBytes(texto)));

    /// <summary>Records com listas comparam referência; JSON compara conteúdo.</summary>
    public static string Normalizar(IEnumerable<ResultadoLinha> resultados) => JsonSerializer.Serialize(resultados);
}
