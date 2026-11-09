using System.Net;
using System.Net.Http.Json;

namespace PortalTarefas.Tests;

/// <summary>Fábrica com limites baixos, para testar a política de rate limiting de forma determinística.</summary>
public class PortalWebFactoryLimitesBaixos : PortalWebFactory
{
    protected override IEnumerable<(string, string)> Configuracoes =>
        [("RateLimit:Api:Limite", "5"), ("RateLimit:Api:JanelaSegundos", "60"), ("RateLimit:Publica:Limite", "10000"), ("RateLimit:Login:Limite", "3")];
    protected override string UrlCalendario => "http://127.0.0.1:9/";   // porta sem serviço: dependência indisponível
}

public class Atividade7OperacaoTests(PortalWebFactoryLimitesBaixos factory) : IClassFixture<PortalWebFactoryLimitesBaixos>
{
    [Fact]
    public async Task Excesso_de_requisicoes_retorna_429_com_retry_after_por_usuario()
    {
        var ana = factory.CriarCliente("ana");
        var codigos = new List<HttpStatusCode>();
        for (var i = 0; i < 7; i++) codigos.Add((await ana.GetAsync("/api/v2/tarefas?tamanho=1")).StatusCode);
        Assert.Equal(5, codigos.Count(c => c == HttpStatusCode.OK));
        Assert.Equal(2, codigos.Count(c => c == HttpStatusCode.TooManyRequests));

        var rejeitada = await ana.GetAsync("/api/v2/tarefas?tamanho=1");
        Assert.True(rejeitada.Headers.Contains("Retry-After"));
        Assert.Equal("application/problem+json", rejeitada.Content.Headers.ContentType?.MediaType);

        // A cota é por usuário: outro usuário ainda consegue acessar.
        Assert.Equal(HttpStatusCode.OK, (await factory.CriarCliente("bruno").GetAsync("/api/v2/tarefas?tamanho=1")).StatusCode);
    }

    [Fact]
    public async Task Liveness_saudavel_e_readiness_indisponivel_sem_a_dependencia()
    {
        var anonimo = factory.CriarCliente(perfil: null);
        var live = await anonimo.GetAsync("/health/live");
        var ready = await anonimo.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Equal("Unhealthy", await ready.Content.ReadAsStringAsync());   // público: só o status, sem detalhes
    }

    [Fact]
    public async Task Detalhes_de_saude_exigem_administrador()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CriarCliente(perfil: null).GetAsync("/health/detalhes")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CriarCliente("ana").GetAsync("/health/detalhes")).StatusCode);
        var detalhes = await factory.CriarCliente("carla").GetAsync("/health/detalhes");
        Assert.Contains("calendario", await detalhes.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Dependencia_fora_retorna_problem_details_sem_detalhe_interno()
    {
        var resp = await factory.CriarCliente(perfil: null).GetAsync("/api/v2/calendario/feriados/2026");
        Assert.True(resp.StatusCode is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout);
        var texto = await resp.Content.ReadAsStringAsync();
        Assert.DoesNotContain("127.0.0.1", texto);
    }
}
