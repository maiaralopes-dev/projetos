using System.Collections.Concurrent;
using PortalTarefas.Web.Contratos;

namespace PortalTarefas.Web.Servicos;

/// <summary>Armazenamento em memória usado na unidade 1 (substituído por EF Core na unidade 3).</summary>
public sealed class RepositorioTarefasMemoria
{
    private readonly ConcurrentDictionary<int, TarefaDto> _tarefas = new();
    private int _ultimoId;

    public RepositorioTarefasMemoria()
    {
        Adicionar("Configurar ambiente .NET 10", "Instalar SDK e Visual Studio", SituacaoTarefa.Concluida);
        Adicionar("Montar pipeline HTTP", "Middlewares na ordem correta", SituacaoTarefa.EmAndamento);
        Adicionar("Escrever relatório 1", null, SituacaoTarefa.Pendente);
    }

    public IEnumerable<TarefaDto> Listar(SituacaoTarefa? situacao, string? busca) =>
        _tarefas.Values
            .Where(t => situacao is null || t.Situacao == situacao)
            .Where(t => string.IsNullOrWhiteSpace(busca) || t.Titulo.Contains(busca, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Id);

    public TarefaDto? Obter(int id) => _tarefas.GetValueOrDefault(id);

    public TarefaDto Adicionar(string titulo, string? descricao, SituacaoTarefa situacao)
    {
        var id = Interlocked.Increment(ref _ultimoId);
        var tarefa = new TarefaDto(id, titulo.Trim(), descricao, situacao);
        _tarefas[id] = tarefa;
        return tarefa;
    }
}
