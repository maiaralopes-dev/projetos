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

    /// <summary>Equipe dona da tarefa (usada na autorização por recurso).</summary>
    public string Equipe { get; set; } = "Geral";

    /// <summary>Id do usuário (Identity) que criou a tarefa.</summary>
    public string? CriadaPor { get; set; }

    /// <summary>Token de concorrência otimista (rowversion no SQL Server).</summary>
    public byte[] Versao { get; set; } = [];

    public List<EventoAuditoria> Auditoria { get; set; } = [];

    public bool EstaVencida(DateOnly hoje) => Prazo < hoje && Situacao != SituacaoTarefa.Concluida;
}

/// <summary>Registro de auditoria gravado na mesma transação da alteração da tarefa.</summary>
public class EventoAuditoria
{
    public long Id { get; set; }
    public int TarefaId { get; set; }
    public string Acao { get; set; } = "";
    public string? Detalhe { get; set; }
    public string? Usuario { get; set; }
    public DateTimeOffset OcorridoEm { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Lançada quando outra sessão alterou a tarefa depois de ela ter sido lida.</summary>
public sealed class ConflitoConcorrenciaException(Tarefa? valoresAtuais)
    : Exception("A tarefa foi alterada por outra sessão.")
{
    /// <summary>Valores atuais no banco (null se a tarefa foi excluída).</summary>
    public Tarefa? ValoresAtuais { get; } = valoresAtuais;
}

/// <summary>Evento de segurança (login, logout, falha, alteração de permissão). Nunca guarda senha ou token.</summary>
public class EventoSeguranca
{
    public long Id { get; set; }
    public string Evento { get; set; } = "";
    public string? Usuario { get; set; }
    public bool Sucesso { get; set; }
    public string? Detalhe { get; set; }
    public DateTimeOffset OcorridoEm { get; set; } = DateTimeOffset.UtcNow;
}
