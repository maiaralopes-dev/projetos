using System.Net;
using System.Net.Http.Json;
using HubPedidos.Application.Contratos;
using HubPedidos.Domain.Pedidos;
using HubPedidos.Infrastructure.Persistencia;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HubPedidos.Tests.Integracao;

public class FabricaTests
{
    [Fact]
    public void Banco_de_arquivo_e_recusado_no_ambiente_de_teste()
    {
        using var f = new HubApiFactory();
        using var errada = f.WithWebHostBuilder(b => b.UseSetting("ConnectionStrings:Hub", "Data Source=hubpedidos.db"));

        var ex = Assert.Throws<InvalidOperationException>(() => errada.Services);
        Assert.Contains("Banco:ExigirEmMemoria", ex.Message);
    }

    [Fact]
    public async Task Cada_fabrica_tem_banco_proprio_e_vazio_com_esquema_migrado()
    {
        await using var a = new HubApiFactory();
        await using var b = new HubApiFactory();
        await a.CreateClient().PostAsJsonAsync("/pedidos", new CriarPedidoRequest("CLI-1", "Sul", "Normal", [new("x", 1, 1m)]));

        int ContarPedidos(HubApiFactory f)
        {
            using var escopo = f.Services.CreateScope();
            var db = escopo.ServiceProvider.GetRequiredService<HubDbContext>();
            Assert.Empty(db.Database.GetPendingMigrations()); // esquema via migrations reais
            return db.Pedidos.Count();
        }

        Assert.Equal(1, ContarPedidos(a));
        Assert.Equal(0, ContarPedidos(b)); // nenhum estado compartilhado entre fábricas
    }

    [Fact]
    public async Task Cliente_http_nao_segue_redirecionamento_nem_guarda_cookies()
    {
        await using var f = new HubApiFactory();

        Assert.False(f.ClientOptions.AllowAutoRedirect);
        Assert.False(f.ClientOptions.HandleCookies);
        // Sem token explícito, rota protegida responde 401 (não há credencial "ambiente").
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateClient().GetAsync($"/api/v2/pedidos/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Fluxo_assincrono_da_fila_e_observado_sem_polling()
    {
        await using var f = new HubApiFactory();
        var http = f.CreateClient();

        var resposta = await http.PostAsJsonAsync("/pedidos/fila", new CriarPedidoRequest("CLI-F", "Norte", "Normal", [new("x", 1, 1m)]));
        var id = (await resposta.Content.ReadFromJsonAsync<PedidoEnfileiradoResponseLocal>())!.Id;

        // Espera o EVENTO certo (com prazo), em vez de consultar em laço com sleep.
        var concluido = await f.Notificacoes.AguardarAsync(e => e.PedidoId == id && e.Status == StatusPedido.Concluido);

        Assert.Equal("CLI-F", concluido.ClienteId);
        var detalhe = await http.GetFromJsonAsync<PedidoDetalheResponse>($"/pedidos/{id}");
        Assert.Equal("Concluido", detalhe!.Status); // gravado antes da notificação
    }

    private sealed record PedidoEnfileiradoResponseLocal(Guid Id, string Categoria, string Status);
}
