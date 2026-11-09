using PortalTarefas.Web.Contratos;
using PortalTarefas.Web.Modelos;

namespace PortalTarefas.Web.ViewModels;

/// <summary>Dados exibidos no resumo de uma tarefa (partial _TarefaResumo).</summary>
public sealed record TarefaResumoViewModel(int Id, string Titulo, SituacaoTarefa Situacao,
    PrioridadeTarefa Prioridade, DateOnly? Prazo, bool Vencida)
{
    public static TarefaResumoViewModel De(Tarefa t, DateOnly hoje) =>
        new(t.Id, t.Titulo, t.Situacao, t.Prioridade, t.Prazo, t.EstaVencida(hoje));
}

public sealed class TarefaListaViewModel
{
    public SituacaoTarefa? Situacao { get; init; }
    public string? Busca { get; init; }
    public IReadOnlyList<TarefaResumoViewModel> Itens { get; init; } = [];
}

public sealed class TarefaDetalhesViewModel
{
    public required TarefaResumoViewModel Resumo { get; init; }
    public string? Descricao { get; init; }
    public DateTimeOffset CriadaEm { get; init; }
}

public sealed class TarefaEdicaoViewModel
{
    public int Id { get; init; }
    public required TarefaInputModel Entrada { get; init; }

    /// <summary>Token de concorrência (rowversion em Base64) lido junto com a tarefa.</summary>
    public string Versao { get; init; } = "";

    /// <summary>Preenchido quando houve conflito: valores atuais gravados por outra sessão.</summary>
    public TarefaInputModel? ValoresAtuais { get; init; }
}
