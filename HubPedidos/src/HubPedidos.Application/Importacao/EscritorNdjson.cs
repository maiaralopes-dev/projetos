using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace HubPedidos.Application.Importacao;

/// <summary>
/// Escreve um objeto JSON por linha (NDJSON) acumulando em memória e fazendo
/// WriteAsync + FlushAsync apenas a cada <c>itensPorFlush</c> itens. Com valor 1,
/// reproduz o anti-padrão de flush por item (usado no benchmark de comparação).
/// </summary>
public sealed class EscritorNdjson : IAsyncDisposable
{
    private static readonly JsonSerializerOptions OpcoesJson = new(JsonSerializerDefaults.Web);

    // Ao serializar direto num Utf8JsonWriter vale o encoder DO WRITER (não o das opções).
    // UnicodeRanges.All mantém acentos legíveis; < > & ' " continuam escapados.
    private static readonly JsonWriterOptions OpcoesWriter = new() { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };

    private readonly Stream _destino;
    private readonly int _itensPorFlush;
    private readonly ArrayBufferWriter<byte> _buffer = new(16 * 1024);
    private readonly Utf8JsonWriter _json;
    private int _pendentes;

    public EscritorNdjson(Stream destino, int itensPorFlush)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(itensPorFlush, 1);
        _destino = destino;
        _itensPorFlush = itensPorFlush;
        _json = new Utf8JsonWriter(_buffer, OpcoesWriter);
    }

    public int Flushes { get; private set; }

    public async ValueTask EscreverAsync<T>(T item, CancellationToken cancellationToken = default)
    {
        _json.Reset(_buffer);
        JsonSerializer.Serialize(_json, item, OpcoesJson);
        _json.Flush();
        _buffer.Write("\n"u8);

        if (++_pendentes >= _itensPorFlush)
        {
            await DescarregarAsync(cancellationToken);
        }
    }

    public async ValueTask DescarregarAsync(CancellationToken cancellationToken = default)
    {
        if (_buffer.WrittenCount == 0)
        {
            return;
        }

        await _destino.WriteAsync(_buffer.WrittenMemory, cancellationToken);
        await _destino.FlushAsync(cancellationToken);
        _buffer.ResetWrittenCount();
        _pendentes = 0;
        Flushes++;
    }

    public async ValueTask DisposeAsync()
    {
        await DescarregarAsync();
        await _json.DisposeAsync();
    }
}
