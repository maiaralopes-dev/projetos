using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using PortalTarefas.Web.Contratos;
using PortalTarefas.Web.Controllers;
using PortalTarefas.Web.Modelos;
using PortalTarefas.Web.Servicos;
using PortalTarefas.Web.ViewModels;

namespace PortalTarefas.Tests;

internal sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => agora;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

internal sealed class AutorizacaoSempreConcede : Microsoft.AspNetCore.Authorization.IAuthorizationService
{
    public Task<Microsoft.AspNetCore.Authorization.AuthorizationResult> AuthorizeAsync(System.Security.Claims.ClaimsPrincipal user, object? resource,
        IEnumerable<Microsoft.AspNetCore.Authorization.IAuthorizationRequirement> requirements) =>
        Task.FromResult(Microsoft.AspNetCore.Authorization.AuthorizationResult.Success());
    public Task<Microsoft.AspNetCore.Authorization.AuthorizationResult> AuthorizeAsync(System.Security.Claims.ClaimsPrincipal user, object? resource, string policyName) =>
        Task.FromResult(Microsoft.AspNetCore.Authorization.AuthorizationResult.Success());
}

internal sealed class TempDataFalso : ITempDataProvider
{
    public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
    public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
}

/// <summary>Testes unitários da validação customizada (regra com mais de um campo).</summary>
public class Atividade2ValidacaoTests
{
    private static readonly DateOnly Hoje = new(2026, 11, 9);

    private static List<ValidationResult> Validar(TarefaInputModel m)
    {
        var resultados = new List<ValidationResult>();
        Validator.TryValidateObject(m, new ValidationContext(m), resultados, validateAllProperties: true);
        return resultados;
    }

    [Fact]
    public void Titulo_vazio_e_invalido() =>
        Assert.Contains(Validar(new TarefaInputModel { Titulo = "", Hoje = Hoje }), r => r.MemberNames.Contains("Titulo"));

    [Fact]
    public void Prioridade_alta_sem_prazo_e_invalida()
    {
        var erros = Validar(new TarefaInputModel { Titulo = "Urgente", Prioridade = PrioridadeTarefa.Alta, Hoje = Hoje });
        Assert.Contains(erros, r => r.ErrorMessage == "Tarefas de prioridade Alta precisam de prazo.");
    }

    [Fact]
    public void Prazo_no_passado_so_e_aceito_para_tarefa_concluida()
    {
        var pendente = new TarefaInputModel { Titulo = "Antiga", Prazo = Hoje.AddDays(-1), Hoje = Hoje };
        var concluida = new TarefaInputModel { Titulo = "Antiga", Prazo = Hoje.AddDays(-1), Situacao = SituacaoTarefa.Concluida, Hoje = Hoje };
        Assert.Contains(Validar(pendente), r => r.MemberNames.Contains("Prazo"));
        Assert.Empty(Validar(concluida));
    }

    [Fact]
    public void Entrada_valida_nao_gera_erros() =>
        Assert.Empty(Validar(new TarefaInputModel { Titulo = "Válida", Prioridade = PrioridadeTarefa.Alta, Prazo = Hoje.AddDays(3), Hoje = Hoje }));
}

/// <summary>Testes unitários do controller (sem servidor HTTP).</summary>
public class Atividade2ControllerTests
{
    private readonly ITarefaServico _servico;
    private readonly TarefasController _controller;

    public Atividade2ControllerTests()
    {
        var relogio = new RelogioFixo(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero));
        _servico = new TarefaServicoMemoria(relogio);
        var usuario = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "Administrador")], "teste"));
        _controller = new TarefasController(_servico, relogio, new AutorizacaoSempreConcede())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = usuario } },
        };
        _controller.TempData = new TempDataDictionary(_controller.HttpContext, new TempDataFalso());
    }

    [Fact]
    public async Task Details_inexistente_retorna_view_404()
    {
        var resultado = Assert.IsType<ViewResult>(await _controller.Details(999, default));
        Assert.Equal("NaoEncontrada", resultado.ViewName);
        Assert.Equal(404, resultado.StatusCode);
    }

    [Fact]
    public async Task Create_com_modelstate_invalido_volta_para_view_sem_gravar()
    {
        var antes = (await _servico.ListarAsync(null, null)).Count;
        _controller.ModelState.AddModelError("Titulo", "obrigatório");
        var entrada = new TarefaInputModel { Titulo = "" };
        var resultado = Assert.IsType<ViewResult>(await _controller.Create(entrada, default));
        Assert.Same(entrada, resultado.Model);    // dados digitados preservados
        Assert.Equal(antes, (await _servico.ListarAsync(null, null)).Count);
    }

    [Fact]
    public async Task Create_com_titulo_repetido_e_rejeitado_pela_validacao_assincrona()
    {
        var resultado = await _controller.Create(new TarefaInputModel { Titulo = "escrever RELATÓRIO 1" }, default);
        Assert.IsType<ViewResult>(resultado);
        Assert.True(_controller.ModelState.ContainsKey("Titulo"));
    }

    [Fact]
    public async Task Create_valido_redireciona_para_detalhes()
    {
        var resultado = Assert.IsType<RedirectToActionResult>(
            await _controller.Create(new TarefaInputModel { Titulo = "Nova pelo teste" }, default));
        Assert.Equal(nameof(TarefasController.Details), resultado.ActionName);
    }
}

/// <summary>Testes de integração das páginas Razor (layout, antiforgery, validação).</summary>
public class Atividade2PaginasTests(PortalWebFactory factory) : IClassFixture<PortalWebFactory>
{
    private readonly HttpClient _client = factory.CriarCliente("carla", seguirRedirecionamentos: false);

    [Theory]
    [InlineData("/")]
    [InlineData("/Tarefas/Details/1")]
    [InlineData("/Tarefas/Create")]
    [InlineData("/Tarefas/Edit/2")]
    public async Task Paginas_compartilham_layout(string url)
    {
        var html = await _client.GetStringAsync(url);
        Assert.Contains("<header class=\"topo\">", html);
        Assert.Contains("Tarefas vencidas", html);   // View Component do layout
    }

    [Fact]
    public async Task Post_sem_token_antiforgery_e_rejeitado()
    {
        var resp = await _client.PostAsync("/Tarefas/Create", new FormUrlEncodedContent(new Dictionary<string, string> { ["Titulo"] = "x" }));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Post_com_titulo_vazio_reexibe_formulario_com_mensagem()
    {
        var form = await _client.GetStringAsync("/Tarefas/Create");
        var token = Regex.Match(form, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        var resp = await _client.PostAsync("/Tarefas/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Titulo"] = "", ["Descricao"] = "texto preservado", ["Prioridade"] = "1", ["Situacao"] = "0",
            ["__RequestVerificationToken"] = token
        }));
        var html = WebUtility.HtmlDecode(await resp.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("Informe o título da tarefa.", html);
        Assert.Contains("texto preservado", html);
    }
}
