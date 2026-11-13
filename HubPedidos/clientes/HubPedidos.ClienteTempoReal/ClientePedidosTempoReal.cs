using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace HubPedidos.ClienteTempoReal;

public enum EstadoConexao
{
    Desconectado,
    Conectando,
    Conectado,
    Reconectando,
}

// Tipos PRÓPRIOS do cliente (contrato publicado), sem referência ao servidor.
public sealed record PedidoAtualizadoMensagem(Guid PedidoId, string Status, long Sequencia, DateTimeOffset Em);

public sealed record AcessoRevogadoMensagem(string Motivo);

public sealed record PedidoResumo(Guid Id, string ClienteId, string Status, string Categoria);

/// <summary>Retentativas: 0 s, 0,25 s, 0,5 s, 1 s, 2 s, 5 s e então 10 s até 2 minutos; depois desiste.</summary>
public sealed class BackoffComTeto(TimeSpan[]? atrasos = null, TimeSpan? limiteTotal = null) : IRetryPolicy
{
    private readonly TimeSpan[] _atrasos = atrasos ?? [TimeSpan.Zero, TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)];
    private readonly TimeSpan _limite = limiteTotal ?? TimeSpan.FromMinutes(2);

    public TimeSpan? NextRetryDelay(RetryContext contexto) =>
        contexto.ElapsedTime >= _limite ? null
        : contexto.PreviousRetryCount < _atrasos.Length ? _atrasos[contexto.PreviousRetryCount]
        : TimeSpan.FromSeconds(10);
}

/// <summary>
/// Cliente de tempo real do painel. O hub avisa; o HTTP é a FONTE DA VERDADE:
/// eventos perdidos durante uma queda são recuperados consultando o estado atual
/// depois de reconectar (sem depender de "replay" do servidor).
/// </summary>
public sealed class ClientePedidosTempoReal : IAsyncDisposable
{
    private readonly HubConnection _conexao;
    private readonly HttpClient _http;
    private readonly ConcurrentDictionary<Guid, EstadoLocal> _assinados = new();

    public ClientePedidosTempoReal(Uri baseUrl, Func<string?> token, IRetryPolicy? politica = null, HttpMessageHandler? handlerHttp = null)
    {
        _http = handlerHttp is null ? new HttpClient { BaseAddress = baseUrl } : new HttpClient(handlerHttp, disposeHandler: false) { BaseAddress = baseUrl };
        _http.DefaultRequestHeaders.Authorization = null;
        TokenAtual = token;

        _conexao = new HubConnectionBuilder()
            .WithUrl(new Uri(baseUrl, "/hubs/pedidos"), o => o.AccessTokenProvider = () => Task.FromResult(token()))
            .WithAutomaticReconnect(politica ?? new BackoffComTeto())
            .Build();

        _conexao.On<PedidoAtualizadoMensagem>("PedidoAtualizado", m => Aplicar(m));
        _conexao.On<AcessoRevogadoMensagem>("AcessoRevogado", m =>
        {
            _assinados.Clear();
            AcessoRevogado?.Invoke(m.Motivo);
        });
        _conexao.Reconnecting += _ =>
        {
            Mudar(EstadoConexao.Reconectando, "Conexão perdida. Tentando reconectar… As ações estão desabilitadas até voltar.");
            return Task.CompletedTask;
        };
        _conexao.Reconnected += async _ =>
        {
            Mudar(EstadoConexao.Conectado, "Reconectado. Atualizando os pedidos…");
            await ReassinarEReconciliarAsync();
        };
        _conexao.Closed += _ =>
        {
            Mudar(EstadoConexao.Desconectado, "Sem conexão com o servidor. Recarregue a página para tentar de novo.");
            return Task.CompletedTask;
        };
    }

    private Func<string?> TokenAtual { get; }

    public EstadoConexao Estado { get; private set; } = EstadoConexao.Desconectado;

    /// <summary>A interface usa isto para habilitar/desabilitar botões que dependem do servidor.</summary>
    public bool AcoesHabilitadas => Estado == EstadoConexao.Conectado;

    public IReadOnlyCollection<Guid> Assinados => [.. _assinados.Keys];

    public event Action<EstadoConexao, string>? EstadoMudou;

    /// <summary>Atualização nova (duplicatas e eventos fora de ordem já descartados).</summary>
    public event Action<PedidoAtualizadoMensagem>? PedidoAtualizado;

