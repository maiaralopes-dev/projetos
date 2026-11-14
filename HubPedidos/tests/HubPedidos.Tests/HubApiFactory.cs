using HubPedidos.Infrastructure.Persistencia;
using HubPedidos.Notificacoes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace HubPedidos.Tests;

/// <summary>
/// A aplicação REAL (Program) em memória, com infraestrutura substituída de forma explícita:
/// <list type="bullet">
/// <item>Ambiente "Testing": não carrega appsettings.Development (que semeia 20 mil pedidos)
/// e gera uma chave JWT aleatória por execução.</item>
/// <item>Banco SQLite EM MEMÓRIA exclusivo desta fábrica (nome aleatório), mantido vivo por
/// uma conexão-âncora; migrations aplicadas na inicialização; Banco:ExigirEmMemoria impede
/// que um connection string de arquivo seja usado por engano.</item>
/// <item>Interceptor que conta comandos SQL e um canal que captura notificações
/// (observação determinística do fluxo assíncrono).</item>
/// <item>Cliente HTTP sem redirecionamento automático e sem cookies: cada teste declara a
/// própria autenticação (Bearer) — nada passa por acidente.</item>
/// </list>
/// </summary>
public sealed class HubApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString = $"Data Source=hub-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _ancora;

    public HubApiFactory()
    {
        _ancora = new SqliteConnection(_connectionString);
        _ancora.Open();
        ClientOptions.AllowAutoRedirect = false;
        ClientOptions.HandleCookies = false;
    }

    public ContadorComandos Contador { get; } = new();

    public CanalCaptura Notificacoes { get; } = new();

    /// <summary>Emite tokens com a chave desta instância (gerada por execução no ambiente Testing).</summary>
    public string Token(string? clienteId, bool operador = false) =>
        Services.GetRequiredService<HubPedidos.Api.Seguranca.EmissorTokens>().Emitir(clienteId, operador);

    public HttpClient ClienteAutenticado(string? clienteId, bool operador = false)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token(clienteId, operador));
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Hub", _connectionString);
        builder.UseSetting("Banco:ExigirEmMemoria", "true");
        builder.UseSetting("Banco:AplicarMigrations", "true");
        builder.UseSetting("Banco:SemearPedidos", "0");
        builder.ConfigureTestServices(s =>
        {
            s.AddSingleton<IInterceptor>(Contador);
            s.AddSingleton<ICanalNotificacao>(Notificacoes);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _ancora.Dispose();
        }
    }
}

/// <summary>
/// Canal de notificação de TESTE: registra cada evento e permite aguardar um evento
/// específico com prazo, sem polling nem atraso fixo.
/// </summary>
public sealed class CanalCaptura : ICanalNotificacao
{
    private readonly Lock _trava = new();
    private readonly List<global::HubPedidos.Processamento.PedidoAtualizado> _eventos = [];
    private readonly List<(Func<global::HubPedidos.Processamento.PedidoAtualizado, bool> Condicao, TaskCompletionSource<global::HubPedidos.Processamento.PedidoAtualizado> Tcs)> _esperas = [];

    public IReadOnlyList<global::HubPedidos.Processamento.PedidoAtualizado> Eventos
    {
        get
        {
            lock (_trava)
            {
                return [.. _eventos];
            }
        }
    }

    public ValueTask PublicarAsync(global::HubPedidos.Processamento.PedidoAtualizado evento, CancellationToken cancellationToken)
    {
        lock (_trava)
        {
            _eventos.Add(evento);
            foreach (var espera in _esperas.Where(e => e.Condicao(evento)).ToList())
            {
                espera.Tcs.TrySetResult(evento);
                _esperas.Remove(espera);
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Completa quando (ou se já) chegou um evento que satisfaz a condição.</summary>
    public Task<global::HubPedidos.Processamento.PedidoAtualizado> AguardarAsync(Func<global::HubPedidos.Processamento.PedidoAtualizado, bool> condicao, TimeSpan? prazo = null)
    {
        lock (_trava)
        {
            if (_eventos.FirstOrDefault(condicao) is { } existente)
            {
                return Task.FromResult(existente);
            }

            var tcs = new TaskCompletionSource<global::HubPedidos.Processamento.PedidoAtualizado>(TaskCreationOptions.RunContinuationsAsynchronously);
            _esperas.Add((condicao, tcs));
            return tcs.Task.WaitAsync(prazo ?? TimeSpan.FromSeconds(10));
        }
    }
}
