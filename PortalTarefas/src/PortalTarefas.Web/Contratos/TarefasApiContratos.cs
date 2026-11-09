using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using PortalTarefas.Web.Modelos;

namespace PortalTarefas.Web.Contratos;

// DTOs da API v2. A entidade persistida (Tarefa) nunca é exposta diretamente.

/// <summary>Dados para cadastrar uma tarefa.</summary>
public sealed record CriarTarefaDto(
    [property: Required(ErrorMessage = "Informe o título."), StringLength(120, MinimumLength = 3, ErrorMessage = "O título deve ter entre 3 e 120 caracteres."), Description("Título único da tarefa.")] string Titulo,
    [property: StringLength(1000, ErrorMessage = "A descrição deve ter no máximo 1000 caracteres.")] string? Descricao = null,
    PrioridadeTarefa Prioridade = PrioridadeTarefa.Media,
    [property: Description("Data limite (AAAA-MM-DD). Obrigatória para prioridade Alta.")] DateOnly? Prazo = null);

/// <summary>Dados para substituir uma tarefa existente (PUT). Exige a versão lida anteriormente.</summary>
public sealed record AtualizarTarefaDto(
    [property: Required(ErrorMessage = "Informe o título."), StringLength(120, MinimumLength = 3, ErrorMessage = "O título deve ter entre 3 e 120 caracteres.")] string Titulo,
    [property: StringLength(1000, ErrorMessage = "A descrição deve ter no máximo 1000 caracteres.")] string? Descricao,
    SituacaoTarefa Situacao,
    PrioridadeTarefa Prioridade,
    DateOnly? Prazo,
    [property: Required(ErrorMessage = "Informe a versão lida no GET."), Description("Token de concorrência devolvido no GET (Base64).")] string Versao);

/// <summary>Representação resumida usada em listagens.</summary>
public sealed record TarefaResumoDto(int Id, string Titulo, SituacaoTarefa Situacao, PrioridadeTarefa Prioridade, DateOnly? Prazo,
    [property: Description("Verdadeiro quando o prazo já passou e a tarefa não foi concluída (campo adicionado na revisão 2 do contrato).")] bool Vencida);

/// <summary>Representação completa de uma tarefa.</summary>
public sealed record TarefaDetalhesDto(int Id, string Titulo, string? Descricao, SituacaoTarefa Situacao,
    PrioridadeTarefa Prioridade, DateOnly? Prazo, DateTimeOffset CriadaEm,
    [property: Description("Enviar no PUT para detecção de edição concorrente.")] string Versao);

/// <summary>Página de resultados com metadados de navegação.</summary>
public sealed record PaginaDto<T>(IReadOnlyList<T> Itens, int Pagina, int Tamanho, int TotalItens, int TotalPaginas);

/// <summary>Parâmetros de consulta da listagem (query string).</summary>
public sealed class ConsultaTarefasQuery
{
    public const int TamanhoMaximo = 50;
    public static readonly string[] CamposOrdenacao = ["id", "titulo", "prazo", "prioridade", "situacao", "criadaEm"];

    // Propriedades anuláveis = parâmetros opcionais no contrato; os padrões ficam nas propriedades *Efetiva.
    [FromQuery(Name = "pagina")]
    [Range(1, int.MaxValue, ErrorMessage = "A página deve ser maior ou igual a 1.")]
    [Description("Número da página (começa em 1)."), DefaultValue(1)]
    public int? Pagina { get; init; }

    [FromQuery(Name = "tamanho")]
    [Range(1, TamanhoMaximo, ErrorMessage = "O tamanho deve estar entre 1 e 50.")]
    [Description("Itens por página (1 a 50)."), DefaultValue(10)]
    public int? Tamanho { get; init; }

    [FromQuery(Name = "ordenarPor")]
    [Description("Campo de ordenação: id, titulo, prazo, prioridade, situacao ou criadaEm."), DefaultValue("id")]
    public string? OrdenarPor { get; init; }

    [FromQuery(Name = "direcao")]
    [Description("asc ou desc."), DefaultValue("asc")]
    public string? Direcao { get; init; }

    [FromQuery(Name = "situacao")]
    public SituacaoTarefa? Situacao { get; init; }

    [FromQuery(Name = "prioridade")]
    public PrioridadeTarefa? Prioridade { get; init; }

    [FromQuery(Name = "busca")]
    [Description("Trecho do título.")]
    [StringLength(100)]
    public string? Busca { get; init; }

    [FromQuery(Name = "prazoAte")]
    [Description("Somente tarefas com prazo até esta data (inclusive).")]
    public DateOnly? PrazoAte { get; init; }

    public int PaginaEfetiva => Pagina ?? 1;
    public int TamanhoEfetivo => Tamanho ?? 10;
}
