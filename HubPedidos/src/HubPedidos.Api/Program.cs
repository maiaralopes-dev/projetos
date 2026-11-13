using HubPedidos.Api.Modulos.Notificacoes;
using HubPedidos.Api.Modulos.Pedidos;
using HubPedidos.Api.Http;
using HubPedidos.Api.Modulos.Processamento;
using HubPedidos.Api.Seguranca;
using HubPedidos.Application;
using HubPedidos.Infrastructure;
using HubPedidos.Notificacoes;
using HubPedidos.Processamento;
using HubPedidos.Processamento.Fila;

// COMPOSITION ROOT: o único lugar que conhece todos os módulos e a infraestrutura.
// Cada módulo registra seus serviços na própria extensão; aqui só se compõe.
var builder = WebApplication.CreateBuilder(args);

// Falhar na inicialização (e não na primeira requisição) se o grafo de DI estiver
// errado, inclusive serviço scoped capturado por singleton.
builder.Host.UseDefaultServiceProvider(o =>
{
    o.ValidateOnBuild = true;
    o.ValidateScopes = true;
});

builder.Services
    .AddModuloPedidos(builder.Configuration)
    .AddModuloProcessamento(builder.Configuration)
    .AddModuloNotificacoes()
    .AddInfraestrutura(builder.Configuration)
    .AddProblemDetails();

builder.Services.AddVersionamentoApi();
builder.Services.AddAutenticacaoHub(builder.Configuration, builder.Environment);
builder.Services.AddCachePublico(builder.Configuration);
builder.Services.Configure<HostOptions>(o =>
    o.ShutdownTimeout = TimeSpan.FromSeconds((builder.Configuration.GetSection("Fila").Get<OpcoesFila>() ?? new()).PrazoDrenagemSegundos));

var app = builder.Build();

await app.Services.PrepararBancoAsync(app.Configuration);

// Ordem importa: o cache só entra DEPOIS de autenticar/autorizar, para nunca servir a
// alguém uma resposta que ele não poderia ver.
app.UseAuthentication();
app.UseAuthorization();
app.UseOutputCache();

app.UseVersionamentoApi();
app.MapModuloPedidos();
app.MapModuloProcessamento();
app.MapModuloNotificacoes(app.Environment);

app.Run();

/// <summary>Exposto para WebApplicationFactory nos testes.</summary>
public partial class Program;
