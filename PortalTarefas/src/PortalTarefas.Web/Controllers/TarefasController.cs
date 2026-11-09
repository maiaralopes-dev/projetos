using Microsoft.AspNetCore.Mvc;
using PortalTarefas.Web.Contratos;
using PortalTarefas.Web.Filtros;
using PortalTarefas.Web.Modelos;
using PortalTarefas.Web.Servicos;
using PortalTarefas.Web.ViewModels;

namespace PortalTarefas.Web.Controllers;

/// <summary>Interface MVC de tarefas. O controller apenas orquestra: valida, chama o serviço e escolhe a view.</summary>
[ServiceFilter<RegistroAcaoFilter>]
public sealed class TarefasController(ITarefaServico servico, TimeProvider relogio) : Controller
{
    private DateOnly Hoje => DateOnly.FromDateTime(relogio.GetLocalNow().Date);

    [HttpGet]
    public async Task<IActionResult> Index(SituacaoTarefa? situacao, string? busca, CancellationToken ct)
    {
        var tarefas = await servico.ListarAsync(situacao, busca, ct);
        return View(new TarefaListaViewModel
        {
            Situacao = situacao,
            Busca = busca,
            Itens = tarefas.Select(t => TarefaResumoViewModel.De(t, Hoje)).ToList()
        });
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var tarefa = await servico.ObterAsync(id, ct);
        if (tarefa is null) return NaoEncontrada(id);
        return View(new TarefaDetalhesViewModel
        {
            Resumo = TarefaResumoViewModel.De(tarefa, Hoje),
            Descricao = tarefa.Descricao,
            CriadaEm = tarefa.CriadaEm
        });
    }

    [HttpGet]
    public IActionResult Create() => View(new TarefaInputModel());

    [HttpPost]
    public async Task<IActionResult> Create(TarefaInputModel entrada, CancellationToken ct)
    {
        await ValidarTituloUnicoAsync(entrada.Titulo, null, ct);
        if (!ModelState.IsValid) return View(entrada);   // preserva o que foi digitado

        var tarefa = new Tarefa();
        entrada.AplicarEm(tarefa);
        var criada = await servico.CriarAsync(tarefa, ct);
        TempData["Mensagem"] = $"Tarefa \"{criada.Titulo}\" cadastrada.";
        return RedirectToAction(nameof(Details), new { id = criada.Id });   // padrão PRG
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var tarefa = await servico.ObterAsync(id, ct);
        if (tarefa is null) return NaoEncontrada(id);
        return View(new TarefaEdicaoViewModel { Id = id, Entrada = TarefaInputModel.De(tarefa) });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, [Bind(Prefix = "Entrada")] TarefaInputModel entrada, CancellationToken ct)
    {
        var tarefa = await servico.ObterAsync(id, ct);
        if (tarefa is null) return NaoEncontrada(id);

        await ValidarTituloUnicoAsync(entrada.Titulo, id, ct);
        if (!ModelState.IsValid) return View(new TarefaEdicaoViewModel { Id = id, Entrada = entrada });

        entrada.AplicarEm(tarefa);
        await servico.AtualizarAsync(tarefa, ct);
        TempData["Mensagem"] = "Alterações salvas.";
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>Validação assíncrona dependente de dados: o título não pode repetir outro existente.</summary>
    private async Task ValidarTituloUnicoAsync(string? titulo, int? idAtual, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(titulo)) return;
        if (await servico.ExisteTituloAsync(titulo, idAtual, ct))
        {
            var chave = idAtual is null ? nameof(TarefaInputModel.Titulo) : $"Entrada.{nameof(TarefaInputModel.Titulo)}";
            ModelState.AddModelError(chave, "Já existe uma tarefa com este título.");
        }
    }

    private ViewResult NaoEncontrada(int id)
    {
        var view = View("NaoEncontrada", id);
        view.StatusCode = StatusCodes.Status404NotFound;
        return view;
    }
}
