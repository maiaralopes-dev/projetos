using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HubPedidos.Infrastructure.Persistencia;

public enum MapeamentoMetadados
{
    /// <summary>Uma coluna TEXT com o objeto JSON (padrão; é o que as migrations criam).</summary>
    Json,

    /// <summary>Uma coluna por propriedade (alternativa relacional, usada na comparação).</summary>
    Relacional,
}

public sealed record OpcoesModeloHub(MapeamentoMetadados Metadados = MapeamentoMetadados.Json);

public sealed class HubDbContext(DbContextOptions<HubDbContext> options, OpcoesModeloHub? modelo = null) : DbContext(options)
{
    /// <summary>
    /// SQLite não tem tipo decimal: por padrão o EF grava TEXT e usa funções próprias
    /// (ef_sum, COLLATE EF_DECIMAL) que só existem em conexões abertas pelo EF.
    /// Gravar centavos em INTEGER deixa SUM/ORDER BY nativos e indexáveis.
    /// O domínio já garante no máximo 2 casas decimais.
    /// </summary>
    private static readonly ValueConverter<decimal, long> Centavos = new(
        v => (long)decimal.Round(v * 100m),
        v => v / 100m);

    public MapeamentoMetadados Mapeamento { get; } = modelo?.Metadados ?? MapeamentoMetadados.Json;

    public DbSet<PedidoRegistro> Pedidos => Set<PedidoRegistro>();

    public DbSet<ItemPedidoRegistro> Itens => Set<ItemPedidoRegistro>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        // O modelo muda conforme o mapeamento: o cache de modelos do EF precisa saber disso.
        optionsBuilder.ReplaceService<IModelCacheKeyFactory, ChaveModeloHub>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PedidoRegistro>(e =>
        {
            e.ToTable("Pedidos");
            e.HasKey(p => p.Id);
            e.Property(p => p.ClienteId).HasMaxLength(64).IsRequired();
            e.Property(p => p.Total).HasConversion(Centavos).HasColumnName("TotalCentavos");

            // Índices pensados a partir das consultas reais:
            e.HasIndex(p => new { p.CriadoEmUtc, p.Id }).HasDatabaseName("IX_Pedidos_CriadoEm_Id");       // ordenação/cursor
            // Filtros + mesma ordenação da listagem. O Id entra no fim para que o desempate
            // também venha do índice (sem ele o plano mostrava "USE TEMP B-TREE FOR LAST TERM OF ORDER BY").
            e.HasIndex(p => new { p.ClienteId, p.CriadoEmUtc, p.Id }).HasDatabaseName("IX_Pedidos_Cliente_CriadoEm_Id");
            e.HasIndex(p => new { p.Status, p.CriadoEmUtc, p.Id }).HasDatabaseName("IX_Pedidos_Status_CriadoEm_Id");
            e.HasIndex(p => p.Regiao).HasDatabaseName("IX_Pedidos_Regiao");                                     // painel

            e.ComplexProperty(p => p.Entrega, c =>
            {
                c.Property(x => x.Logradouro).HasMaxLength(120);
                c.Property(x => x.Cidade).HasMaxLength(60);
                c.Property(x => x.Uf).HasMaxLength(2);
                c.Property(x => x.Cep).HasMaxLength(8);
            });

            e.ComplexProperty(p => p.Metadados, c =>
            {
                if (Mapeamento == MapeamentoMetadados.Json)
                {
                    c.ToJson("Metadados");
                }
                else
                {
                    c.Property(x => x.Canal).HasMaxLength(20);
                    c.Property(x => x.Origem).HasMaxLength(40);
                    c.Property(x => x.Campanha).HasMaxLength(40);
                }
            });

            e.HasMany(p => p.Itens).WithOne().HasForeignKey(i => i.PedidoId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ItemPedidoRegistro>(e =>
        {
            e.ToTable("ItensPedido");
            e.HasKey(i => i.Id);
            e.Property(i => i.Sku).HasMaxLength(32).IsRequired();
            e.Property(i => i.PrecoUnitario).HasConversion(Centavos).HasColumnName("PrecoUnitarioCentavos");
            e.HasIndex(i => new { i.PedidoId, i.Sku }).IsUnique().HasDatabaseName("UX_ItensPedido_Pedido_Sku");
        });
    }

    private sealed class ChaveModeloHub : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) =>
            context is HubDbContext hub
                ? (context.GetType(), hub.Mapeamento, designTime)
                : (context.GetType(), designTime);
    }
}

/// <summary>Usado só pelo dotnet-ef para gerar migrations (não conecta em banco real).</summary>
public sealed class HubDbContextDesignFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<HubDbContext>
{
    public HubDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<HubDbContext>().UseSqlite("Data Source=design-time.db").Options);
}
