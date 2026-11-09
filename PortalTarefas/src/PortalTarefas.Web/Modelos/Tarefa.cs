using PortalTarefas.Web.Contratos;

namespace PortalTarefas.Web.Modelos;

public enum PrioridadeTarefa { Baixa, Media, Alta }

/// <summary>Modelo de domínio da tarefa. Não é exposto diretamente em views nem na API.</summary>
public class Tarefa
{
    public int Id { get; set; }
    public string Titulo { get; set; } = "";
    public string? Descricao { get; set; }
    public SituacaoTarefa Situacao { get; set; } = SituacaoTarefa.Pendente;
    public PrioridadeTarefa Prioridade { get; set; } = PrioridadeTarefa.Media;
    public DateOnly? Prazo { get; set; }
    public DateTimeOffset CriadaEm { get; set; } = DateTimeOffset.UtcNow;

    public bool EstaVencida(DateOnly hoje) => Prazo < hoje && Situacao != SituacaoTarefa.Concluida;
}