    /// <summary>A reconciliação HTTP encontrou um estado diferente do local (evento perdido).</summary>
    public event Action<PedidoResumo>? Reconciliado;

    public event Action<string>? AcessoRevogado;

    public string? StatusLocal(Guid pedidoId) => _assinados.TryGetValue(pedidoId, out var e) ? e.Status : null;

    public async Task IniciarAsync(CancellationToken cancellationToken = default)
    {
        Mudar(EstadoConexao.Conectando, "Conectando…");
        await _conexao.StartAsync(cancellationToken);
        Mudar(EstadoConexao.Conectado, "Conectado.");
    }

    /// <summary>Pede ao servidor (que autoriza) e busca o estado inicial por HTTP.</summary>
    public async Task AssinarAsync(Guid pedidoId, CancellationToken cancellationToken = default)
    {
        await _conexao.InvokeAsync("AssinarPedido", pedidoId, cancellationToken); // HubException se negado
        _assinados.TryAdd(pedidoId, new EstadoLocal(null, 0, null));
        await ReconciliarAsync(pedidoId, cancellationToken);
    }

    public async Task CancelarAsync(Guid pedidoId, CancellationToken cancellationToken = default)
    {
        _assinados.TryRemove(pedidoId, out _);
        await _conexao.InvokeAsync("CancelarAssinatura", pedidoId, cancellationToken);
    }

    /// <summary>Só para testes: simula um pedido já assinado com estado conhecido.</summary>
    internal void AssinarLocal(Guid pedidoId, string status) => _assinados[pedidoId] = new EstadoLocal(status, 0, null);

    /// <summary>Descarta duplicata/atraso (sequência ≤ última vista) e atualiza o estado local.</summary>
    internal bool Aplicar(PedidoAtualizadoMensagem mensagem)
    {
        if (!_assinados.TryGetValue(mensagem.PedidoId, out var atual) || mensagem.Sequencia <= atual.UltimaSequencia)
        {
            return false;
        }

        _assinados[mensagem.PedidoId] = atual with { Status = mensagem.Status, UltimaSequencia = mensagem.Sequencia };
        PedidoAtualizado?.Invoke(mensagem);
        return true;
    }

    private async Task ReassinarEReconciliarAsync()
    {
        foreach (var id in _assinados.Keys.ToArray())
        {
            try
            {
                // O servidor REVALIDA a autorização: permissão revogada → HubException.
                await _conexao.InvokeAsync("AssinarPedido", id);
                await ReconciliarAsync(id, CancellationToken.None);
            }
            catch (HubException)
            {
                _assinados.TryRemove(id, out _);
            }
        }
    }

    private async Task ReconciliarAsync(Guid id, CancellationToken cancellationToken)
    {
        using var requisicao = new HttpRequestMessage(HttpMethod.Get, $"/api/v2/pedidos/{id}");
        requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TokenAtual());
        if (_assinados.TryGetValue(id, out var local) && local.Etag is { } etag)
        {
            requisicao.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(etag)); // nada mudou → 304, sem corpo
        }

        using var resposta = await _http.SendAsync(requisicao, cancellationToken);
        switch (resposta.StatusCode)
        {
            case HttpStatusCode.NotModified:
                return;
            case HttpStatusCode.NotFound or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                _assinados.TryRemove(id, out _);
                return;
        }

        resposta.EnsureSuccessStatusCode();
        var pedido = (await resposta.Content.ReadFromJsonAsync<PedidoResumo>(cancellationToken))!;
        var anterior = _assinados.TryGetValue(id, out var e) ? e.Status : null;
        // HTTP é autoritativo: reinicia a sequência (o servidor pode ter reiniciado o contador).
        _assinados[id] = new EstadoLocal(pedido.Status, 0, resposta.Headers.ETag?.ToString());
        if (anterior is not null && anterior != pedido.Status)
        {
            Reconciliado?.Invoke(pedido);
        }
    }

    private void Mudar(EstadoConexao estado, string mensagem)
    {
        Estado = estado;
        EstadoMudou?.Invoke(estado, mensagem);
    }

    public async ValueTask DisposeAsync()
    {
        await _conexao.DisposeAsync();
        _http.Dispose();
    }

    private sealed record EstadoLocal(string? Status, long UltimaSequencia, string? Etag);
}
