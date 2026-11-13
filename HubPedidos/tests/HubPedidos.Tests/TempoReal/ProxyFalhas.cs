using System.Net;
using System.Net.Sockets;

namespace HubPedidos.Tests.TempoReal;

/// <summary>
/// Proxy TCP entre o cliente e o Kestrel, para simular quedas de rede de verdade:
/// <see cref="DerrubarTudo"/> fecha todas as conexões ativas (WebSocket incluso) e
/// <see cref="Recusar"/> faz novas conexões serem fechadas assim que aceitas.
/// </summary>
internal sealed class ProxyFalhas : IAsyncDisposable
{
    private readonly TcpListener _ouvinte = new(IPAddress.Loopback, 0);
    private readonly int _portaDestino;
    private readonly List<(TcpClient Cliente, TcpClient Servidor)> _pares = [];
    private readonly Lock _trava = new();
    private readonly CancellationTokenSource _parar = new();
    private volatile bool _recusando;

    public ProxyFalhas(int portaDestino)
    {
        _portaDestino = portaDestino;
        _ouvinte.Start();
        _ = AceitarAsync();
    }

    public Uri Endereco => new($"http://127.0.0.1:{((IPEndPoint)_ouvinte.LocalEndpoint).Port}");

    public void Recusar(bool recusar) => _recusando = recusar;

    public void DerrubarTudo()
    {
        lock (_trava)
        {
            foreach (var (c, s) in _pares)
            {
                try
                {
                    c.Client.LingerState = new LingerOption(true, 0); // RST: queda abrupta, não FIN educado
                }
                catch (ObjectDisposedException)
                {
                    // o par já tinha sido fechado pela bomba de cópia
                }

                c.Dispose();
                s.Dispose();
            }

            _pares.Clear();
        }
    }

    private async Task AceitarAsync()
    {
        while (!_parar.IsCancellationRequested)
        {
            TcpClient cliente;
            try
            {
                cliente = await _ouvinte.AcceptTcpClientAsync(_parar.Token);
            }
            catch (Exception)
            {
                return;
            }

            if (_recusando)
            {
                cliente.Client.LingerState = new LingerOption(true, 0);
                cliente.Dispose();
                continue;
            }

            var servidor = new TcpClient();
            await servidor.ConnectAsync(IPAddress.Loopback, _portaDestino);
            lock (_trava)
            {
                _pares.Add((cliente, servidor));
            }

            _ = BombearAsync(cliente.GetStream(), servidor.GetStream());
            _ = BombearAsync(servidor.GetStream(), cliente.GetStream());
        }
    }

    private static async Task BombearAsync(Stream de, Stream para)
    {
        try
        {
            await de.CopyToAsync(para);
        }
        catch (Exception)
        {
            // conexão derrubada
        }
        finally
        {
            de.Dispose();
            para.Dispose();
        }
    }

    public ValueTask DisposeAsync()
    {
        _parar.Cancel();
        _ouvinte.Stop();
        DerrubarTudo();
        _parar.Dispose();
        return ValueTask.CompletedTask;
    }
}
