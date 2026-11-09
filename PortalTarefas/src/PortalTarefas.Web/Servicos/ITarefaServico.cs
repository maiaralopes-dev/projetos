using PortalTarefas.Web.Contratos;
using PortalTarefas.Web.Modelos;

namespace PortalTarefas.Web.Servicos;

/// <summary>
/// Contrato de acesso às tarefas. A interface isola controllers e endpoints da forma
/// de persistência (memória na unidade 2, EF Core a partir da unidade 3).
/// </summary>
public interface ITarefaServico
{
    Task<IReadOnlyList<Tarefa>> ListarAsync(SituacaoTarefa? situacao, string? busca, CancellationToken ct = default);
    Task<Tarefa?> ObterAsync(int id, CancellationToken ct = default);
    Task<Tarefa> CriarAsync(Tarefa tarefa, CancellationToken ct = default);
    /// <summary>Atualiza a tarefa usando <see cref="Tarefa.Versao"/> como token de concorrência.</summary>
    /// <exception cref="ConflitoConcorrenciaException">Outra sessão alterou a tarefa.</exception>
    Task<bool> AtualizarAsync(Tarefa tarefa, string? usuario = null, CancellationToken ct = default);

    /// <summary>Conclui a tarefa e grava auditoria numa única transação explícita.</summary>
    Task<bool> ConcluirAsync(int id, string? usuario, bool simularFalha = false, CancellationToken ct = default);
    Task<bool> ExcluirAsync(int id, CancellationToken ct = default);
    Task<bool> ExisteTituloAsync(string titulo, int? ignorarId = null, CancellationToken ct = default);
    Task<IReadOnlyList<Tarefa>> ListarVencidasAsync(DateOnly hoje, CancellationToken ct = default);
}
