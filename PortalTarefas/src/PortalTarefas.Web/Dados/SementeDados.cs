using Microsoft.EntityFrameworkCore;
using PortalTarefas.Web.Contratos;
using PortalTarefas.Web.Modelos;

namespace PortalTarefas.Web.Dados;

/// <summary>
/// Seed idempotente: só insere quando a tabela está vazia. É executado pelo EF Core
/// (UseSeeding/UseAsyncSeeding) ao aplicar migrations via Migrate() ou "dotnet ef database update".
/// </summary>
public static class SementeDados
{
    public static async Task SemearAsync(DbContext ctx, CancellationToken ct = default)
    {
        var tarefas = ctx.Set<Tarefa>();
        if (await tarefas.AnyAsync(ct)) return;   // já semeado: reiniciar a aplicação não duplica

        tarefas.AddRange(CriarTarefas(DateOnly.FromDateTime(DateTime.Today)));
        await ctx.SaveChangesAsync(ct);
    }

    public static void Semear(DbContext ctx)
    {
        var tarefas = ctx.Set<Tarefa>();
        if (tarefas.Any()) return;
        tarefas.AddRange(CriarTarefas(DateOnly.FromDateTime(DateTime.Today)));
        ctx.SaveChanges();
    }

    private static IEnumerable<Tarefa> CriarTarefas(DateOnly hoje) =>
    [
        new() { Titulo = "Configurar ambiente .NET 10", Descricao = "Instalar SDK e Visual Studio", Situacao = SituacaoTarefa.Concluida, Prioridade = PrioridadeTarefa.Alta, Prazo = hoje.AddDays(-10) },
        new() { Titulo = "Montar pipeline HTTP", Descricao = "Middlewares na ordem correta", Situacao = SituacaoTarefa.EmAndamento, Prioridade = PrioridadeTarefa.Media, Prazo = hoje.AddDays(-2) },
        new() { Titulo = "Escrever relatório 1", Situacao = SituacaoTarefa.Pendente, Prioridade = PrioridadeTarefa.Baixa, Prazo = hoje.AddDays(7) },
        new() { Titulo = "Criar views Razor", Descricao = "Layout, partial e Tag Helpers", Situacao = SituacaoTarefa.Concluida, Prioridade = PrioridadeTarefa.Media, Prazo = hoje.AddDays(-5) },
        new() { Titulo = "Mapear entidades no EF Core", Situacao = SituacaoTarefa.EmAndamento, Prioridade = PrioridadeTarefa.Alta, Prazo = hoje.AddDays(3) },
        new() { Titulo = "Revisar SQL da migration", Situacao = SituacaoTarefa.Pendente, Prioridade = PrioridadeTarefa.Media, Prazo = hoje.AddDays(-1) },
        new() { Titulo = "Testar concorrência", Situacao = SituacaoTarefa.Pendente, Prioridade = PrioridadeTarefa.Alta, Prazo = hoje.AddDays(2) },
        new() { Titulo = "Comparar EF Core e Dapper", Situacao = SituacaoTarefa.Pendente, Prioridade = PrioridadeTarefa.Baixa },
    ];
}
