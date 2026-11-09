using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PortalTarefas.Web.Contratos;
using PortalTarefas.Web.Filtros;
using PortalTarefas.Web.Modelos;
using PortalTarefas.Web.Seguranca;
using PortalTarefas.Web.Servicos;
using PortalTarefas.Web.ViewModels;

namespace PortalTarefas.Web.Controllers;

/// <summary>Interface MVC de tarefas. O controller apenas orquestra: valida, chama o serviço e escolhe a view.</summary>
[ServiceFilter<RegistroAcaoFilter>]
[Authorize(Policy = Politicas.LerTarefas)]
public sealed class TarefasController(ITarefaServico servico, TimeProvider relogio, IAuthorizationService autorizacao) : Controller
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
    [Authorize(Policy = Politicas.CriarTarefas)]
    public IActionResult Create() => View(new TarefaInputModel());

    [HttpPost]
    [Authorize(Policy = Politicas.CriarTarefas)]
    public async Task<IActionResult> Create(TarefaInputModel entrada, CancellationToken ct)
    {
        await ValidarTituloUnicoAsync(entrada.Titulo, null, ct);
        if (!ModelState.IsValid) return View(entrada);   // preserva o que foi digitado

        var tarefa = new Tarefa { Equipe = User.Equipe() ?? "Geral", CriadaPor = User.FindFirstValue(ClaimTypes.NameIdentifier) };
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
        if (!await PodeEditarAsync(tarefa)) return Forbid();
        return View(new TarefaEdicaoViewModel { Id = id, Entrada = TarefaInputModel.De(tarefa), Versao = Convert.ToBase64String(tarefa.Versao) });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, [Bind(Prefix = "Entrada")] TarefaInputModel entrada, string versao, CancellationToken ct)
    {
        var tarefa = await servico.ObterAsync(id, ct);
        if (tarefa is null) return NaoEncontrada(id);
        if (!await PodeEditarAsync(tarefa)) return Forbid();   // a mesma regra da API vale para a interface MVC

        await ValidarTituloUnicoAsync(entrada.Titulo, id, ct);
        if (!ModelState.IsValid) return View(new TarefaEdicaoViewModel { Id = id, Entrada = entrada, Versao = versao });

        entrada.AplicarEm(tarefa);
        tarefa.Versao = Convert.FromBase64String(versao ?? "");   // versão que o usuário viu, não a recém-lida
        try
        {
            await servico.AtualizarAsync(tarefa, User.Identity?.Name, ct);
        }
        catch (ConflitoConcorrenciaException ex) when (ex.ValoresAtuais is { } atuais)
        {
            // Não sobrescreve: mostra o que está no banco e entrega a nova versão para o usuário decidir.
            ModelState.AddModelError("", "Esta tarefa foi alterada por outra pessoa depois que você a abriu. " +
                "Revise os valores atuais e salve novamente se quiser manter suas alterações.");
            return View(new TarefaEdicaoViewModel
            {
                Id = id, Entrada = entrada, Versao = Convert.ToBase64String(atuais.Versao),
                ValoresAtuais = TarefaInputModel.De(atuais)
            });
        }
        catch (ConflitoConcorrenciaException)
        {
            TempData["Mensagem"] = "A tarefa foi excluída por outra sessão.";
            return RedirectToAction(nameof(Index));
        }
        TempData["Mensagem"] = "Alterações salvas.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Concluir(int id, CancellationToken ct)
    {
        var tarefa = await servico.ObterAsync(id, ct);
        if (tarefa is null) return NaoEncontrada(id);
        if (!await PodeEditarAsync(tarefa)) return Forbid();
        if (!await servico.ConcluirAsync(id, User.Identity?.Name, ct: ct)) return NaoEncontrada(id);
        TempData["Mensagem"] = "Tarefa concluída e auditoria registrada.";
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

    private async Task<bool> PodeEditarAsync(Tarefa tarefa) =>
        (await autorizacao.AuthorizeAsync(User, tarefa, Politicas.EditarTarefa)).Succeeded;

    private ViewResult NaoEncontrada(int id)
    {
        var view = View("NaoEncontrada", id);
        view.StatusCode = StatusCodes.Status404NotFound;
        return view;
    }
}
