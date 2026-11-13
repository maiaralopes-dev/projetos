using System.Net.Http.Json;
using HubPedidos.Application.Contratos;
using HubPedidos.Infrastructure.Persistencia;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HubPedidos.Tests.Http;

/// <summary>Cada teste usa uma API própria: o cache é estado global da instância.</summary>
public class OutputCacheTests
{
    private static async Task<int> PedidosNoPainelAsync(HttpClient http) =>
        (await http.GetFromJsonAsync<ResumoRegiaoResponse[]>("/painel/regioes"))!.Sum(r => r.Pedidos);

    private static int ConsultasDoPainel(HubApiFactory f) => f.Contador.Comandos.Count(c => c.Contains("GROUP BY"));

    private static async Task CriarAsync(HttpClient http) =>
        (await http.PostAsJsonAsync("/pedidos", new CriarPedidoRequest("CLI-C", "Norte", "Normal", [new("x", 1, 1m)]))).EnsureSuccessStatusCode();

    [Fact]
    public async Task Segunda_leitura_vem_do_cache_sem_tocar_no_banco()
    {
        await using var f = new HubApiFactory();
        var http = f.CreateClient();
        await CriarAsync(http);

        f.Contador.Limpar();
        var primeira = await http.GetAsync("/painel/regioes");
        var segunda = await http.GetAsync("/painel/regioes");

        Assert.Equal(2, ConsultasDoPainel(f));            // só a primeira foi ao banco (2 GROUP BY)
        Assert.True(segunda.Headers.Age.HasValue);        // "Age" marca resposta servida do cache
        Assert.False(primeira.Headers.Age.HasValue);
        Assert.Equal(await primeira.Content.ReadAsStringAsync(), await segunda.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Mudanca_de_pedido_invalida_pela_tag()
    {
        await using var f = new HubApiFactory();
        var http = f.CreateClient();
        await CriarAsync(http);
        var antes = await PedidosNoPainelAsync(http);

        await CriarAsync(http); // notificador → CanalInvalidacaoCache → EvictByTag("painel")

        Assert.Equal(antes + 1, await PedidosNoPainelAsync(http));
    }

    [Fact]
    public async Task Mudanca_fora_do_fluxo_da_aplicacao_fica_invisivel_ate_expirar()
    {
        // Risco de inconsistência documentado: alteração direta no banco (ou por OUTRA
        // instância) não passa pelo notificador desta instância, então nada é invalidado.
        await using var f = new HubApiFactory();
        var http = f.CreateClient();
        await CriarAsync(http);
        var antes = await PedidosNoPainelAsync(http);

        using (var escopo = f.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<HubDbContext>();
            await db.Database.ExecuteSqlRawAsync("DELETE FROM ItensPedido; DELETE FROM Pedidos;");
        }

        Assert.Equal(antes, await PedidosNoPainelAsync(http)); // ainda o valor antigo (até 30 s)
    }

    [Fact]
    public async Task Requisicao_autenticada_nao_e_guardada_nem_servida_do_cache()
    {
        await using var f = new HubApiFactory();
        var autenticado = f.ClienteAutenticado("CLI-C");
        await CriarAsync(autenticado);

        f.Contador.Limpar();
        await autenticado.GetAsync("/painel/regioes");
        var segunda = await autenticado.GetAsync("/painel/regioes");

        Assert.Equal(4, ConsultasDoPainel(f)); // as duas foram ao banco
        Assert.False(segunda.Headers.Age.HasValue);
    }

    [Fact]
    public async Task Cache_pode_ser_desligado_por_configuracao()
    {
        await using var f = new HubApiFactory();
        using var semCache = f.WithWebHostBuilder(b => b.UseSetting("Cache:Habilitado", "false"));
        var http = semCache.CreateClient();

        f.Contador.Limpar();
        await http.GetAsync("/painel/regioes");
        await http.GetAsync("/painel/regioes");

        Assert.Equal(4, ConsultasDoPainel(f));
    }

    [Fact]
    public async Task Detalhe_de_pedido_nunca_e_compartilhado_entre_usuarios()
    {
        await using var f = new HubApiFactory();
        var dono = f.ClienteAutenticado("CLI-D");
        var r = await dono.PostAsJsonAsync("/pedidos", new CriarPedidoRequest("CLI-D", "Sul", "Normal", [new("x", 1, 1m)]));
        var id = (await r.Content.ReadFromJsonAsync<PedidoResponse>())!.Id;

        Assert.Equal(System.Net.HttpStatusCode.OK, (await dono.GetAsync($"/api/v2/pedidos/{id}")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await f.ClienteAutenticado("CLI-INTRUSO").GetAsync($"/api/v2/pedidos/{id}")).StatusCode);
    }
}
