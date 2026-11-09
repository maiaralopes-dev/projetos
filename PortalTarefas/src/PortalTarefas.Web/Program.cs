using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using PortalTarefas.Web.Observabilidade;
using Scalar.AspNetCore;
using PortalTarefas.Web.Dados;
using PortalTarefas.Web.Endpoints;
using PortalTarefas.Web.Filtros;
using PortalTarefas.Web.Seguranca;
using PortalTarefas.Web.Infraestrutura;
using PortalTarefas.Web.Servicos;

var builder = WebApplication.CreateBuilder(args);

// ---------- Observabilidade (unidade 7) ----------
builder.AddObservabilidade();

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

// ---------- Identidade e autorização ----------
// Esquema escolhido: cookie do Identity. A SPA é servida na mesma origem da API (proxy do Vite em dev,
// arquivos estáticos da API em produção), então o cookie HttpOnly evita expor token ao JavaScript.
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
builder.Services.AddIdentityCore<IdentityUser>(o =>
    {
        o.Password.RequiredLength = 10;               // requisitos mantidos (nada desativado silenciosamente)
        o.Password.RequireNonAlphanumeric = true;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
        o.Lockout.AllowedForNewUsers = true;
        o.User.RequireUniqueEmail = true;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<PortalTarefasDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = "portal.sessao";
    o.Cookie.HttpOnly = true;                              // inacessível a scripts
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;     // somente HTTPS
    o.Cookie.SameSite = SameSiteMode.Strict;               // não é enviado em navegação/requisição de outro site
    o.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    o.SlidingExpiration = true;
    o.LoginPath = "/Conta/Login";
    o.AccessDeniedPath = "/Conta/AcessoNegado";
    // Para a API, nada de redirecionar para HTML: 401 (não autenticado) e 403 (sem permissão).
    o.Events.OnRedirectToLogin = ctx => ResponderOuRedirecionar(ctx, StatusCodes.Status401Unauthorized);
    o.Events.OnRedirectToAccessDenied = ctx => ResponderOuRedirecionar(ctx, StatusCodes.Status403Forbidden);
    static Task ResponderOuRedirecionar(Microsoft.AspNetCore.Authentication.RedirectContext<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions> ctx, int status)
    {
        if (ctx.Request.Path.StartsWithSegments("/api") || ctx.Request.Path.StartsWithSegments("/health")) ctx.Response.StatusCode = status;
        else ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    }
});
// O security stamp é conferido a cada requisição: após logout (troca do stamp) um cookie copiado deixa de valer.
// Custo: uma consulta ao banco por requisição autenticada — aceitável neste projeto, configurável em produção.
builder.Services.Configure<SecurityStampValidatorOptions>(o =>
    o.ValidationInterval = builder.Configuration.GetValue("Seguranca:IntervaloValidacaoStamp", TimeSpan.Zero));

Politicas.Registrar(builder.Services.AddAuthorizationBuilder());
builder.Services.AddSingleton<IAuthorizationHandler, EditarTarefaHandler>();
builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "X-XSRF-TOKEN";
    o.Cookie.Name = "portal.af";
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    o.Cookie.SameSite = SameSiteMode.Strict;
});
builder.Services.AddScoped<AntiforgeryApiFilter>();

// ---------- Dependência externa com resiliência (unidade 7) ----------
var urlCalendario = builder.Configuration["Dependencias:Calendario:Url"] ?? "http://localhost:5099/";
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<PropagarCorrelacaoHandler>();
builder.Services.AddHttpClient<CalendarioCliente>(c => c.BaseAddress = new Uri(urlCalendario))
    .AddHttpMessageHandler<PropagarCorrelacaoHandler>()
    .AddResilienceHandler("calendario", p =>
    {
        // Ordem: tempo total > tentativas > disjuntor > tempo por tentativa (o mais interno).
        p.AddTimeout(TimeSpan.FromSeconds(8));
        var retry = new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 3,                       // finito
            BackoffType = DelayBackoffType.Exponential, // 200 ms, 400 ms, 800 ms (+ jitter)
            Delay = TimeSpan.FromMilliseconds(200),
            UseJitter = true,
            // Padrão: só falhas transitórias (5xx, 408, 429, erro de rede, timeout). 400/404 não são repetidos.
        };
        retry.DisableForUnsafeHttpMethods();            // nunca repete POST/PUT/PATCH/DELETE
        p.AddRetry(retry);
        p.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            MinimumThroughput = 4,
            SamplingDuration = TimeSpan.FromSeconds(30),
            BreakDuration = TimeSpan.FromSeconds(15),
        });
        p.AddTimeout(TimeSpan.FromSeconds(2));
    });
