using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using PortalTarefas.Web.Dados;
using PortalTarefas.Web.Endpoints;
using PortalTarefas.Web.Filtros;
using PortalTarefas.Web.Infraestrutura;
using PortalTarefas.Web.Servicos;

var builder = WebApplication.CreateBuilder(args);

// ---------- Serviços ----------
const string PoliticaCorsFrontend = "frontend-dev";
var origensPermitidas = builder.Configuration.GetSection("Cors:Origens").Get<string[]>() ?? [];

builder.Services.AddCors(o => o.AddPolicy(PoliticaCorsFrontend, p => p
    .WithOrigins(origensPermitidas)          // somente origens explícitas
    .WithMethods("GET", "POST", "PUT", "DELETE")
    .WithHeaders("Content-Type", "X-Cliente-Id", "X-Correlation-Id")
    .WithExposedHeaders("X-Correlation-Id", "Location")));

builder.Services.AddProblemDetailsComCorrelacao();
builder.Services.AddExceptionHandler<TratadorExcecoesGlobal>();
builder.Services.AddValidation();             // validação de Data Annotations em Minimal APIs (.NET 10)
builder.Services.AddControllersWithViews(o =>
    o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));   // POST de formulários exige token antifalsificação
builder.Services.AddScoped<RegistroAcaoFilter>();
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;   // números são números no contrato JSON
});
builder.Services.AddSingleton(TimeProvider.System);

// Persistência: a connection string vem de User Secrets (dev) ou variável de ambiente (produção).
// Durante o build, a ferramenta GetDocument executa o Program só para gerar o documento OpenAPI (sem banco).
var gerandoOpenApi = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";
var conexao = builder.Configuration.GetConnectionString("PortalTarefas")
    ?? (gerandoOpenApi ? "Server=nao-usado" : null)
    ?? throw new InvalidOperationException(
        "Connection string 'PortalTarefas' não configurada. Use 'dotnet user-secrets set ConnectionStrings:PortalTarefas ...' " +
        "ou a variável de ambiente ConnectionStrings__PortalTarefas.");
builder.Services.AddDbContextPool<PortalTarefasDbContext>(o => o
    .UseSqlServer(conexao, sql => sql.EnableRetryOnFailure(maxRetryCount: 3))
    .UseSeeding((ctx, _) => SementeDados.Semear(ctx))
    .UseAsyncSeeding((ctx, _, ct) => SementeDados.SemearAsync(ctx, ct)),
    poolSize: 128);
builder.Services.AddScoped<ITarefaServico, TarefaServicoEf>();
builder.Services.AddScoped<PainelServico>();
builder.Services.AddScoped<ConsultaTarefasServico>();

// OpenAPI 3.1 nativo do ASP.NET Core 10: um documento por versão da API.
builder.Services.AddOpenApi("v2", o => o.AddDocumentTransformer((doc, _, _) =>
{
    doc.Servers = [];   // o cliente define a URL base; evita fixar o endereço da máquina de build
    doc.Info.Title = "Portal de Tarefas API";
    doc.Info.Version = "v2";
    doc.Info.Description = "API REST do Portal de Tarefas. Erros seguem Problem Details (RFC 9457).";
    return Task.CompletedTask;
}).ShouldInclude = d => d.RelativePath?.StartsWith("api/v2") == true);
builder.Services.Configure<Microsoft.AspNetCore.OpenApi.OpenApiOptions>("v2", o =>
    // Nomes de esquema legíveis para tipos genéricos (PaginaDto<TarefaResumoDto> -> PaginaTarefaResumo).
    o.CreateSchemaReferenceId = tipo => tipo.Type.IsGenericType && tipo.Type.GetGenericTypeDefinition() == typeof(PortalTarefas.Web.Contratos.PaginaDto<>)
        ? "Pagina" + tipo.Type.GetGenericArguments()[0].Name.Replace("Dto", "")
        : Microsoft.AspNetCore.OpenApi.OpenApiOptions.CreateDefaultSchemaReferenceId(tipo));

var app = builder.Build();

// Em desenvolvimento (ou quando configurado), aplica migrations pendentes e executa o seed idempotente.
if (app.Configuration.GetValue<bool>("Banco:AplicarMigrations"))
{
    await using var escopo = app.Services.CreateAsyncScope();
    await escopo.ServiceProvider.GetRequiredService<PortalTarefasDbContext>().Database.MigrateAsync();
}

// ---------- Pipeline (a ordem importa) ----------
app.UseCorrelationId();          // 1. gera/propaga X-Correlation-Id e abre escopo de log
app.UseRegistroRequisicoes();    // 2. mede método, caminho, status e duração (inclusive de erros)
app.UseExceptionHandler();       // 3. converte exceções em Problem Details 500
app.UseStatusCodePages();        // 4. respostas 4xx/5xx sem corpo viram Problem Details
if (!app.Environment.IsDevelopment())
    app.UseHsts();
app.UseHttpsRedirection();       // 5. redireciona HTTP -> HTTPS
app.UseStaticFiles();            // 6. arquivos de wwwroot (encerra a requisição se encontrar o arquivo)
app.UseRouting();                // 7. seleciona o endpoint
app.UseCors(PoliticaCorsFrontend); // 8. CORS entre roteamento e endpoints

app.MapTarefasEndpoints();       // 9. endpoints Minimal API (v1, unidade 1)
app.MapTarefasApiV2();           //    API REST v2 (unidade 4)
app.MapOpenApi();                //    /openapi/v2.json — contrato publicado em todos os ambientes
if (app.Environment.IsDevelopment())
    app.MapScalarApiReference(o => o.WithTitle("Portal de Tarefas API"));   // UI interativa só em desenvolvimento
app.MapDiagnosticoEndpoints();
app.MapPainelEndpoints();
if (app.Environment.IsDevelopment())
    app.MapEndpointsDesenvolvimento();
app.MapControllers();            //    controllers com rota por atributo (API)
app.MapControllerRoute(          //    rota convencional da interface MVC
    name: "default",
    pattern: "{controller=Tarefas}/{action=Index}/{id:int?}");

app.Run();
