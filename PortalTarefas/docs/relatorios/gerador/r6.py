from base import Relatorio, REPO, acesso, trecho, bloco_entre

EV = "docs/evidencias/atividade6"
COMMIT = "6315a9c"


def gerar():
    r = Relatorio(6, "ASP.NET Core Identity, cookies, tokens bearer, JWT, roles, claims e rotas protegidas",
                  "Unidade 6 – Autenticação e autorização ponta a ponta",
                  "Autenticar usuários, aplicar autorização por política e proteger operações e rotas do front-end sem expor credenciais ou tokens.")
    r.capa()
    r.cabecalho_atividade()

    r.h1("1. Identificação e ambiente")
    r.p(f"Repositório: {REPO}; commit `{COMMIT}`. Pacote `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 10.0.14; migration "
        "`IdentidadeEquipes` (tabelas `AspNet*`, coluna `Tarefas.Equipe` e tabela `EventosSeguranca`). As senhas e cookies que "
        "aparecem nas evidências foram **mascarados** (`********`, `<valor omitido>`); a senha dos usuários de teste existe só em "
        "User Secrets/`.env` locais e uma verificação automática confirmou que ela não aparece em `docs/`, `src/`, `tests/` nem `scripts/`.")

    r.h1("2. Resumo teórico aplicado ao projeto")
    r.p("**Autenticação × autorização.** Autenticar é descobrir *quem* é o usuário (gera a **identidade**, um "
        "`ClaimsPrincipal`); autorizar é decidir *o que* ele pode fazer. **Claim** é uma afirmação sobre o usuário "
        "(`equipe=Backend`); **role** é uma claim especial de papel (`Usuario`, `Administrador`); **política** é um conjunto "
        "nomeado de requisitos. Quando falta autenticação o servidor faz **challenge** (401, ou redirecionamento para login no "
        "MVC); quando o usuário é conhecido mas não tem permissão faz **forbid** (403).")
    r.p("**ASP.NET Core Identity** gerencia usuários, *hash* de senhas (PBKDF2), papéis, claims, bloqueio por tentativas e o "
        "*security stamp*. Configuração: senha mínima de 10 caracteres com símbolo, bloqueio de 5 min após 5 falhas, e-mail único "
        "— nenhum controle foi desativado.")
    r.tabela_dados(["", "Cookie do Identity (escolhido)", "Token bearer do Identity", "JWT de provedor OAuth/OIDC"], [
        ["Origem/cliente", "SPA e MVC na **mesma origem** que a API", "Apps móveis/outros clientes da mesma API", "Várias APIs/clientes, SSO, terceiros"],
        ["Armazenamento", "Cookie `HttpOnly` (JS não lê)", "O cliente guarda o token (risco se em `localStorage`)", "Idem; mais *refresh token*"],
        ["CSRF", "Exige proteção (SameSite + token antiforgery)", "Não se aplica (cabeçalho manual)", "Não se aplica"],
        ["Revogação", "Logout troca o *security stamp* → cookie copiado deixa de valer", "Formato proprietário; expira; revogação limitada", "Vida curta + revogação no provedor/refresh"],
    ], larguras=[2.8, 4.4, 4.2, 4.6], titulo="Trade-offs e decisão do esquema")
    r.p("**Decisão:** cookie, porque a SPA é servida pela própria API (proxy em dev, mesma origem em produção), o que evita "
        "expor qualquer token ao JavaScript. Controles: `HttpOnly`, `Secure`, `SameSite=Strict`, expiração de 30 min com "
        "renovação deslizante e **antiforgery** em toda operação que altera estado (cookie legível `XSRF-TOKEN` + cabeçalho "
        "`X-XSRF-TOKEN`, validado por um filtro de endpoint). Se surgirem clientes de outra origem, a recomendação é um "
        "provedor OIDC emitindo JWT de vida curta, sem gravar tokens em log.")
    r.p("**401 × 403 e interface.** Esconder um botão não protege nada: qualquer pessoa pode chamar a API com curl. Por isso "
        "**toda** regra é aplicada no servidor (inclusive nas ações MVC, outro caminho de entrada), e a SPA apenas reflete as "
        "permissões que o próprio servidor calcula em `GET /api/v2/auth/sessao`. **Rotas protegidas na SPA:** `RotaProtegida` "
        "redireciona para `/login` guardando a URL pretendida; a sessão é restaurada a cada carga consultando a API, já que o cookie é ilegível pelo JS.")

    r.h1("3. Código-fonte e evidências")
    r.tabela_dados(["Política", "Requisito", "Aplicada em"], [
        ["Fallback", "Usuário autenticado (padrão seguro)", "Tudo que não for `AllowAnonymous`"],
        ["`LerTarefas`", "Autenticado", "`GET /api/v2/tarefas*`, interface MVC"],
        ["`CriarTarefas`", "Role `Usuario` ou `Administrador`", "`POST /api/v2/tarefas`, MVC Create"],
        ["`EditarTarefa` (por recurso)", "Role + (mesma equipe **ou** claim `permissao=tarefas.editar-outras-equipes`)", "`PUT` e MVC Edit/Concluir, via `IAuthorizationService` após carregar a tarefa"],
        ["`EditarOutrasEquipes`", "Claim `permissao=tarefas.editar-outras-equipes`", "Usada pelo handler acima"],
        ["`ExcluirTarefas`", "Role `Administrador`", "`DELETE`, detalhes de saúde, invalidação de cache"],
    ], larguras=[3.6, 6.4, 6], titulo="Políticas de autorização")
    r.p("Usuários e roles são criados por **seed controlado** (`SementeUsuarios`): idempotente, com senha lida de "
        "`SeedUsuarios:SenhaInicial`; sem essa configuração nenhum usuário é criado. Perfis: ana (Usuario/Backend), bruno "
        "(Usuario/Frontend), carla (Administrador/Backend + claim de outras equipes) e visitante (sem papel). Login e logout: "
        "`POST /api/v2/auth/login` e `/logout` (API) e `ContaController` (MVC, mesmo cookie).", tamanho=10.5)
    r.tabela_dados(["Operação (curl, fora da SPA)", "Anônimo", "visitante", "ana (Backend)", "bruno (Frontend)", "carla (Admin)"], [
        ["Listar tarefas", "401", "200", "200", "200", "200"],
        ["Criar tarefa", "401", "403", "201", "201", "201"],
        ["Editar tarefa da equipe Backend (id 2)", "401", "403", "200", "403", "200"],
        ["Editar tarefa da equipe Frontend (id 8)", "401", "403", "403", "200", "200"],
        ["Excluir tarefa", "401", "403", "403", "403", "204"],
    ], larguras=[5, 1.8, 2, 2.4, 2.4, 2.4], titulo="Matriz 401/403/200 — todas as células verificadas por teste automatizado; as principais também por curl", tamanho=8.5)
    r.figura_img(f"{EV}/08-ana-tarefa-outra-equipe-sem-acoes.png", "Ana (equipe Backend) vê a tarefa da equipe Frontend sem as ações de editar e excluir", 10)

    r.h1("4. Testes e validações obrigatórias")
    r.tabela_dados(["Caso exigido", "Resultado obtido", "Evidência"], [
        ["Login válido e inválido sem revelar a conta", "Senha errada e conta inexistente → mesmo 401 e mesmo texto; válido → 200 + cookie `secure; samesite=strict; httponly`", "`02-login.txt`; Fig. B2"],
        ["Logout e cookie invalidado", "Logout 204; o cookie copiado **antes** do logout passa a receber 401", "`04-logout.txt`; `Logout_invalida_cookie_copiado`"],
        ["401 para não autenticado", "API: 401 Problem Details; MVC: 302 para `/Conta/Login?ReturnUrl=...`", "`03-matriz…`; teste"],
        ["403 para autenticado sem permissão", "visitante cria → 403; ana edita Frontend → 403; bruno exclui → 403", "Tab. 3; 25 casos de matriz"],
        ["Acesso por role/claim e reflexo na SPA", "carla (claim) edita Frontend → 200; SPA oculta “Nova tarefa”, “Concluir” e “Excluir” conforme a sessão", "Fig. 1, B4–B6"],
    ], larguras=[4, 8, 4], titulo="Testes obrigatórios (32 testes de integração .NET + 6 testes de componentes React)")

    r.h1("5. Aprimoramentos")
    r.itens([
        "**Autorização por recurso** (`EditarTarefaHandler`): a decisão depende da equipe da tarefa, avaliada após carregá-la, igual na API e no MVC.",
        "**Auditoria de segurança** em `EventosSeguranca` (login com sucesso/falha, motivo, logout), sem senha, cookie ou token (`05-auditoria-e-usuarios.txt`).",
        "**Testes de integração** reais com cookie e antiforgery (cliente “navegador”), um esquema de autenticação de teste para a matriz e testes de bloqueio após 5 tentativas.",
        "**Defesas adicionais:** CSRF no login e nas mutações (sem token → 400), `LocalRedirect` contra redirecionamento aberto no MVC e rate limiting do login (unidade 7). Limitação conhecida: a diferença de tempo entre “conta inexistente” e “senha errada” não foi equalizada.",
    ], tamanho=10.5)

    r.referencias([
        acesso("Introduction to Identity on ASP.NET Core", "https://learn.microsoft.com/aspnet/core/security/authentication/identity"),
        acesso("Choose an identity management solution", "https://learn.microsoft.com/aspnet/core/security/how-to-choose-identity-solution"),
        acesso("Use Identity to secure a Web API backend for SPAs", "https://learn.microsoft.com/aspnet/core/security/authentication/identity-api-authorization"),
        acesso("Policy-based authorization in ASP.NET Core", "https://learn.microsoft.com/aspnet/core/security/authorization/policies"),
        acesso("Resource-based authorization in ASP.NET Core", "https://learn.microsoft.com/aspnet/core/security/authorization/resourcebased"),
        acesso("Prevent Cross-Site Request Forgery (XSRF/CSRF) attacks in ASP.NET Core", "https://learn.microsoft.com/aspnet/core/security/anti-request-forgery"),
        "OWASP. **Session Management Cheat Sheet**. OWASP Cheat Sheet Series, 2025. Disponível em: https://cheatsheetseries.owasp.org/cheatsheets/Session_Management_Cheat_Sheet.html. Acesso em: 9 nov. 2026.",
        "JONES, M.; BRADLEY, J.; SAKIMURA, N. **RFC 7519: JSON Web Token (JWT)**. IETF, 2015. Disponível em: https://www.rfc-editor.org/rfc/rfc7519. Acesso em: 9 nov. 2026.",
    ])

    r.nova_pagina()
    r.h1("Apêndice A – Código-fonte integral")
    for arq in ("src/PortalTarefas.Web/Seguranca/Politicas.cs", "src/PortalTarefas.Web/Seguranca/SementeUsuarios.cs",
                "src/PortalTarefas.Web/Seguranca/AntiforgeryApiFilter.cs", "src/PortalTarefas.Web/Endpoints/AuthEndpoints.cs",
                "src/PortalTarefas.Web/Controllers/ContaController.cs",
                "src/PortalTarefas.Frontend/src/auth/AuthContext.tsx", "src/PortalTarefas.Frontend/src/auth/RotaProtegida.tsx",
                "src/PortalTarefas.Frontend/src/pages/Login.tsx"):
        r.arquivo_git("87ccb46" if arq.endswith("ContaController.cs") else COMMIT, arq, tamanho=6.8)
    r.codigo('''// Program.cs (trecho): Identity + cookie
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
builder.Services.AddIdentityCore<IdentityUser>(o => { o.Password.RequiredLength = 10; o.Password.RequireNonAlphanumeric = true;
        o.Lockout.MaxFailedAccessAttempts = 5; o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5); o.User.RequireUniqueEmail = true; })
    .AddRoles<IdentityRole>().AddEntityFrameworkStores<PortalTarefasDbContext>().AddSignInManager().AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(o => {
    o.Cookie.Name = "portal.sessao"; o.Cookie.HttpOnly = true; o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    o.Cookie.SameSite = SameSiteMode.Strict; o.ExpireTimeSpan = TimeSpan.FromMinutes(30); o.SlidingExpiration = true;
    o.LoginPath = "/Conta/Login"; o.AccessDeniedPath = "/Conta/AcessoNegado";
    o.Events.OnRedirectToLogin = ctx => ResponderOuRedirecionar(ctx, 401);          // /api -> 401
    o.Events.OnRedirectToAccessDenied = ctx => ResponderOuRedirecionar(ctx, 403);   // /api -> 403
});
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero);
Politicas.Registrar(builder.Services.AddAuthorizationBuilder());
builder.Services.AddSingleton<IAuthorizationHandler, EditarTarefaHandler>();
builder.Services.AddAntiforgery(o => { o.HeaderName = "X-XSRF-TOKEN"; o.Cookie.SecurePolicy = CookieSecurePolicy.Always; o.Cookie.SameSite = SameSiteMode.Strict; });
...
app.UseAuthentication();
app.UseAuthorization();''', "**Configuração de autenticação no Program.cs**", 6.8)

    r.h1("Apêndice B – Evidências de execução")
    r.codigo(bloco_entre(f"{EV}/02-login.txt", "### Login sem token", "### Login como bruno", 60), "**Login: sem antiforgery, senha errada, conta inexistente e válido (valores mascarados)**", 6.5)
    r.codigo(trecho(f"{EV}/04-logout.txt", contem=["###", "HTTP/", "$ curl"], cortar=190), "**Logout e reuso do cookie antigo**", 6.8)
    r.codigo(trecho(f"{EV}/05-auditoria-e-usuarios.txt"), "**Auditoria e usuários do seed**", 6.8)
    r.codigo(trecho(f"{EV}/13-navegacao-spa.txt"), "**Navegação na SPA (Playwright)**", 7)
    r.figuras_apendice(EV, [
        ("06-redirecionado-login.png", "B1", "Rota protegida redireciona para /login"),
        ("07-login-invalido.png", "B2", "Login inválido com mensagem genérica"),
        ("09-ana-tarefa-mesma-equipe.png", "B3", "Ana na tarefa da própria equipe: pode concluir"),
        ("10-carla-admin-com-acoes.png", "B4", "Carla (Administrador + claim) em tarefa de outra equipe: concluir e excluir"),
        ("11-visitante-sem-nova-tarefa.png", "B5", "Visitante: sem o link Nova tarefa"),
        ("12-visitante-rota-nova-bloqueada.png", "B6", "Visitante acessando /tarefas/nova pela URL"),
    ], 9.5)
    return r.salvar("Relatorio-06-Autenticacao-e-Autorizacao.docx")
