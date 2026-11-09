using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
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
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ITarefaServico, TarefaServicoMemoria>();

var app = builder.Build();

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

app.MapTarefasEndpoints();       // 9. endpoints Minimal API
app.MapDiagnosticoEndpoints();
app.MapControllers();            //    controllers com rota por atributo (API)
app.MapControllerRoute(          //    rota convencional da interface MVC
    name: "default",
    pattern: "{controller=Tarefas}/{action=Index}/{id:int?}");

app.Run();
