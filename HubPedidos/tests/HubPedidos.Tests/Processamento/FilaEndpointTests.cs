using HubPedidos.Processamento;
using System.Net;
using System.Net.Http.Json;
using HubPedidos.Application.Contratos;
using Microsoft.AspNetCore.Hosting;

namespace HubPedidos.Tests.Processamento;

public class FilaEndpointTests(HubApiFactory factory) : IClassFixture<HubApiFactory>
{
    private static CriarPedidoRequest Pedido() => new("CLI-FILA", "Sul", "Normal", [new("x", 1, 10m)]);

    [Fact]
    public async Task Pedido_enfileirado_e_processado_e_gravado_como_concluido()
    {
        var client = factory.CreateClient();

        var resposta = await client.PostAsJsonAsync("/pedidos/fila", Pedido());

        Assert.Equal(HttpStatusCode.Accepted, resposta.StatusCode);
        var corpo = await resposta.Content.ReadFromJsonAsync<PedidoEnfileiradoResponse>();
        Assert.Equal($"/pedidos/{corpo!.Id}", resposta.Headers.Location!.OriginalString);

        PedidoDetalheResponse? detalhe = null;
        await Bancada.AguardarAsync(async () =>
        {
            // 404 até o consumidor gravar: a criação é assíncrona (consistência eventual).
            var r = await client.GetAsync($"/pedidos/{corpo.Id}");
            detalhe = r.IsSuccessStatusCode ? await r.Content.ReadFromJsonAsync<PedidoDetalheResponse>() : null;
            return detalhe?.Status == "Concluido";
        }, "pedido concluído");
        Assert.Equal("Padrao", detalhe!.Categoria);
    }

    [Fact]
    public async Task Fila_cheia_responde_503_com_retry_after_sem_gravar()
    {
        // Fila pausada (0 consumidores), capacidade 1, rejeição imediata.
        using var pausada = factory.WithWebHostBuilder(b => b
            .UseSetting("Fila:Consumidores", "0")
            .UseSetting("Fila:Capacidade", "1")
            .UseSetting("Fila:TempoMaximoEsperaMs", "0"));
        var client = pausada.CreateClient();

        var primeira = await client.PostAsJsonAsync("/pedidos/fila", Pedido());
        var segunda = await client.PostAsJsonAsync("/pedidos/fila", Pedido());

        Assert.Equal(HttpStatusCode.Accepted, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, segunda.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(1), segunda.Headers.RetryAfter!.Delta);

        var metricas = await client.GetFromJsonAsync<MetricasFilaResponse>("/fila/metricas");
        Assert.Equal((1, 1, 1L, 1L, 0L), (metricas!.Profundidade, metricas.Capacidade, metricas.Aceitos, metricas.RejeitadosFilaCheia, metricas.Descartados));
    }
}

public class NotificacoesEndpointTests(HubApiFactory factory) : IClassFixture<HubApiFactory>
{
    [Fact]
    public async Task Conclusao_pela_fila_aparece_nas_notificacoes_recentes()
    {
        var client = factory.CreateClient();
        var aceito = await (await client.PostAsJsonAsync("/pedidos/fila", new CriarPedidoRequest("CLI-N", "Norte", "Normal", [new("x", 1, 1m)])))
            .Content.ReadFromJsonAsync<PedidoEnfileiradoResponse>();

        await Bancada.AguardarAsync(async () =>
            (await client.GetFromJsonAsync<Notificacoes.NotificacaoResponse[]>("/notificacoes/recentes"))!
                .Any(n => n.PedidoId == aceito!.Id && n.Status == "Concluido"), "notificação de conclusão");
    }
}
