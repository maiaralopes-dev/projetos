using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HubPedidos.Application.Contratos;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HubPedidos.Tests.Importacao;

public class LoteEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Lote = """
        CLI-001;Sudeste;Normal;CANETA:10:2.50|LAPIS:5:1.20
        JOÃO-ÇÃ;Sul;Urgente;CAFÉ:1:19.90

        CLI-003;Marte;Normal;X:1:1
        linha-quebrada;sem campos
        """;

    [Fact]
    public async Task Lote_misto_retorna_um_resultado_por_linha_em_ndjson()
    {
        var resposta = await factory.CreateClient().PostAsync("/pedidos/lote", new StringContent(Lote, Encoding.UTF8, "text/plain"));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("application/x-ndjson", resposta.Content.Headers.ContentType!.MediaType);
        var linhas = await LerNdjsonAsync(resposta);
        Assert.Collection(linhas,
            l => Assert.Equal((1, "aceito", "Padrao"), (l.Linha, l.Status, l.Categoria)),
            l => Assert.Equal((2, "aceito", "Expresso"), (l.Linha, l.Status, l.Categoria)),
            l => Assert.Equal((4, "rejeitado"), (l.Linha, l.Status)),
            l => Assert.Equal((5, "rejeitado", "Esperados 4 campos separados por ';'."), (l.Linha, l.Status, l.Erros![0])));
        Assert.StartsWith("regiao:", linhas[2].Erros![0]);
    }

    [Fact]
    public async Task Content_length_acima_do_limite_retorna_413_sem_ler_o_corpo()
    {
        var client = factory.WithWebHostBuilder(b => b.UseSetting("Importacao:LimiteBytes", "50")).CreateClient();

        var resposta = await client.PostAsync("/pedidos/lote", new StringContent(Lote, Encoding.UTF8, "text/plain"));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, resposta.StatusCode);
    }

    [Fact]
    public async Task Corpo_chunked_acima_do_limite_termina_com_linha_abortado()
    {
        var client = factory.WithWebHostBuilder(b => b.UseSetting("Importacao:LimiteBytes", "60")).CreateClient();
        // Stream não pesquisável: o HttpClient não consegue calcular o tamanho e envia chunked.
        var conteudo = new StreamContent(new StreamGotejador(Encoding.UTF8.GetBytes(Lote), 32));

        using var requisicao = new HttpRequestMessage(HttpMethod.Post, "/pedidos/lote") { Content = conteudo };
        requisicao.Headers.TransferEncodingChunked = true;

        var resposta = await client.SendAsync(requisicao);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode); // a resposta já começou quando o limite estourou
        var ultima = (await LerNdjsonAsync(resposta))[^1];
        Assert.Equal("abortado", ultima.Status);
        Assert.Contains("limite de 60 bytes", ultima.Erros![0]);
    }

    private static async Task<List<ResultadoImportacaoResponse>> LerNdjsonAsync(HttpResponseMessage resposta)
    {
        var texto = await resposta.Content.ReadAsStringAsync();
        return [.. texto.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => JsonSerializer.Deserialize<ResultadoImportacaoResponse>(l, JsonSerializerOptions.Web)!)];
    }
}
