using System.ComponentModel.DataAnnotations;

namespace PortalTarefas.Web.Configuracao;

/// <summary>Configuração obrigatória validada na inicialização (ValidateOnStart).</summary>
public sealed class DependenciasOptions
{
    public const string Secao = "Dependencias:Calendario";

    [Required(ErrorMessage = "Dependencias:Calendario:Url é obrigatória."), Url(ErrorMessage = "Dependencias:Calendario:Url deve ser uma URL absoluta.")]
    public string Url { get; set; } = "";
}

public sealed class ConexaoOptions
{
    [Required(ErrorMessage = "ConnectionStrings:PortalTarefas é obrigatória (use User Secrets em dev ou a variável ConnectionStrings__PortalTarefas).")]
    public string? PortalTarefas { get; set; }
}

/// <summary>Identificação da versão implantada (rastreabilidade commit → artefato → execução).</summary>
public sealed record VersaoDto(string Versao, string Commit, string Ambiente);
