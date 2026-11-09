using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PortalTarefas.Web.Modelos;

namespace PortalTarefas.Web.Dados.Configuracoes;

/// <summary>Mapeamento da tarefa com Fluent API (limites, conversões, índices e concorrência).</summary>
public sealed class TarefaConfiguration : IEntityTypeConfiguration<Tarefa>
{
    public void Configure(EntityTypeBuilder<Tarefa> b)
    {
        b.ToTable("Tarefas");
        b.HasKey(t => t.Id);
        b.Property(t => t.Titulo).IsRequired().HasMaxLength(120);
        b.Property(t => t.Descricao).HasMaxLength(1000);
        // Enums como texto: legível em consultas manuais e estável se a ordem do enum mudar.
        b.Property(t => t.Situacao).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(t => t.Prioridade).HasConversion<string>().HasMaxLength(10).IsRequired();
        b.Property(t => t.Prazo).HasColumnType("date");
        b.Property(t => t.CriadaEm).HasDefaultValueSql("SYSDATETIMEOFFSET()");
        b.Property(t => t.Versao).IsRowVersion();   // rowversion: o SQL Server muda o valor a cada UPDATE

        b.HasIndex(t => t.Titulo).IsUnique();
        // Índice adicionado na 2ª migration: apoia filtros por situação e a busca de vencidas/painel.
        b.HasIndex(t => new { t.Situacao, t.Prazo }).HasDatabaseName("IX_Tarefas_Situacao_Prazo");

        b.HasMany(t => t.Auditoria).WithOne().HasForeignKey(e => e.TarefaId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class EventoAuditoriaConfiguration : IEntityTypeConfiguration<EventoAuditoria>
{
    public void Configure(EntityTypeBuilder<EventoAuditoria> b)
    {
        b.ToTable("EventosAuditoria");
        b.Property(e => e.Acao).IsRequired().HasMaxLength(50);
        b.Property(e => e.Detalhe).HasMaxLength(500);
        b.Property(e => e.Usuario).HasMaxLength(100);
        b.HasIndex(e => new { e.TarefaId, e.OcorridoEm });
    }
}
