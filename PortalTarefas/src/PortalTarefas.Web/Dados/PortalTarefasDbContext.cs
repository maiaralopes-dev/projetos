using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PortalTarefas.Web.Modelos;

namespace PortalTarefas.Web.Dados;

/// <summary>
/// Unidade de trabalho do EF Core. Registrado com AddDbContextPool: por isso não recebe
/// serviços com estado no construtor nem guarda campos próprios — o pool reinicia apenas
/// o estado conhecido pelo EF (change tracker etc.) ao devolver a instância.
/// </summary>
public class PortalTarefasDbContext(DbContextOptions<PortalTarefasDbContext> options)
    : IdentityDbContext<IdentityUser>(options)   // tabelas AspNetUsers, AspNetRoles, AspNetUserClaims...
{
    public DbSet<Tarefa> Tarefas => Set<Tarefa>();
    public DbSet<EventoAuditoria> Auditoria => Set<EventoAuditoria>();
    public DbSet<EventoSeguranca> EventosSeguranca => Set<EventoSeguranca>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);   // mapeamento do Identity
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PortalTarefasDbContext).Assembly);
    }
}
