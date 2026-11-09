using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using PortalTarefas.Web.Modelos;

namespace PortalTarefas.Web.Seguranca;

public static class Papeis
{
    public const string Usuario = "Usuario";
    public const string Administrador = "Administrador";
}

public static class Claims
{
    public const string Equipe = "equipe";
    public const string Permissao = "permissao";
    public const string EditarOutrasEquipes = "tarefas.editar-outras-equipes";
}

public static class Politicas
{
    public const string LerTarefas = "LerTarefas";
    public const string CriarTarefas = "CriarTarefas";
    public const string EditarTarefa = "EditarTarefa";              // por recurso (equipe da tarefa)
    public const string EditarOutrasEquipes = "EditarOutrasEquipes"; // baseada em claim
    public const string ExcluirTarefas = "ExcluirTarefas";

    public static void Registrar(AuthorizationBuilder auth) => auth
        // Padrão seguro: tudo exige usuário autenticado, salvo o que for marcado com AllowAnonymous.
        .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
        .AddPolicy(LerTarefas, p => p.RequireAuthenticatedUser())
        .AddPolicy(CriarTarefas, p => p.RequireRole(Papeis.Usuario, Papeis.Administrador))
        .AddPolicy(EditarOutrasEquipes, p => p.RequireClaim(Claims.Permissao, Claims.EditarOutrasEquipes))
        .AddPolicy(EditarTarefa, p => p.AddRequirements(new EditarTarefaRequisito()))
        .AddPolicy(ExcluirTarefas, p => p.RequireRole(Papeis.Administrador));

    public static bool PodeEditarOutrasEquipes(this ClaimsPrincipal u) =>
        u.HasClaim(Claims.Permissao, Claims.EditarOutrasEquipes);

    public static string? Equipe(this ClaimsPrincipal u) => u.FindFirstValue(Claims.Equipe);
}

public sealed class EditarTarefaRequisito : IAuthorizationRequirement;

/// <summary>
/// Autorização por recurso: o usuário (com papel Usuario/Administrador) edita tarefas da própria equipe;
/// tarefas de outra equipe exigem a claim tarefas.editar-outras-equipes.
/// </summary>
public sealed class EditarTarefaHandler : AuthorizationHandler<EditarTarefaRequisito, Tarefa>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext ctx, EditarTarefaRequisito req, Tarefa tarefa)
    {
        var u = ctx.User;
        var temPapel = u.IsInRole(Papeis.Usuario) || u.IsInRole(Papeis.Administrador);
        var mesmaEquipe = string.Equals(u.Equipe(), tarefa.Equipe, StringComparison.OrdinalIgnoreCase);
        if (temPapel && (mesmaEquipe || u.PodeEditarOutrasEquipes()))
            ctx.Succeed(req);
        return Task.CompletedTask;
    }
}
