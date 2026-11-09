using System.ComponentModel.DataAnnotations;

namespace PortalTarefas.Web.Contratos;

public enum SituacaoTarefa { Pendente, EmAndamento, Concluida }

/// <summary>Representação de saída de uma tarefa (unidade 1).</summary>
public sealed record TarefaDto(int Id, string Titulo, string? Descricao, SituacaoTarefa Situacao);

/// <summary>Corpo JSON aceito no cadastro de tarefa.</summary>
public sealed class CriarTarefaRequest
{
    [Required(ErrorMessage = "O título é obrigatório.")]
    [StringLength(120, MinimumLength = 3, ErrorMessage = "O título deve ter entre 3 e 120 caracteres.")]
    public string Titulo { get; set; } = "";

    [StringLength(1000, ErrorMessage = "A descrição deve ter no máximo 1000 caracteres.")]
    public string? Descricao { get; set; }

    [EnumDataType(typeof(SituacaoTarefa), ErrorMessage = "Situação inválida.")]
    public SituacaoTarefa Situacao { get; set; } = SituacaoTarefa.Pendente;
}

/// <summary>Resposta do endpoint diagnóstico, igual nas versões Minimal API e MVC.</summary>
public sealed record DiagnosticoDto(string Abordagem, string Ambiente, string CorrelationId, string? ClienteId, DateTimeOffset Horario);
