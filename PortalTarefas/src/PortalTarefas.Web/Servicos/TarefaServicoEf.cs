using Microsoft.EntityFrameworkCore;
using PortalTarefas.Web.Contratos;
using PortalTarefas.Web.Dados;
using PortalTarefas.Web.Modelos;

namespace PortalTarefas.Web.Servicos;

/// <summary>Serviço de tarefas persistido com EF Core (assíncrono, leituras sem rastreamento).</summary>
public sealed class TarefaServicoEf(PortalTarefasDbContext db, ILogger<TarefaServicoEf> logger) : ITarefaServico
{
    public async Task<IReadOnlyList<Tarefa>> ListarAsync(SituacaoTarefa? situacao, string? busca, CancellationToken ct = default)
    {
        var consulta = db.Tarefas.AsNoTracking();
        if (situacao is not null) consulta = consulta.Where(t => t.Situacao == situacao);
        if (!string.IsNullOrWhiteSpace(busca)) consulta = consulta.Where(t => t.Titulo.Contains(busca));
        return await consulta.OrderBy(t => t.Id).ToListAsync(ct);
    }

    public Task<Tarefa?> ObterAsync(int id, CancellationToken ct = default) =>
        db.Tarefas.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<Tarefa> CriarAsync(Tarefa tarefa, CancellationToken ct = default)
    {
        tarefa.Id = 0;
        tarefa.CriadaEm = DateTimeOffset.UtcNow;
        tarefa.Auditoria.Add(new EventoAuditoria { Acao = "Criada", Detalhe = tarefa.Titulo });
        db.Tarefas.Add(tarefa);
        await db.SaveChangesAsync(ct);   // tarefa + auditoria no mesmo SaveChanges = mesma transação implícita
        return tarefa;
    }

    public async Task<bool> AtualizarAsync(Tarefa tarefa, string? usuario = null, CancellationToken ct = default)
    {
        var atual = await db.Tarefas.FirstOrDefaultAsync(t => t.Id == tarefa.Id, ct);
        if (atual is null) return false;

        // A versão enviada pelo cliente passa a ser o "valor original": o UPDATE terá WHERE Versao = @original.
        db.Entry(atual).Property(t => t.Versao).OriginalValue = tarefa.Versao;
        var mudancas = DescreverMudancas(atual, tarefa);
        atual.Titulo = tarefa.Titulo;
        atual.Descricao = tarefa.Descricao;
        atual.Situacao = tarefa.Situacao;
        atual.Prioridade = tarefa.Prioridade;
        atual.Prazo = tarefa.Prazo;
        db.Auditoria.Add(new EventoAuditoria { TarefaId = atual.Id, Acao = "Editada", Detalhe = mudancas, Usuario = usuario });

        try
        {
            await db.SaveChangesAsync(ct);
            tarefa.Versao = atual.Versao;
            return true;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Estratégia explícita: o banco vence. Nada é sobrescrito; o usuário recebe os valores atuais.
            var entrada = ex.Entries.Single(e => e.Entity is Tarefa);
            var valoresBanco = await entrada.GetDatabaseValuesAsync(ct);
            logger.LogWarning("Conflito de concorrência ao editar a tarefa {TarefaId}", tarefa.Id);
            db.ChangeTracker.Clear();
            throw new ConflitoConcorrenciaException((Tarefa?)valoresBanco?.ToObject());
        }
    }

    public async Task<bool> ConcluirAsync(int id, string? usuario, bool simularFalha = false, CancellationToken ct = default)
    {
        // Com EnableRetryOnFailure, transações explícitas precisam rodar dentro da estratégia de execução,
        // para que a unidade inteira (e não só um comando) seja repetida em falhas transitórias.
        var estrategia = db.Database.CreateExecutionStrategy();
        return await estrategia.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transacao = await db.Database.BeginTransactionAsync(ct);

            var tarefa = await db.Tarefas.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (tarefa is null) return false;

            var anterior = tarefa.Situacao;
            tarefa.Situacao = SituacaoTarefa.Concluida;
            await db.SaveChangesAsync(ct);                      // 1º comando: UPDATE Tarefas

            if (simularFalha)
                throw new InvalidOperationException("Falha simulada entre o UPDATE e o registro de auditoria.");

            db.Auditoria.Add(new EventoAuditoria { TarefaId = id, Acao = "Concluida", Detalhe = $"{anterior} -> Concluida", Usuario = usuario });
            await db.SaveChangesAsync(ct);                      // 2º comando: INSERT EventosAuditoria

            await transacao.CommitAsync(ct);                    // sem Commit, o Dispose faz rollback
            return true;
        });
    }

    public Task<bool> ExisteTituloAsync(string titulo, int? ignorarId = null, CancellationToken ct = default)
    {
        var normalizado = titulo.Trim();
        return db.Tarefas.AnyAsync(t => t.Titulo == normalizado && t.Id != ignorarId, ct);   // collation CI do SQL Server
    }

    public async Task<IReadOnlyList<Tarefa>> ListarVencidasAsync(DateOnly hoje, CancellationToken ct = default) =>
        await db.Tarefas.AsNoTracking()
            .Where(t => t.Prazo < hoje && t.Situacao != SituacaoTarefa.Concluida)
            .OrderBy(t => t.Prazo).ToListAsync(ct);

    private static string DescreverMudancas(Tarefa antes, Tarefa depois)
    {
        var partes = new List<string>();
        if (antes.Titulo != depois.Titulo) partes.Add("título");
        if (antes.Descricao != depois.Descricao) partes.Add("descrição");
        if (antes.Situacao != depois.Situacao) partes.Add($"situação {antes.Situacao}->{depois.Situacao}");
        if (antes.Prioridade != depois.Prioridade) partes.Add($"prioridade {antes.Prioridade}->{depois.Prioridade}");
        if (antes.Prazo != depois.Prazo) partes.Add("prazo");
        return partes.Count == 0 ? "sem alterações" : string.Join(", ", partes);
    }
}