builder.Services.AddHttpClient("calendario-saude", c => { c.BaseAddress = new Uri(urlCalendario); c.Timeout = TimeSpan.FromSeconds(2); });

builder.Services.AddHealthChecks()
    .AddDbContextCheck<PortalTarefasDbContext>("banco", tags: ["ready"])
    .AddCheck<CalendarioHealthCheck>("calendario", tags: ["ready"]);

// ---------- Rate limiting e output caching (unidade 7) ----------
var limites = builder.Configuration.GetSection("RateLimit");
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = async (ctx, ct) =>
    {
        if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var espera))
            ctx.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(espera.TotalSeconds)).ToString();
        ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("RateLimiting")
            .LogWarning("Limite de requisições excedido em {Caminho}", ctx.HttpContext.Request.Path);
        await ctx.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new()
        {
            HttpContext = ctx.HttpContext,
            ProblemDetails = { Status = 429, Title = "Muitas requisições.", Detail = "Aguarde o tempo indicado em Retry-After." }
        });
    };
    // Usuário autenticado: cota por usuário; anônimo: cota por IP.
    static string Chave(HttpContext c) => c.User.Identity?.IsAuthenticated == true
        ? "u:" + c.User.Identity.Name : "ip:" + c.Connection.RemoteIpAddress;
    o.AddPolicy("api", c => RateLimitPartition.GetFixedWindowLimiter(Chave(c), _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = limites.GetValue("Api:Limite", 100), Window = TimeSpan.FromSeconds(limites.GetValue("Api:JanelaSegundos", 10)), QueueLimit = 0
    }));
    o.AddPolicy("publica", c => RateLimitPartition.GetFixedWindowLimiter(Chave(c), _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = limites.GetValue("Publica:Limite", 30), Window = TimeSpan.FromSeconds(limites.GetValue("Publica:JanelaSegundos", 10)), QueueLimit = 0
    }));
    o.AddPolicy("login", c => RateLimitPartition.GetFixedWindowLimiter("ip:" + c.Connection.RemoteIpAddress, _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = limites.GetValue("Login:Limite", 10), Window = TimeSpan.FromMinutes(1), QueueLimit = 0
    }));
});
builder.Services.AddOutputCache(o =>
    // Política base: só GET/HEAD 200 de requisições anônimas e sem cookie de resposta são armazenados.
    o.AddPolicy("feriados", b => b.Expire(TimeSpan.FromSeconds(60)).SetVaryByRouteValue("ano").Tag("feriados")));

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
    await SementeUsuarios.ExecutarAsync(escopo.ServiceProvider, app.Configuration, app.Logger);
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
app.UseAuthentication();         // 9. lê o cookie e monta HttpContext.User
app.UseRateLimiter();            // 10. cotas por usuário/IP (429) — depois da autenticação para particionar por usuário
app.UseAuthorization();          // 11. aplica as políticas do endpoint (401/403)
app.UseOutputCache();            // 12. serve/armazena respostas públicas em cache

app.MapOperacao();               // 13. health checks e calendário (unidade 7)
app.MapTarefasEndpoints();       //    endpoints Minimal API (v1, unidade 1)
app.MapTarefasApiV2();           //    API REST v2 (unidade 4)
app.MapAuthEndpoints().AddEndpointFilter<AntiforgeryApiFilter>().RequireRateLimiting("login");   // login/logout/sessão (unidade 6)
app.MapOpenApi().AllowAnonymous();   // /openapi/v2.json — contrato publicado em todos os ambientes
if (app.Environment.IsDevelopment())
    app.MapScalarApiReference(o => o.WithTitle("Portal de Tarefas API")).AllowAnonymous();   // UI só em desenvolvimento
app.MapDiagnosticoEndpoints();
app.MapPainelEndpoints();
if (app.Environment.IsDevelopment())
    app.MapEndpointsDesenvolvimento();
app.MapControllers();            //    controllers com rota por atributo (API)
app.MapControllerRoute(          //    rota convencional da interface MVC
    name: "default",
    pattern: "{controller=Tarefas}/{action=Index}/{id:int?}");

app.Run();
