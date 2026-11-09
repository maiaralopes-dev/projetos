using Microsoft.EntityFrameworkCore;
using PortalTarefas.Web.Contratos;
using PortalTarefas.Web.Dados;
using PortalTarefas.Web.Modelos;

namespace PortalTarefas.Web.Servicos;

/// <summary>
/// Leitura paginada da API: filtros, ordenação e paginação são aplicados no IQueryable,
/// portanto traduzidos para SQL (WHERE / ORDER BY / OFFSET FETCH) antes da materialização.
/// </summary>
public sealed class ConsultaTarefasServico(PortalTarefasDbContext db, TimeProvider relogio)
{
    public async Task<PaginaDto<TarefaResumoDto>> ConsultarAsync(ConsultaTarefasQuery q, CancellationToken ct)
    {
        var consulta = db.Tarefas.AsNoTracking();
        var hoje = DateOnly.FromDateTime(relogio.GetLocalNow().Date);

        if (q.Situacao is { } situacao) consulta = consulta.Where(t => t.Situacao == situacao);
        if (q.Prioridade is { } prioridade) consulta = consulta.Where(t => t.Prioridade == prioridade);
        if (!string.IsNullOrWhiteSpace(q.Busca)) consulta = consulta.Where(t => t.Titulo.Contains(q.Busca));
        if (q.PrazoAte is { } prazoAte) consulta = consulta.Where(t => t.Prazo <= prazoAte);

        var total = await consulta.CountAsync(ct);
        var desc = string.Equals(q.Direcao, "desc", StringComparison.OrdinalIgnoreCase);

        // Lista permitida: cada campo aceito mapeia para uma expressão conhecida; Id desempata a ordem.
        IOrderedQueryable<Tarefa> ordenada = (q.OrdenarPor?.ToLowerInvariant() ?? "id") switch
        {
            "titulo" => desc ? consulta.OrderByDescending(t => t.Titulo) : consulta.OrderBy(t => t.Titulo),
            "prazo" => desc ? consulta.OrderByDescending(t => t.Prazo) : consulta.OrderBy(t => t.Prazo),
            "prioridade" => desc ? consulta.OrderByDescending(t => t.Prioridade) : consulta.OrderBy(t => t.Prioridade),
            "situacao" => desc ? consulta.OrderByDescending(t => t.Situacao) : consulta.OrderBy(t => t.Situacao),
            "criadaem" => desc ? consulta.OrderByDescending(t => t.CriadaEm) : consulta.OrderBy(t => t.CriadaEm),
            _ => desc ? consulta.OrderByDescending(t => t.Id) : consulta.OrderBy(t => t.Id),
        };

        var itens = await ordenada.ThenBy(t => t.Id)
            .Skip((q.PaginaEfetiva - 1) * q.TamanhoEfetivo).Take(q.TamanhoEfetivo)
            .Select(t => new TarefaResumoDto(t.Id, t.Titulo, t.Situacao, t.Prioridade, t.Prazo,
                t.Prazo < hoje && t.Situacao != SituacaoTarefa.Concluida))   // projeção
            .ToListAsync(ct);

        return new PaginaDto<TarefaResumoDto>(itens, q.PaginaEfetiva, q.TamanhoEfetivo, total,
            (int)Math.Ceiling(total / (double)q.TamanhoEfetivo));
    }

    public static TarefaDetalhesDto ParaDetalhes(Tarefa t) =>
        new(t.Id, t.Titulo, t.Descricao, t.Situacao, t.Prioridade, t.Prazo, t.CriadaEm, Convert.ToBase64String(t.Versao));
}
