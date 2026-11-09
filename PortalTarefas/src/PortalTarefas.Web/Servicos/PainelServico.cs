using Dapper;
using Microsoft.EntityFrameworkCore;
using PortalTarefas.Web.Contratos;
using PortalTarefas.Web.Dados;

namespace PortalTarefas.Web.Servicos;

/// <summary>Linha do painel: totais por situação.</summary>
public sealed record ResumoSituacao(string Situacao, int Quantidade, int Vencidas, DateOnly? ProximoPrazo);

/// <summary>
/// A mesma consulta de leitura em duas versões: EF Core (LINQ + projeção + AsNoTracking)
/// e Dapper (SQL escrito à mão, com parâmetros).
/// </summary>
public sealed class PainelServico(PortalTarefasDbContext db)
{
    public async Task<IReadOnlyList<ResumoSituacao>> ResumoEfAsync(DateOnly hoje, CancellationToken ct = default)
    {
        // Projeção traduzida para um único SELECT ... GROUP BY; só as colunas agregadas trafegam.
        var linhas = await db.Tarefas.AsNoTracking()
            .GroupBy(t => t.Situacao)
            .Select(g => new
            {
                Situacao = g.Key,
                Quantidade = g.Count(),
                Vencidas = g.Count(t => t.Prazo < hoje && t.Situacao != SituacaoTarefa.Concluida),
                ProximoPrazo = g.Where(t => t.Prazo >= hoje).Min(t => t.Prazo)
            })
            .ToListAsync(ct);
        return linhas
            .Select(l => new ResumoSituacao(l.Situacao.ToString(), l.Quantidade, l.Vencidas, l.ProximoPrazo))
            .OrderBy(r => r.Situacao, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<IReadOnlyList<ResumoSituacao>> ResumoDapperAsync(DateOnly hoje, CancellationToken ct = default)
    {
        const string sql = """
            SELECT  Situacao,
                    COUNT(*) AS Quantidade,
                    SUM(CASE WHEN Prazo < @Hoje AND Situacao <> @Concluida THEN 1 ELSE 0 END) AS Vencidas,
                    MIN(CASE WHEN Prazo >= @Hoje THEN Prazo END) AS ProximoPrazo
            FROM    dbo.Tarefas
            GROUP BY Situacao
            ORDER BY Situacao;
            """;
        // Reaproveita a conexão do DbContext (mesmo pool de conexões); valores sempre via parâmetros.
        var conexao = db.Database.GetDbConnection();
        var linhas = await conexao.QueryAsync<LinhaPainel>(new CommandDefinition(sql,
            new { Hoje = hoje.ToDateTime(TimeOnly.MinValue), Concluida = nameof(SituacaoTarefa.Concluida) },
            cancellationToken: ct));
        return linhas.Select(l => new ResumoSituacao(l.Situacao, l.Quantidade, l.Vencidas,
            l.ProximoPrazo is { } d ? DateOnly.FromDateTime(d) : null)).ToList();
    }

    private sealed record LinhaPainel(string Situacao, int Quantidade, int Vencidas, DateTime? ProximoPrazo);
}
