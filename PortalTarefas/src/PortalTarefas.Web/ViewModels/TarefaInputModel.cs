using System.ComponentModel.DataAnnotations;
using PortalTarefas.Web.Contratos;
using PortalTarefas.Web.Modelos;

namespace PortalTarefas.Web.ViewModels;

/// <summary>
/// Dados digitados no formulário de cadastro/edição. Separa a entrada do usuário do
/// modelo de domínio, impedindo overposting de campos como Id e CriadaEm.
/// </summary>
public sealed class TarefaInputModel : IValidatableObject
{
    [Required(ErrorMessage = "Informe o título da tarefa.")]
    [StringLength(120, MinimumLength = 3, ErrorMessage = "O título deve ter entre {2} e {1} caracteres.")]
    [Display(Name = "Título")]
    public string Titulo { get; set; } = "";

    [StringLength(1000, ErrorMessage = "A descrição deve ter no máximo {1} caracteres.")]
    [Display(Name = "Descrição")]
    [DataType(DataType.MultilineText)]
    public string? Descricao { get; set; }

    [Required(ErrorMessage = "Selecione a prioridade.")]
    [Display(Name = "Prioridade")]
    public PrioridadeTarefa? Prioridade { get; set; } = PrioridadeTarefa.Media;

    [Display(Name = "Situação")]
    public SituacaoTarefa Situacao { get; set; } = SituacaoTarefa.Pendente;

    [DataType(DataType.Date)]
    [Display(Name = "Prazo")]
    public DateOnly? Prazo { get; set; }

    /// <summary>Data de referência para a validação (injetável nos testes).</summary>
    internal DateOnly Hoje { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    /// <summary>
    /// Regra que envolve mais de um campo: tarefas de prioridade Alta exigem prazo,
    /// e o prazo de uma tarefa ainda não concluída não pode estar no passado.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Prioridade == PrioridadeTarefa.Alta && Prazo is null)
            yield return new ValidationResult("Tarefas de prioridade Alta precisam de prazo.", [nameof(Prazo)]);

        if (Prazo is { } prazo && prazo < Hoje && Situacao != SituacaoTarefa.Concluida)
            yield return new ValidationResult("O prazo não pode estar no passado para tarefas não concluídas.", [nameof(Prazo)]);
    }

    public static TarefaInputModel De(Tarefa t) => new()
    {
        Titulo = t.Titulo, Descricao = t.Descricao, Prioridade = t.Prioridade, Situacao = t.Situacao, Prazo = t.Prazo
    };

    public void AplicarEm(Tarefa t)
    {
        t.Titulo = Titulo.Trim();
        t.Descricao = string.IsNullOrWhiteSpace(Descricao) ? null : Descricao.Trim();
        t.Prioridade = Prioridade ?? PrioridadeTarefa.Media;
        t.Situacao = Situacao;
        t.Prazo = Prazo;
    }
}
