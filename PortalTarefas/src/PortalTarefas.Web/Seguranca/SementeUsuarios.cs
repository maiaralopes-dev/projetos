using System.Security.Claims;
using Microsoft.AspNetCore.Identity;

namespace PortalTarefas.Web.Seguranca;

/// <summary>
/// Cria papéis e usuários de teste de forma controlada. A senha inicial NÃO está no repositório:
/// vem de SeedUsuarios:SenhaInicial (User Secrets/variável de ambiente). Sem ela, nenhum usuário é criado.
/// </summary>
public static class SementeUsuarios
{
    private sealed record UsuarioTeste(string Email, string? Papel, string Equipe, string? Permissao = null);

    private static readonly UsuarioTeste[] Usuarios =
    [
        new("ana@portal.local", Papeis.Usuario, "Backend"),
        new("bruno@portal.local", Papeis.Usuario, "Frontend"),
        new("carla@portal.local", Papeis.Administrador, "Backend", Claims.EditarOutrasEquipes),
        new("visitante@portal.local", null, "Geral"),
    ];

    public static async Task ExecutarAsync(IServiceProvider sp, IConfiguration config, ILogger logger)
    {
        var roles = sp.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var papel in new[] { Papeis.Usuario, Papeis.Administrador })
            if (!await roles.RoleExistsAsync(papel)) await roles.CreateAsync(new IdentityRole(papel));

        var senha = config["SeedUsuarios:SenhaInicial"];
        if (string.IsNullOrWhiteSpace(senha))
        {
            logger.LogWarning("SeedUsuarios:SenhaInicial não configurada; usuários de teste não serão criados.");
            return;
        }

        var users = sp.GetRequiredService<UserManager<IdentityUser>>();
        foreach (var u in Usuarios)
        {
            if (await users.FindByEmailAsync(u.Email) is not null) continue;   // idempotente
            var novo = new IdentityUser { UserName = u.Email, Email = u.Email, EmailConfirmed = true };
            var r = await users.CreateAsync(novo, senha);
            if (!r.Succeeded)
                throw new InvalidOperationException("Falha ao criar usuário de teste: " + string.Join("; ", r.Errors.Select(e => e.Code)));
            if (u.Papel is not null) await users.AddToRoleAsync(novo, u.Papel);
            await users.AddClaimAsync(novo, new Claim(Claims.Equipe, u.Equipe));
            if (u.Permissao is not null) await users.AddClaimAsync(novo, new Claim(Claims.Permissao, u.Permissao));
            logger.LogInformation("Usuário de teste {Usuario} criado (papel {Papel}, equipe {Equipe}).", u.Email, u.Papel ?? "nenhum", u.Equipe);
        }
    }
}
