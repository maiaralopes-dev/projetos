using System.Collections.Concurrent;
using PortalTarefas.Web.Contratos;
using PortalTarefas.Web.Modelos;

namespace PortalTarefas.Web.Servicos;

/// <summary>Implementação em memória (thread-safe) usada nas unidades 1 e 2.</summary>
public sealed class TarefaServicoMemoria : ITarefaServico
{
    private readonly ConcurrentDictionary<int, Tarefa> _tarefas = new();
    private int _ultimoId;

    public TarefaServicoMemoria(TimeProvider relogio)
    {
        var hoje = DateOnly.FromDateTime(relogio.GetLocalNow().Date);
        Semear(new() { Titulo = "Configurar ambiente .NET 10", Descricao = "Instalar SDK e Visual Studio", Situacao = SituacaoTarefa.Concluida, Prioridade = PrioridadeTarefa.Alta, Prazo = hoje.AddDays(-10) });
        Semear(new() { Titulo = "Montar pipeline HTTP", Descricao = "Middlewares na ordem correta", Situacao = SituacaoTarefa.EmAndamento, Prioridade = PrioridadeTarefa.Media, Prazo = hoje.AddDays(-2) });
        Semear(new() { Titulo = "Escrever relatório 1", Situacao = SituacaoTarefa.Pendente, Prioridade = PrioridadeTarefa.Baixa, Prazo = hoje.AddDays(7) });
    }

    private void Semear(Tarefa t) { t.Id = Interlocked.Increment(ref _ultimoId); t.Versao = NovaVersao(); _tarefas[t.Id] = t; }

    private static Tarefa Copia(Tarefa t) => new()
    {
        Id = t.Id, Titulo = t.Titulo, Descricao = t.Descricao, Situacao = t.Situacao,
        Prioridade = t.Prioridade, Prazo = t.Prazo, CriadaEm = t.CriadaEm, Versao = t.Versao,
        Equipe = t.Equipe, CriadaPor = t.CriadaPor
    };

    private static byte[] NovaVersao() => Guid.NewGuid().ToByteArray();

    public Task<IReadOnlyList<Tarefa>> ListarAsync(SituacaoTarefa? situacao, string? busca, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Tarefa>>(_tarefas.Values
            .Where(t => situacao is null || t.Situacao == situacao)
            .Where(t => string.IsNullOrWhiteSpace(busca) || t.Titulo.Contains(busca, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Id).Select(Copia).ToList());

    public Task<Tarefa?> ObterAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(_tarefas.TryGetValue(id, out var t) ? Copia(t) : null);

    public Task<Tarefa> CriarAsync(Tarefa tarefa, CancellationToken ct = default)
    {
        var nova = Copia(tarefa);
        nova.Id = Interlocked.Increment(ref _ultimoId);
        nova.CriadaEm = DateTimeOffset.UtcNow;
        nova.Versao = NovaVersao();
        _tarefas[nova.Id] = nova;
        return Task.FromResult(Copia(nova));
    }

    public Task<bool> AtualizarAsync(Tarefa tarefa, string? usuario = null, CancellationToken ct = default)
    {
        if (!_tarefas.TryGetValue(tarefa.Id, out var atual)) return Task.FromResult(false);
        if (!atual.Versao.AsSpan().SequenceEqual(tarefa.Versao)) throw new ConflitoConcorrenciaException(Copia(atual));
        var nova = Copia(tarefa);
        nova.CriadaEm = atual.CriadaEm;
        nova.Versao = NovaVersao();
        if (!_tarefas.TryUpdate(tarefa.Id, nova, atual)) throw new ConflitoConcorrenciaException(Copia(_tarefas[tarefa.Id]));
        return Task.FromResult(true);
    }

    public async Task<bool> ConcluirAsync(int id, string? usuario, bool simularFalha = false, CancellationToken ct = default)
    {
        if (await ObterAsync(id, ct) is not { } t) return false;
        if (simularFalha) throw new InvalidOperationException("Falha simulada.");
        t.Situacao = SituacaoTarefa.Concluida;
        return await AtualizarAsync(t, usuario, ct);
    }

    public Task<bool> ExcluirAsync(int id, CancellationToken ct = default) => Task.FromResult(_tarefas.TryRemove(id, out _));

    public Task<bool> ExisteTituloAsync(string titulo, int? ignorarId = null, CancellationToken ct = default) =>
        Task.FromResult(_tarefas.Values.Any(t => t.Id != ignorarId &&
            string.Equals(t.Titulo.Trim(), titulo.Trim(), StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyList<Tarefa>> ListarVencidasAsync(DateOnly hoje, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Tarefa>>(_tarefas.Values.Where(t => t.EstaVencida(hoje))
            .OrderBy(t => t.Prazo).Select(Copia).ToList());
}
