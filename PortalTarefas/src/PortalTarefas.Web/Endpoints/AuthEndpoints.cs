using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using PortalTarefas.Web.Dados;
using PortalTarefas.Web.Modelos;
using PortalTarefas.Web.Seguranca;

namespace PortalTarefas.Web.Endpoints;

public sealed record LoginDto(
    [property: Required(ErrorMessage = "Informe o e-mail."), EmailAddress(ErrorMessage = "E-mail inválido.")] string Email,
    [property: Required(ErrorMessage = "Informe a senha.")] string Senha);

/// <summary>Sessão atual e permissões calculadas no servidor (a SPA apenas as reflete na interface).</summary>
public sealed record SessaoDto(bool Autenticado, string? Usuario, string? Equipe, IReadOnlyList<string> Papeis,
    bool PodeCriar, bool PodeExcluir, bool PodeEditarOutrasEquipes);

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v2/auth").WithTags("Autenticação");

        g.MapGet("/sessao", Sessao).AllowAnonymous().WithName("ObterSessao")
            .WithSummary("Retorna a sessão atual e emite o token antiforgery (cookie XSRF-TOKEN).");
        g.MapPost("/login", Login).AllowAnonymous().WithName("Entrar")
            .WithSummary("Autentica com e-mail e senha e emite o cookie de sessão (HttpOnly, Secure, SameSite=Strict).")
            .ProducesProblem(StatusCodes.Status401Unauthorized);
        g.MapPost("/logout", Logout).WithName("Sair")
            .WithSummary("Encerra a sessão e invalida cookies emitidos anteriormente (troca do security stamp).")
            .ProducesProblem(StatusCodes.Status401Unauthorized);
        return g;
    }

    internal static Ok<SessaoDto> Sessao(HttpContext http, IAntiforgery antiforgery)
    {
        http.EmitirTokenAntiforgery(antiforgery);
        return TypedResults.Ok(CriarSessao(http.User));
    }

    internal static async Task<Results<Ok<SessaoDto>, ProblemHttpResult>> Login(
        LoginDto dto, SignInManager<IdentityUser> signIn, IAntiforgery antiforgery, PortalTarefasDbContext db,
        HttpContext http, ILogger<LoginDto> logger)
    {
        var usuario = await signIn.UserManager.FindByEmailAsync(dto.Email);
        var resultado = usuario is null
            ? SignInResult.Failed
            : await signIn.PasswordSignInAsync(usuario, dto.Senha, isPersistent: false, lockoutOnFailure: true);

        db.EventosSeguranca.Add(new EventoSeguranca
        {
            Evento = "Login", Usuario = dto.Email.Trim().ToLowerInvariant(), Sucesso = resultado.Succeeded,
            Detalhe = resultado.Succeeded ? null : resultado.IsLockedOut ? "bloqueado" : usuario is null ? "conta inexistente" : "senha incorreta"
        });
        await db.SaveChangesAsync();

        if (!resultado.Succeeded)
        {
            logger.LogWarning("Falha de login (bloqueado: {Bloqueado})", resultado.IsLockedOut);
            // Mesma resposta para conta inexistente, senha errada ou bloqueio: não revela se a conta existe.
            return TypedResults.Problem(statusCode: 401, title: "Não foi possível entrar.",
                detail: "E-mail ou senha inválidos, ou acesso temporariamente bloqueado.");
        }

        // O token antiforgery é vinculado à identidade: após o login é preciso emitir um novo.
        http.User = await signIn.CreateUserPrincipalAsync(usuario!);
        http.EmitirTokenAntiforgery(antiforgery);
        return TypedResults.Ok(CriarSessao(http.User));
    }

    internal static async Task<NoContent> Logout(SignInManager<IdentityUser> signIn, PortalTarefasDbContext db, HttpContext http)
    {
        if (await signIn.UserManager.GetUserAsync(http.User) is { } usuario)
            await signIn.UserManager.UpdateSecurityStampAsync(usuario);   // cookies antigos deixam de valer
        await signIn.SignOutAsync();
        db.EventosSeguranca.Add(new EventoSeguranca { Evento = "Logout", Usuario = http.User.Identity?.Name, Sucesso = true });
        await db.SaveChangesAsync();
        http.Response.Cookies.Delete(AntiforgeryExtensions.CookieLegivel);
        return TypedResults.NoContent();
    }

    public static SessaoDto CriarSessao(ClaimsPrincipal u) => u.Identity?.IsAuthenticated == true
        ? new SessaoDto(true, u.Identity.Name, u.Equipe(),
            u.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList(),
            u.IsInRole(Papeis.Usuario) || u.IsInRole(Papeis.Administrador),
            u.IsInRole(Papeis.Administrador),
            u.PodeEditarOutrasEquipes())
        : new SessaoDto(false, null, null, [], false, false, false);
}
