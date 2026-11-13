using System.Net.Http.Json;
using HubPedidos.Application.Contratos;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace HubPedidos.Tests.TempoReal;

/// <summary>API em Kestrel REAL (porta TCP), necessária para o proxy de falhas e WebSockets.</summary>
public sealed class ServidorReal : IAsyncLifetime
{
    public HubApiFactory Fabrica { get; } = new();

    public Uri Endereco { get; private set; } = null!;

    public HttpClient Http { get; private set; } = null!;

    public Task InitializeAsync()
    {
        Fabrica.UseKestrel(0);
        Fabrica.StartServer();
        var endereco = Fabrica.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        Endereco = new Uri(endereco.Replace("[::1]", "127.0.0.1").Replace("localhost", "127.0.0.1"));
        Http = new HttpClient { BaseAddress = Endereco };
        return Task.CompletedTask;
    }

    public async Task<Guid> CriarPedidoAsync(string cliente)
    {
        var r = await Http.PostAsJsonAsync("/pedidos", new CriarPedidoRequest(cliente, "Sul", "Normal", [new("x", 1, 10m)]));
        r.EnsureSuccessStatusCode();
        return Guid.Parse((await r.Content.ReadFromJsonAsync<PedidoResponse>())!.Id);
    }

    public async Task AlterarStatusAsync(Guid id, string status) =>
        (await Http.PutAsJsonAsync($"/pedidos/{id}/status", new AlterarStatusRequest(status))).EnsureSuccessStatusCode();

    public string Token(string? cliente, bool operador = false) => Fabrica.Token(cliente, operador);

    public async Task DisposeAsync()
    {
        Http.Dispose();
        await Fabrica.DisposeAsync();
    }
}
